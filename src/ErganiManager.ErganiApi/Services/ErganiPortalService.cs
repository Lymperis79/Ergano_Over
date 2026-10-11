using System.Text.RegularExpressions;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// Opens the Ergani web portal (trial or production, set in Administration) in Edge/Chrome, picks the
/// login type from the company's User Type, fills the company's saved username/password, logs in and then
/// opens the configured page so the user can just fill in the dates.
/// The portal uses cookieless sessions ("/(S(xxxx))/" in the address), so the session part of the address
/// after login is re-used when going to the target page.
/// </summary>
public sealed class ErganiPortalService : IErganiPortalService
{
    private static readonly Regex SessionSegment = new(@"/\(S\([^)]*\)\)", RegexOptions.Compiled);

    // All browser sessions opened by the application are tracked here so they can
    // be closed cleanly when the application exits.
    private static readonly object DriverGate = new();
    private static readonly HashSet<IWebDriver> OpenDrivers = new();

    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _protector;
    private readonly IPortalSettingsService _settings;
    private readonly ILogger<ErganiPortalService> _logger;

    public event Action<string>? Progress;
    public event Action<string>? ExcelDownloaded;
    private void Report(string message) { try { Progress?.Invoke(message); } catch { } }

    public ErganiPortalService(IConnectionStateService connectionState, ICredentialProtector protector,
        IPortalSettingsService settings, ILogger<ErganiPortalService> logger)
    {
        _connectionState = connectionState;
        _protector = protector;
        _settings = settings;
        _logger = logger;
    }

    public Task<string> OpenAsync(int companyId, DateOnly? from = null, DateOnly? to = null)
        => OpenProfileAsync(companyId, _settings.Current.Schedule, from, to, downloadAfterSearch: true);

    public Task<string> OpenWorkCardsAsync(int companyId, DateOnly date)
        => OpenProfileAsync(companyId, _settings.Current.WorkCards, date, date, downloadAfterSearch: false);

    /// <summary>
    /// Closes every browser and Selenium driver process started by this application.
    /// This must be called before application termination.
    /// </summary>
    public static void ShutdownAllBrowsers()
    {
        IWebDriver[] drivers;
        lock (DriverGate)
        {
            drivers = OpenDrivers.ToArray();
            OpenDrivers.Clear();
        }

        foreach (var driver in drivers)
        {
            try { driver.Quit(); }
            catch (Exception) { /* browser may already have been closed */ }
            try { driver.Dispose(); }
            catch (Exception) { /* driver may already be disposed */ }
        }
    }

    public void Dispose() => ShutdownAllBrowsers();

    private async Task<string> OpenProfileAsync(int companyId, ErganiPortalProfile profile,
        DateOnly? from, DateOnly? to, bool downloadAfterSearch)
    {
        if (!Uri.TryCreate(profile.PortalBaseUrl?.Trim(), UriKind.Absolute, out var baseUri))
            return "❌ The portal address is not set in Administration.";

        string username, password, usertype;
        await using (var db = new AppDbContext(_connectionState.GetDbOptions()))
        {
            var company = await db.Companies.FindAsync(companyId);
            if (company == null) return "❌ Company not found.";
            username = company.ErganiUsername;
            password = _protector.Unprotect(company.ErganiPasswordEncrypted);
            usertype = company.ErganiUsertype;
        }
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return "❌ This company has no Ergani username/password saved.";

        IWebDriver driver;
        var downloadDir = ResolveDownloadFolder(profile);
        try { driver = await Task.Run(() => CreateDriver(downloadDir)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start a browser for the Ergani portal");
            return "❌ Could not start Edge or Chrome: " + ex.Message;
        }

        lock (DriverGate) OpenDrivers.Add(driver);
        _ = Task.Run(() => LoginAndNavigate(driver, baseUri, profile, downloadDir,
            from, to, username, password, usertype, downloadAfterSearch));
        return "🌐 Browser opened — logging in to the Ergani portal…";
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    private static string ResolveDownloadFolder(ErganiPortalProfile settings)
    {
        var dir = string.IsNullOrWhiteSpace(settings.DownloadFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ErganiManager", "Downloads")
            : settings.DownloadFolder.Trim();
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ApplyDownloadPrefs(OpenQA.Selenium.Chromium.ChromiumOptions options, string downloadDir)
    {
        options.AddUserProfilePreference("download.default_directory", downloadDir);
        options.AddUserProfilePreference("download.prompt_for_download", false);
        options.AddUserProfilePreference("download.directory_upgrade", true);
        options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);
    }

    private static IWebDriver CreateDriver(string downloadDir)
    {
        var errors = new List<string>();
        var edgeFirst = OperatingSystem.IsWindows();

        foreach (var edge in edgeFirst ? new[] { true, false } : new[] { false, true })
        {
            try
            {
                if (edge)
                {
                    var service = EdgeDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;
                    var options = new EdgeOptions { LeaveBrowserRunning = false };
                    ApplyDownloadPrefs(options, downloadDir);
                    options.AddArgument("--start-maximized");
                    options.AddExcludedArgument("enable-automation");
                    return new EdgeDriver(service, options);
                }
                else
                {
                    var service = ChromeDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;
                    var options = new ChromeOptions { LeaveBrowserRunning = false };
                    ApplyDownloadPrefs(options, downloadDir);
                    options.AddArgument("--start-maximized");
                    options.AddExcludedArgument("enable-automation");
                    return new ChromeDriver(service, options);
                }
            }
            catch (Exception ex) { errors.Add($"{(edge ? "Edge" : "Chrome")}: {ex.Message}"); }
        }

        throw new InvalidOperationException(
            "Install Microsoft Edge or Google Chrome. (" + string.Join(" | ", errors) + ")");
    }

    // ── Login flow ────────────────────────────────────────────────────────────

    private void LoginAndNavigate(IWebDriver driver, Uri baseUri, ErganiPortalProfile settings, string downloadDir,
        DateOnly? from, DateOnly? to, string username, string password, string usertype, bool downloadAfterSearch)
    {
        try
        {
            driver.Navigate().GoToUrl(baseUri.ToString());

            ChooseLoginType(driver, usertype);

            var passwordBox = WaitFor(driver, TimeSpan.FromSeconds(30), () => FirstDisplayed(driver, By.CssSelector("input[type='password']")));
            if (passwordBox == null) { _logger.LogWarning("Ergani portal: login form not found"); return; }

            var userBox = FindUsernameBox(passwordBox);
            if (userBox != null) { userBox.Clear(); userBox.SendKeys(username); }
            passwordBox.Clear();
            passwordBox.SendKeys(password);
            passwordBox.SendKeys(Keys.Enter);

            // Wait (up to 5 min, in case the user has to complete a verification step) until we leave the login page.
            var loggedIn = WaitFor(driver, TimeSpan.FromMinutes(5),
                () => !driver.Url.Contains("login.aspx", StringComparison.OrdinalIgnoreCase) ? driver.Url : (string?)null);
            if (loggedIn == null) { _logger.LogWarning("Ergani portal: still on the login page"); return; }

            if (!string.IsNullOrWhiteSpace(settings.TargetPageUrl))
                driver.Navigate().GoToUrl(BuildTargetUrl(settings.TargetPageUrl, baseUri, driver.Url));

            if (from is { } f && to is { } t)
            {
                var okFrom = FillDate(driver, settings.FromDateFieldId, f, settings.DateFormat);
                var okTo   = FillDate(driver, settings.ToDateFieldId, t, settings.DateFormat);

                if (settings.AutoSearch)
                {
                    if (okFrom && okTo)
                    {
                        Search(driver, settings);
                        if (downloadAfterSearch && settings.AutoDownload) DownloadExcel(driver, settings, downloadDir);
                    }
                    else Report("⚠ The date boxes were not found on the page, so the search was not started.");
                }
            }
        }
        catch (Exception ex)
        {
            // Includes the user closing the browser early
            _logger.LogWarning(ex, "Ergani portal automation stopped");
        }
    }

    /// <summary>Clicks "Σύνδεση με κωδικούς ΕΡΓΑΝΗ" (User Type 01/02) or the EFKA construction-works option (03).</summary>
    private static void ChooseLoginType(IWebDriver driver, string usertype)
    {
        var xpath = usertype == "03"
            ? "//*[self::a or self::button or self::label or self::input or self::span][contains(normalize-space(.),'Οικοδομοτεχνικά') or contains(@value,'Οικοδομοτεχνικά')]"
            : "//*[self::a or self::button or self::label or self::input or self::span][(contains(normalize-space(.),'Σύνδεση με κωδικούς') and contains(normalize-space(.),'ΕΡΓΑΝΗ') and not(contains(normalize-space(.),'Οικοδομοτεχνικά'))) or contains(@value,'ΕΡΓΑΝΗ')]";

        // If the password box is already there, no choice is needed
        if (WaitFor(driver, TimeSpan.FromSeconds(4), () => FirstDisplayed(driver, By.CssSelector("input[type='password']"))) != null)
            return;

        var option = WaitFor(driver, TimeSpan.FromSeconds(10), () => FirstDisplayed(driver, By.XPath(xpath)));
        try { option?.Click(); }
        catch { ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", option); }
    }

    /// <summary>
    /// Types a date into a date box of the page. The portal's date boxes are web-forms date editors, so the
    /// editor's own script (SetDate) is used when it exists; otherwise the input is filled directly.
    /// Waits for the box to appear (the page may still be loading).
    /// </summary>
    private bool FillDate(IWebDriver driver, string fieldId, DateOnly date, string format)
    {
        if (string.IsNullOrWhiteSpace(fieldId)) return false;
        var text = date.ToString(string.IsNullOrWhiteSpace(format) ? "dd/MM/yyyy" : format);

        const string script = @"
            var id = arguments[0], text = arguments[1], y = arguments[2], m = arguments[3], d = arguments[4];
            var c = window[id];
            if (c && typeof c.SetDate === 'function') { c.SetDate(new Date(y, m - 1, d)); return 'editor'; }
            var el = document.getElementById(id + '_I') || document.getElementById(id);
            if (el && el.tagName !== 'INPUT') { var inner = el.querySelector('input[type=text]'); if (inner) el = inner; }
            if (el && el.tagName === 'INPUT') {
                el.focus();
                el.value = text;
                el.dispatchEvent(new Event('input',  { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
                el.blur();
                return 'input';
            }
            return null;";

        var js = (IJavaScriptExecutor)driver;
        var result = WaitFor(driver, TimeSpan.FromSeconds(20),
            () => js.ExecuteScript(script, fieldId, text, date.Year, date.Month, date.Day) as string);
        if (result == null)
            _logger.LogWarning("Ergani portal: date box {Id} was not found on the page", fieldId);
        return result != null;
    }

    /// <summary>Presses Search, waits for the results, clicks the Excel export icon and waits for the file.</summary>
    private void Search(IWebDriver driver, ErganiPortalProfile settings)
    {
        Report("🔎 Searching…");
        var search = WaitFor(driver, TimeSpan.FromSeconds(20),
            () => driver.FindElements(By.Id(settings.SearchButtonId)).FirstOrDefault(SafeDisplayed));
        if (search == null) { Report("❌ The Search button was not found on the page."); return; }
        Click(driver, search);
        WaitForPageIdle(driver);
        Report("✅ Search completed.");
    }

    private void DownloadExcel(IWebDriver driver, ErganiPortalProfile settings, string downloadDir)
    {
        var export = WaitFor(driver, TimeSpan.FromSeconds(30), () =>
            FirstDisplayed(driver, By.CssSelector("img.ExcelExport"))
            ?? FirstDisplayed(driver, By.XPath("//img[contains(@alt,'Excel')]")));
        if (export == null) { Report("❌ The Excel export icon was not found (no results for this period?)."); return; }
        var before = Directory.GetFiles(downloadDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Report("⬇ Exporting to Excel…");
        Click(driver, export);
        var file = WaitForDownload(downloadDir, before, TimeSpan.FromSeconds(120));
        Report(file != null ? $"✅ Excel file saved: {file}" : "❌ The download did not complete within 2 minutes.");
        if (file != null)
        {
            try { ExcelDownloaded?.Invoke(file); }
            catch (Exception ex) { _logger.LogWarning(ex, "ExcelDownloaded handler failed"); }
        }
    }

    private static void Click(IWebDriver driver, IWebElement element)
    {
        try { element.Click(); }
        catch { ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element); }
    }

    /// <summary>Waits until the page finished loading and no ASP.NET partial postback is running.</summary>
    private static void WaitForPageIdle(IWebDriver driver)
    {
        Thread.Sleep(800);   // let the postback start
        var js = (IJavaScriptExecutor)driver;
        WaitFor(driver, TimeSpan.FromSeconds(30), () =>
        {
            var idle = js.ExecuteScript(@"
                if (document.readyState !== 'complete') return false;
                try {
                    if (window.Sys && Sys.WebForms && Sys.WebForms.PageRequestManager) {
                        return !Sys.WebForms.PageRequestManager.getInstance().get_isInAsyncPostBack();
                    }
                } catch (e) {}
                return true;");
            return idle is true ? "idle" : null;
        });
    }

    /// <summary>Waits for a new, completely written file to appear in the download folder.</summary>
    private static string? WaitForDownload(string dir, HashSet<string> before, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        string? candidate = null;
        long lastSize = -1;

        while (DateTime.UtcNow < end)
        {
            var fresh = Directory.GetFiles(dir)
                .Where(f => !before.Contains(f))
                .Where(f => !f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (fresh != null)
            {
                var size = new FileInfo(fresh).Length;
                if (fresh == candidate && size == lastSize && size > 0) return fresh;   // size stable for one poll
                candidate = fresh;
                lastSize = size;
            }
            Thread.Sleep(700);
        }
        return null;
    }

    private static IWebElement? FindUsernameBox(IWebElement passwordBox)
    {
        var candidates = passwordBox.FindElements(By.XPath(
            "preceding::input[@type='text' or @type='email' or not(@type)]"));
        return candidates.LastOrDefault(e => SafeDisplayed(e));
    }

    private static IWebElement? FirstDisplayed(IWebDriver driver, By by) =>
        driver.FindElements(by).FirstOrDefault(SafeDisplayed);

    private static bool SafeDisplayed(IWebElement e)
    {
        try { return e.Displayed; } catch { return false; }
    }

    private static T? WaitFor<T>(IWebDriver driver, TimeSpan timeout, Func<T?> probe) where T : class
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            try { var result = probe(); if (result != null) return result; }
            catch (StaleElementReferenceException) { }
            catch (NoSuchElementException) { }
            Thread.Sleep(300);
        }
        return null;
    }

    /// <summary>
    /// Builds the address of the target page inside the current (cookieless) session:
    /// https://host/(S(session))/Path/Page.aspx?query — any session part typed in the setting is replaced.
    /// </summary>
    internal static string BuildTargetUrl(string target, Uri baseUri, string currentUrl)
    {
        var session = SessionSegment.Match(currentUrl).Value;               // "" when cookies are used
        var cleaned = SessionSegment.Replace(target.Trim(), string.Empty);

        var uri = Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(baseUri, cleaned.TrimStart('/'));

        return $"{uri.Scheme}://{uri.Authority}{session}{uri.AbsolutePath}{uri.Query}";
    }
}
