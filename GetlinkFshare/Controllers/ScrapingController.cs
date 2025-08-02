using GetlinkFshare.Models;
using GetlinkFshare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text.RegularExpressions;

namespace GetlinkFshare.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScrapingController : ControllerBase
    {
        private readonly PuppeteerService _puppeteerService;
        private readonly ILogger<ScrapingController> _logger;
        private readonly IMemoryCache _cache;

        public ScrapingController(PuppeteerService puppeteerService, ILogger<ScrapingController> logger, IMemoryCache cache)
        {
            _puppeteerService = puppeteerService;
            _logger = logger;
            _cache = cache;
        }

        [HttpGet("prepare-download")]
        [Authorize]
        public async Task<IActionResult> PrepareDownload([FromQuery] string fshareUrl)
        {
            if (string.IsNullOrEmpty(fshareUrl) || !Uri.TryCreate(fshareUrl, UriKind.Absolute, out _))
            {
                return BadRequest("Fshare URL không hợp lệ.");
            }

            var (directLink, cookies, fileName, fileSize, userAgent, refererUrl) = await _puppeteerService.GetDownloadInfoAsync(fshareUrl);

            if (string.IsNullOrEmpty(directLink) || cookies == null || string.IsNullOrEmpty(userAgent) || string.IsNullOrEmpty(refererUrl))
            {
                return StatusCode(500, "Không thể lấy thông tin tải file từ Fshare.");
            }

            var token = Guid.NewGuid().ToString();
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

            var downloadInfo = new DownloadInfo { DirectLink = directLink, Cookies = cookies, FileSize = fileSize, UserAgent = userAgent, RefererUrl = refererUrl };
            _cache.Set(token, downloadInfo, cacheEntryOptions);

            var proxyUrl = Url.Action("ExecuteDownload", "Scraping", new { token = token }, Request.Scheme);

            return Ok(new { proxyUrl = proxyUrl, fileName = fileName, fileSize = fileSize });
        }

        [HttpGet("execute-download")]
        [AllowAnonymous]
        public async Task ExecuteDownload([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token) || !_cache.TryGetValue(token, out DownloadInfo downloadInfo))
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await Response.WriteAsync("Link tải không hợp lệ hoặc đã hết hạn.");
                return;
            }

            var fileName = "unknown";
            try
            {
                var cookieContainer = new CookieContainer();
                foreach (var cookie in downloadInfo.Cookies)
                {
                    cookieContainer.Add(new Uri(downloadInfo.DirectLink), new Cookie(cookie.Name, cookie.Value));
                }

                // *** ĐÃ SỬA: Tạo HttpClient với handler chứa cookie một cách chính xác ***
                var handler = new HttpClientHandler { CookieContainer = cookieContainer, UseCookies = true };
                using var client = new HttpClient(handler);

                var request = new HttpRequestMessage(HttpMethod.Get, downloadInfo.DirectLink);

                request.Headers.UserAgent.ParseAdd(downloadInfo.UserAgent);
                request.Headers.Referrer = new Uri(downloadInfo.RefererUrl);

                if (Request.Headers.TryGetValue("Range", out var rangeHeader))
                {
                    request.Headers.Add("Range", rangeHeader.ToString());
                }

                var fshareResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);

                Response.StatusCode = (int)fshareResponse.StatusCode;

                var allowedResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Content-Length", "Content-Type", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified", "Date"
            };

                Action<KeyValuePair<string, IEnumerable<string>>> copyHeader = (header) =>
                {
                    if (allowedResponseHeaders.Contains(header.Key))
                    {
                        try { Response.Headers[header.Key] = header.Value.ToArray(); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Không thể sao chép header '{HeaderName}'", header.Key); }
                    }
                };

                foreach (var header in fshareResponse.Headers) copyHeader(header);
                foreach (var header in fshareResponse.Content.Headers) copyHeader(header);

                fileName = WebUtility.UrlDecode(Path.GetFileName(new Uri(downloadInfo.DirectLink).AbsolutePath));

                //Sửa lỗi sai tên file khi có ký tự đặc biệt
                var sanitizedFileNameFallback = Regex.Replace(fileName, @"[^\u0020-\u007E]", "_");
                Response.Headers["Content-Disposition"] = $"attachment; filename=\"{sanitizedFileNameFallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";

                var fshareStream = await fshareResponse.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                await fshareStream.CopyToAsync(Response.Body, HttpContext.RequestAborted);

                //_logger.LogWarning("--> Đã hoàn thành proxy file '{FileName}' cho người dùng.", fileName);
            }
            catch (OperationCanceledException)
            {
                //_logger.LogInformation("--> Người dùng đã hủy tải file '{FileName}'.", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi proxy file '{FileName}' với token {Token}.", fileName, token);
                if (!Response.HasStarted)
                {
                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    await Response.WriteAsync("Đã xảy ra lỗi trong quá trình tải file.");
                }
            }
        }
    }
}
