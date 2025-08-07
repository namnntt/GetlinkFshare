using GetlinkFshare.Models;
using PuppeteerSharp;
using System.Net;

namespace GetlinkFshare.Services
{
    public class PuppeteerService : IAsyncDisposable
    {
        private readonly IBrowser _browser;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PuppeteerService> _logger;

        public PuppeteerService(IConfiguration configuration, ILogger<PuppeteerService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _logger.LogInformation("Đang khởi tạo PuppeteerService...");

            var browserInitializationTask = InitializeBrowserAsync();
            browserInitializationTask.Wait();
            _browser = browserInitializationTask.Result;

            LoginToTargetSite().Wait();
        }

        private async Task<IBrowser> InitializeBrowserAsync()
        {
            _logger.LogInformation("--> Đang tải trình duyệt (nếu cần)...");
            await new BrowserFetcher().DownloadAsync();
            _logger.LogInformation("--> Tải trình duyệt hoàn tất.");

            var launchOptions = new LaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage", "--disable-gpu" }
            };

            _logger.LogInformation("--> Đang khởi chạy trình duyệt...");
            var browser = await Puppeteer.LaunchAsync(launchOptions);

            var client = await browser.Target.CreateCDPSessionAsync();
            await client.SendAsync("Browser.setDownloadBehavior", new { behavior = "deny" });

            _logger.LogInformation("--> Hành vi download mặc định đã được đặt thành 'deny'.");
            _logger.LogInformation("--> Khởi chạy trình duyệt thành công!");
            return browser;
        }

        private async Task LoginToTargetSite()
        {
            var page = await _browser.NewPageAsync();
            try
            {
                var credentials = _configuration.GetSection("TargetWebsite");
                await page.GoToAsync(credentials["LoginUrl"], new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });

                await page.TypeAsync("#loginform-email", credentials["Username"]);
                await page.TypeAsync("#loginform-password", credentials["Password"]);
                await page.ClickAsync("#loginform-rememberme");
                await page.ClickAsync("form#form-signup button[type='submit']");

                await page.WaitForSelectorAsync("div.user__profile", new WaitForSelectorOptions { Timeout = 15000 });
                _logger.LogInformation("Đăng nhập vào Fshare.vn THÀNH CÔNG!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LỖI nghiêm trọng khi đăng nhập vào Fshare.vn.");
                throw;
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        public async Task<DownloadInfo?> GetDownloadInfoAsync(string fshareUrl)
        {
            var page = await _browser.NewPageAsync();
            try
            {
                _logger.LogInformation("Đang xử lý Fshare URL: {fshareUrl}", fshareUrl);

                // *** ĐÃ THAY ĐỔI: Không còn bắt header nữa ***
                var tcs = new TaskCompletionSource<(string Url, long? FileSize)>();

                page.Response += (sender, e) =>
                {
                    if (e.Response.Headers.TryGetValue("content-disposition", out var contentDisposition) && contentDisposition.Contains("attachment"))
                    {
                        long? fileSize = null;
                        if (e.Response.Headers.TryGetValue("content-length", out var lengthStr) && long.TryParse(lengthStr, out var length))
                        {
                            fileSize = length;
                        }
                        tcs.TrySetResult((e.Response.Url, fileSize));
                    }
                };

                var navigationTask = page.GoToAsync(fshareUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });

                await Task.WhenAny(navigationTask, tcs.Task);

                if (!tcs.Task.IsCompleted)
                {
                    await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }

                var (directLink, fileSize) = await tcs.Task;

                if (string.IsNullOrEmpty(directLink)) return null;

                var allCookies = await page.GetCookiesAsync(fshareUrl, directLink);
                var fshareCookies = allCookies.Where(c => c.Domain == ".fshare.vn").ToArray();
                var fileName = WebUtility.UrlDecode(Path.GetFileName(new Uri(directLink).AbsolutePath));

                var userAgent = await _browser.GetUserAgentAsync();
                var refererUrl = fshareUrl;

                return new DownloadInfo
                {
                    OriginalFshareUrl = fshareUrl,
                    DirectLink = directLink,
                    Cookies = fshareCookies,
                    FileSize = fileSize,
                    UserAgent = userAgent,
                    RefererUrl = refererUrl,
                    FileName = fileName,
                   
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin download cho {fshareUrl}.", fshareUrl);
                await page.ScreenshotAsync($"./error_screenshot_{DateTime.Now:yyyyMMddHHmmss}.png");
                return null;
            }
            finally
            {
                if (!page.IsClosed)
                {
                    await page.CloseAsync();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_browser != null && !_browser.IsClosed)
            {
                await _browser.CloseAsync();
            }
        }
    }


}
