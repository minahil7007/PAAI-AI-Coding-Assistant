using Microsoft.Playwright;

namespace PAAI.Services;

public class BrowserService
{
    public event Action<string>? BrowserErrorDetected;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private bool _isRunning = false;

    public async Task StartWatchingAsync(string url)
    {
        try
        {
            if (_isRunning) await StopAsync();

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false
            });

            _page = await _browser.NewPageAsync();

            // Console errors pakdo
            _page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                    BrowserErrorDetected?.Invoke($"Browser Console Error:\n{msg.Text}");
            };

            // Page errors pakdo
            _page.PageError += (_, error) =>
            {
                BrowserErrorDetected?.Invoke($"Browser Page Error:\n{error}");
            };

            // Request failures pakdo
            _page.RequestFailed += (_, request) =>
            {
                BrowserErrorDetected?.Invoke(
                    $"Network Error:\nURL: {request.Url}\nReason: {request.Failure}");
            };

            await _page.GotoAsync(url);
            _isRunning = true;
        }
        catch (Exception ex)
        {
            BrowserErrorDetected?.Invoke($"Browser start error: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_page != null) await _page.CloseAsync();
            if (_browser != null) await _browser.CloseAsync();
            _playwright?.Dispose();
        }
        catch { }
        finally
        {
            _isRunning = false;
            _page = null;
            _browser = null;
            _playwright = null;
        }
    }
}