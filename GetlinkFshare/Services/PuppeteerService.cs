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

            // *** ĐÃ SỬA: Quay lại phương pháp tải và khởi chạy đơn giản hơn ***
            var browserInitializationTask = InitializeBrowserAsync();
            browserInitializationTask.Wait();
            _browser = browserInitializationTask.Result;

            LoginToTargetSite().Wait();
        }

        private async Task<IBrowser> InitializeBrowserAsync()
        {
            _logger.LogInformation("--> Đang tải trình duyệt (nếu cần)...");
            // Để Puppeteer tự quản lý việc tải về thư mục mặc định.
            // Điều này sẽ hoạt động tốt vì chúng ta đã cấp quyền cho thư mục ứng dụng.
            await new BrowserFetcher().DownloadAsync();
            _logger.LogInformation("--> Tải trình duyệt hoàn tất.");

            var launchOptions = new LaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu"
            }
            };

            _logger.LogInformation("--> Đang khởi chạy trình duyệt...");
            var browser = await Puppeteer.LaunchAsync(launchOptions);
            _logger.LogInformation("--> Khởi chạy trình duyệt thành công!");
            return browser;
        }

        private async Task LoginToTargetSite()
        {
            _logger.LogInformation("--> Bắt đầu quá trình đăng nhập vào Fshare.vn...");
            var page = await _browser.NewPageAsync();
            try
            {
                var credentials = _configuration.GetSection("TargetWebsite");
                var loginUrl = credentials["LoginUrl"];
                var username = credentials["Username"];
                var password = credentials["Password"];

                await page.GoToAsync(loginUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });

                const string emailSelector = "#loginform-email";
                await page.WaitForSelectorAsync(emailSelector);
                await page.TypeAsync(emailSelector, username);
                await page.TypeAsync("#loginform-password", password);
                await page.ClickAsync("#loginform-rememberme");
                await page.ClickAsync("form#form-signup button[type='submit']");

                await page.WaitForSelectorAsync("div.user__profile", new WaitForSelectorOptions { Timeout = 15000 });
                _logger.LogInformation("--> Đăng nhập vào Fshare.vn THÀNH CÔNG!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--> LỖI nghiêm trọng khi đăng nhập vào Fshare.vn.");
                await page.ScreenshotAsync($"./fshare_login_error_{DateTime.Now:yyyyMMddHHmmss}.png");
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        public async Task<(string? directLink, CookieParam[]? cookies, string? fileName, long? fileSize, string? userAgent, string? refererUrl)> GetDownloadInfoAsync(string fshareUrl)
        {
            var page = await _browser.NewPageAsync();
            try
            {
                _logger.LogInformation($"--> Đang xử lý Fshare URL: {fshareUrl}");

                var client = await page.Target.CreateCDPSessionAsync();
                await client.SendAsync("Page.setDownloadBehavior", new { behavior = "deny" });

                var tcs = new TaskCompletionSource<(string Url, long? FileSize)>();

                page.Response += (sender, e) =>
                {
                    if (e.Response.Headers.TryGetValue("content-disposition", out var contentDisposition) && contentDisposition.Contains("attachment"))
                    {
                        _logger.LogInformation($"--> Đã bắt được file download với Content-Disposition '{contentDisposition}': {e.Response.Url}");
                        long? fileSize = null;
                        if (e.Response.Headers.TryGetValue("content-length", out var lengthStr) && long.TryParse(lengthStr, out var length))
                        {
                            fileSize = length;
                        }
                        tcs.TrySetResult((e.Response.Url, fileSize));
                    }
                };

                // *** ĐÃ SỬA: Chạy GoToAsync và việc chờ link song song, lấy kết quả của tác vụ hoàn thành trước ***
                var navigationTask = page.GoToAsync(fshareUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });

                await Task.WhenAny(navigationTask, tcs.Task);

                // Nếu tcs.Task chưa hoàn thành (tức là navigationTask xong trước hoặc bị lỗi), cho nó thêm một cơ hội
                if (!tcs.Task.IsCompleted)
                {
                    await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }

                var (directLink, fileSize) = await tcs.Task; // Lấy kết quả

                if (string.IsNullOrEmpty(directLink)) return (null, null, null, null, null, null);

                var cookies = await page.GetCookiesAsync();
                var encodedFileName = Path.GetFileName(new Uri(directLink).AbsolutePath);
                var fileName = WebUtility.UrlDecode(encodedFileName);
                var userAgent = await _browser.GetUserAgentAsync();
                var refererUrl = page.Url;

                return (directLink, cookies, fileName, fileSize, userAgent, refererUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"--> Lỗi khi lấy thông tin download cho {fshareUrl}.");
                return (null, null, null, null, null, null);
            }
            finally
            {
                // Đảm bảo tab luôn được đóng
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
