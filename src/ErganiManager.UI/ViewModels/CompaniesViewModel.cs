using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.ErganiApi;
using ErganiManager.ErganiApi.Models;
using ErganiManager.ErganiApi.Services;

namespace ErganiManager.UI.ViewModels;

public partial class CompaniesViewModel :
ViewModelBase,
IAdminSectionViewModel
{
    private readonly ICompanyService _companyService;
    private readonly IErganiDataImportService _erganiImport;
    private readonly ICacheSyncService _cacheSyncService;
    private readonly IEmailAlertService _emailAlertService;

    private UserSession? _session;

    // ── Company picture ──
    [ObservableProperty] private byte[]? _formLogo;
    private bool _logoChanged;

    /// <summary>Preview of <see cref="FormLogo"/> shown in the form (null = no picture).</summary>
    public Bitmap? FormLogoImage
    {
        get
        {
            if (FormLogo is not { Length: > 0 }) return null;
            try { return new Bitmap(new System.IO.MemoryStream(FormLogo)); }
            catch { return null; }
        }
    }

    public bool HasFormLogo => FormLogo is { Length: > 0 };

    partial void OnFormLogoChanged(byte[]? value)
    {
        OnPropertyChanged(nameof(FormLogoImage));
        OnPropertyChanged(nameof(HasFormLogo));
    }

    /// <summary>Called by the view after the user picked a picture file.</summary>
    public void SetLogo(byte[]? bytes)
    {
        FormLogo = bytes;
        _logoChanged = true;
    }

    [RelayCommand]
    private void RemoveLogo() => SetLogo(null);

    public event EventHandler? CompaniesChanged;

    public ObservableCollection<CompanyDto> Companies { get; }
        = new();

    /*
     * Ergani user types (stored as the code, shown as a localized description):
     *
     * 01 = External
     * 02 = Login with ERGANI credentials
     * 03 = Login with EFKA credentials (construction works)
     */
    public ObservableCollection<LocalizedCodeOption> AvailableUsertypes { get; }
        = new();

    private void InitUsertypes()
    {
        AvailableUsertypes.Add(new LocalizedCodeOption("01", L.UsertypeExternal, Loc));
        AvailableUsertypes.Add(new LocalizedCodeOption("02", L.UsertypeErgani, Loc));
        AvailableUsertypes.Add(new LocalizedCodeOption("03", L.UsertypeEfka, Loc));

        // Re-render the descriptions when the language changes
        Loc.LanguageChanged += (_, _) =>
        {
            foreach (var o in AvailableUsertypes) o.Refresh();
        };
    }

    [ObservableProperty]
    private CompanyDto? _selectedCompany;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _statusMessage =
        string.Empty;

    // ---------------------------------------------------------------------
    // Ergani status
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private string _erganiTestResult =
        string.Empty;

    [ObservableProperty]
    private bool _erganiTestSuccess;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isImporting;

    // ---------------------------------------------------------------------
    // Company form
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private int _formId;

    [ObservableProperty]
    private string _formName =
        string.Empty;

    [ObservableProperty]
    private string _formTaxId =
        string.Empty;

    [ObservableProperty]
    private string _formErganiUsername =
        string.Empty;

    [ObservableProperty]
    private string _formErganiPassword =
        string.Empty;

    [ObservableProperty]
    private string _formErganiUsertype =
        "01";

    [ObservableProperty]
    private string _formErganiBaseUrl =
        ErganiEndpoints.TrialBaseUrl;

    [ObservableProperty]
    private bool _formIsActive =
        true;

    // ---------------------------------------------------------------------
    // Time rules
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private int _formEarlyClockInBlockMinutes =
        15;

    [ObservableProperty]
    private int _formEarlyDepartureAlertMinutes =
        10;

    [ObservableProperty]
    private bool _formBlockClockInWithoutSchedule =
        true;

    [ObservableProperty]
    private bool _formBlockClockInOnRestOrAbsentDay =
        true;

    // Which events send an alert mail (see AlertEvent) and the clock-in tolerance.
    [ObservableProperty] private bool _formAlertEvEarlyDeparture = true;
    [ObservableProperty] private bool _formAlertEvErganiDown;
    [ObservableProperty] private bool _formAlertEvInternetDown;
    [ObservableProperty] private bool _formAlertEvEarlyClockIn;
    [ObservableProperty] private bool _formAlertEvLateClockIn;
    [ObservableProperty] private bool _formAlertEvNoSchedule;
    [ObservableProperty] private bool _formAlertEvScanRejected;
    [ObservableProperty] private bool _formAlertEvAutomaticClockOut;
    [ObservableProperty] private int _formClockInAlertMinutes = 10;

    private int ComposeAlertEvents() =>
        (FormAlertEvEarlyDeparture ? (int)AlertEvent.EarlyDeparture : 0) |
        (FormAlertEvErganiDown     ? (int)AlertEvent.ErganiDown : 0) |
        (FormAlertEvInternetDown   ? (int)AlertEvent.InternetDown : 0) |
        (FormAlertEvEarlyClockIn   ? (int)AlertEvent.EarlyClockIn : 0) |
        (FormAlertEvLateClockIn    ? (int)AlertEvent.LateClockIn : 0) |
        (FormAlertEvNoSchedule     ? (int)AlertEvent.NoScheduleClockIn : 0) |
        (FormAlertEvScanRejected   ? (int)AlertEvent.ScanRejected : 0) |
        (FormAlertEvAutomaticClockOut ? (int)AlertEvent.AutomaticClockOut : 0);

    private void ApplyAlertEvents(int mask)
    {
        var events = (AlertEvent)mask;
        FormAlertEvEarlyDeparture = events.HasFlag(AlertEvent.EarlyDeparture);
        FormAlertEvErganiDown     = events.HasFlag(AlertEvent.ErganiDown);
        FormAlertEvInternetDown   = events.HasFlag(AlertEvent.InternetDown);
        FormAlertEvEarlyClockIn   = events.HasFlag(AlertEvent.EarlyClockIn);
        FormAlertEvLateClockIn    = events.HasFlag(AlertEvent.LateClockIn);
        FormAlertEvNoSchedule     = events.HasFlag(AlertEvent.NoScheduleClockIn);
        FormAlertEvScanRejected   = events.HasFlag(AlertEvent.ScanRejected);
        FormAlertEvAutomaticClockOut = events.HasFlag(AlertEvent.AutomaticClockOut);
    }

    // ---------------------------------------------------------------------
    // Email / SMTP
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private bool _formAlertEmailEnabled;

    [ObservableProperty] private bool _isTestingEmail;
    [ObservableProperty] private bool _emailTestSuccess;
    [ObservableProperty] private string _emailTestResult = string.Empty;

    [ObservableProperty]
    private string _formAlertEmailRecipients =
        string.Empty;

    [ObservableProperty]
    private bool _formAutoRetryFailedSubmissions =
        true;

    [ObservableProperty]
    private string _formSmtpHost =
        string.Empty;

    [ObservableProperty]
    private int? _formSmtpPort =
        465;

    [ObservableProperty]
    private string _formSmtpUser =
        string.Empty;

    [ObservableProperty]
    private string _formSmtpPassword =
        string.Empty;

    [ObservableProperty]
    private bool _formSmtpUseTls =
        true;

    // ---------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------

    public CompaniesViewModel(
        ICompanyService companyService,
        IErganiDataImportService erganiImport,
        ICacheSyncService cacheSyncService,
        IEmailAlertService emailAlertService)
    {
        _companyService =
            companyService;

        _erganiImport =
            erganiImport;

        _cacheSyncService =
            cacheSyncService;

        _emailAlertService = emailAlertService;

        InitUsertypes();
    }

    // ---------------------------------------------------------------------
    // Initialize
    // ---------------------------------------------------------------------

    public void Initialize(
        UserSession session)
    {
        _session =
            session;

        _ = LoadAsync();
    }

    // ---------------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var list =
                await _companyService
                    .GetAllAsync()
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    Companies.Clear();

                    foreach (var company in list)
                    {
                        Companies.Add(company);
                    }
                });

            CompaniesChanged?.Invoke(
                this,
                EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    StatusMessage =
                        string.Format(Loc["Vm.❌ Failed to load companies: {0}"], ex.Message);
                });
        }
    }

    // ---------------------------------------------------------------------
    // Create
    // ---------------------------------------------------------------------

    [RelayCommand]
    private void StartCreate()
    {
        SelectedCompany = null;

        FormId = 0;

        FormLogo = null;
        _logoChanged = false;

        FormName =
            string.Empty;

        FormTaxId =
            string.Empty;

        FormErganiUsername =
            string.Empty;

        FormErganiPassword =
            string.Empty;

        FormErganiUsertype =
            "01";

        FormErganiBaseUrl =
            ErganiEndpoints.TrialBaseUrl;

        FormIsActive =
            true;

        FormEarlyClockInBlockMinutes =
            15;

        FormEarlyDepartureAlertMinutes =
            10;

        ApplyAlertEvents((int)AlertEvent.Default);
        FormClockInAlertMinutes = 10;

        FormBlockClockInWithoutSchedule =
            true;

        FormBlockClockInOnRestOrAbsentDay =
            true;

        FormAlertEmailEnabled =
            false;

        FormAutoRetryFailedSubmissions =
            true;

        FormAlertEmailRecipients =
            string.Empty;

        FormSmtpHost =
            string.Empty;

        FormSmtpPort =
            465;

        FormSmtpUser =
            string.Empty;

        FormSmtpPassword =
            string.Empty;

        FormSmtpUseTls =
            true;

        EmailTestResult = string.Empty;
        EmailTestSuccess = false;

        ErganiTestResult =
            string.Empty;

        ErganiTestSuccess =
            false;

        StatusMessage =
            string.Empty;

        IsEditing =
            true;
    }

    // ---------------------------------------------------------------------
    // Edit
    // ---------------------------------------------------------------------

    [RelayCommand]
    private void StartEdit(
        CompanyDto company)
    {
        SelectedCompany =
            company;

        FormId =
            company.Id;

        FormLogo = company.Logo;
        _logoChanged = false;

        FormName =
            company.Name;

        FormTaxId =
            company.TaxId;

        FormErganiUsername =
            company.ErganiUsername;

        /*
         * Passwords are never returned by CompanyService.
         *
         * The user enters the password again when performing an Ergani
         * import/test.
         */
        FormErganiPassword =
            string.Empty;

        FormErganiUsertype =
            string.IsNullOrWhiteSpace(
                company.ErganiUsertype)
                ? "01"
                : company.ErganiUsertype;

        FormErganiBaseUrl =
            string.IsNullOrWhiteSpace(
                company.ErganiBaseUrl)
                ? ErganiEndpoints.TrialBaseUrl
                : company.ErganiBaseUrl;

        FormIsActive =
            company.IsActive;

        FormEarlyClockInBlockMinutes =
            company.EarlyClockInBlockMinutes;

        FormEarlyDepartureAlertMinutes =
            company.EarlyDepartureAlertMinutes;

        ApplyAlertEvents(company.AlertEvents);
        FormClockInAlertMinutes = company.ClockInAlertMinutes;

        FormBlockClockInWithoutSchedule =
            company.BlockClockInWithoutSchedule;

        FormBlockClockInOnRestOrAbsentDay =
            company.BlockClockInOnRestOrAbsentDay;

        FormAlertEmailEnabled =
            company.AlertEmailEnabled;

        FormAutoRetryFailedSubmissions =
            company.AutoRetryFailedSubmissions;

        FormAlertEmailRecipients =
            company.AlertEmailRecipients
            ?? string.Empty;

        FormSmtpHost =
            company.SmtpHost
            ?? string.Empty;

        FormSmtpPort =
            company.SmtpPort
            ?? 465;

        FormSmtpUser =
            company.SmtpUser
            ?? string.Empty;

        FormSmtpPassword =
            string.Empty;

        FormSmtpUseTls =
            company.SmtpUseTls;

        EmailTestResult = string.Empty;
        EmailTestSuccess = false;

        ErganiTestResult =
            string.Empty;

        ErganiTestSuccess =
            false;

        StatusMessage =
            string.Empty;

        IsEditing =
            true;
    }

    // ---------------------------------------------------------------------
    // Cancel
    // ---------------------------------------------------------------------

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing =
            false;

        StatusMessage =
            string.Empty;
    }

    // ---------------------------------------------------------------------
    // Test email / SMTP settings (does not save the company)
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task TestEmailSettingsAsync()
    {
        EmailTestResult = string.Empty;
        EmailTestSuccess = false;

        if (string.IsNullOrWhiteSpace(FormSmtpHost))
        {
            EmailTestResult = Loc["Vm.Enter the SMTP host."];
            return;
        }

        if (string.IsNullOrWhiteSpace(FormAlertEmailRecipients))
        {
            EmailTestResult = Loc["Vm.Enter at least one recipient email address."];
            return;
        }

        if (!string.IsNullOrWhiteSpace(FormSmtpUser) && string.IsNullOrEmpty(FormSmtpPassword))
        {
            EmailTestResult = Loc["Vm.Enter the SMTP password to test authenticated SMTP settings."];
            return;
        }

        IsTestingEmail = true;
        EmailTestResult = Loc["Vm.⏳ Sending test email..."];

        try
        {
            var result = await _emailAlertService.TestSmtpAsync(new EmailSmtpTestRequest(
                FormSmtpHost.Trim(),
                FormSmtpPort ?? 465,
                string.IsNullOrWhiteSpace(FormSmtpUser) ? null : FormSmtpUser.Trim(),
                string.IsNullOrEmpty(FormSmtpPassword) ? null : FormSmtpPassword,
                FormAlertEmailRecipients.Trim(),
                FormSmtpUseTls));

            EmailTestSuccess = result.Success;
            EmailTestResult = result.Success
                ? Loc["Vm.✅ Test email sent. Check the recipient inbox."]
                : $"❌ {result.ErrorMessage ?? Loc["Vm.Email test failed."]}";
        }
        catch (Exception ex)
        {
            EmailTestSuccess = false;
            EmailTestResult = $"❌ {ex.Message}";
        }
        finally
        {
            IsTestingEmail = false;
        }
    }

    // ---------------------------------------------------------------------
    // Manual Save
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(
                FormName))
        {
            StatusMessage =
                Loc["Vm.Name is required."];

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormTaxId))
        {
            StatusMessage =
                Loc["Vm.Tax ID is required."];

            return;
        }

        var dto =
            new CompanyDto
            {
                Id =
                    FormId,

                Name =
                    FormName.Trim(),

                TaxId =
                    FormTaxId.Trim(),

                ErganiUsername =
                    FormErganiUsername.Trim(),

                ErganiPasswordPlainText =
                    string.IsNullOrWhiteSpace(
                        FormErganiPassword)
                        ? null
                        : FormErganiPassword,

                ErganiUsertype =
                    string.IsNullOrWhiteSpace(
                        FormErganiUsertype)
                        ? "01"
                        : FormErganiUsertype,

                ErganiBaseUrl =
                    string.IsNullOrWhiteSpace(
                        FormErganiBaseUrl)
                        ? ErganiEndpoints.TrialBaseUrl
                        : FormErganiBaseUrl.Trim(),

                IsActive =
                    FormIsActive,

                EarlyClockInBlockMinutes =
                    FormEarlyClockInBlockMinutes,

                EarlyDepartureAlertMinutes =
                    FormEarlyDepartureAlertMinutes,

                AlertEvents = ComposeAlertEvents(),
                ClockInAlertMinutes = FormClockInAlertMinutes,

                BlockClockInWithoutSchedule =
                    FormBlockClockInWithoutSchedule,

                BlockClockInOnRestOrAbsentDay =
                    FormBlockClockInOnRestOrAbsentDay,

                OfficialHolidayDatesJson = SelectedCompany?.OfficialHolidayDatesJson ?? "[]",

                AlertEmailEnabled =
                    FormAlertEmailEnabled,

                AutoRetryFailedSubmissions =
                    FormAutoRetryFailedSubmissions,

                AlertEmailRecipients =
                    string.IsNullOrWhiteSpace(
                        FormAlertEmailRecipients)
                        ? null
                        : FormAlertEmailRecipients.Trim(),

                SmtpHost =
                    string.IsNullOrWhiteSpace(
                        FormSmtpHost)
                        ? null
                        : FormSmtpHost.Trim(),

                SmtpPort =
                    FormSmtpPort,

                SmtpUser =
                    string.IsNullOrWhiteSpace(
                        FormSmtpUser)
                        ? null
                        : FormSmtpUser.Trim(),

                SmtpPasswordPlainText =
                    string.IsNullOrWhiteSpace(
                        FormSmtpPassword)
                        ? null
                        : FormSmtpPassword,

                SmtpUseTls =
                    FormSmtpUseTls,

                Logo = FormLogo,
                UpdateLogo = _logoChanged
            };

        try
        {
            if (FormId == 0)
            {
                if (string.IsNullOrWhiteSpace(
                        FormErganiPassword))
                {
                    StatusMessage =
                        Loc["Vm.Ergani password is required when creating a new company."];

                    return;
                }

                var newId =
                    await _companyService
                        .CreateAsync(dto);

                FormId =
                    newId;

                StatusMessage =
                    Loc["Vm.✅ Company saved."];
            }
            else
            {
                await _companyService
                    .UpdateAsync(dto);

                StatusMessage =
                    Loc["Vm.✅ Company updated."];
            }

            IsEditing =
                false;

            // The login / terminal windows show the picture of the company the user works in.
            if (_logoChanged && _session?.CompanyId == FormId)
                Logo.SetLogo(FormLogo);
            _logoChanged = false;

            // Company time rules are cached for offline terminal scans. Refresh
            // the cache immediately after saving so changing the checkbox takes
            // effect without requiring an application restart or manual sync.
            try
            {
                if (FormId > 0)
                {
                    var syncResult = await _cacheSyncService.RefreshCacheFromMainDatabaseAsync(FormId);
                    if (!syncResult.Success)
                    {
                        StatusMessage = Loc["Vm.Company saved, but terminal settings could not be refreshed. Sync the terminal before scanning. "] + syncResult.ErrorMessage;
                    }
                }
            }
            catch (Exception syncEx)
            {
                // Do not undo a successfully saved company because cache refresh
                // failed; surface a clear warning so the operator knows to sync.
                StatusMessage = Loc["Vm.Company saved, but terminal settings could not be refreshed. Sync the terminal before scanning. "] + syncEx.Message;
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }

    // ---------------------------------------------------------------------
    // Toggle active
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task ToggleActiveAsync(
        CompanyDto company)
    {
        try
        {
            await _companyService
                .SetActiveAsync(
                    company.Id,
                    !company.IsActive);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }

    // ---------------------------------------------------------------------
    // Delete
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task DeleteAsync(
        CompanyDto item)
    {
        if (!await ConfirmDeleteAsync(item))
            return;

        try
        {
            await _companyService
                .DeleteAsync(item.Id);

            await LoadCommand
                .ExecuteAsync(null);

            StatusMessage =
                Loc["Vm.Company deleted."];
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }

    private static Task<bool> ConfirmDeleteAsync(
        object item)
        => Task.FromResult(true);

    // ---------------------------------------------------------------------
    // Test Ergani
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task TestErganiCredentialsAsync()
    {
        if (string.IsNullOrWhiteSpace(
                FormErganiUsername))
        {
            ErganiTestResult =
                Loc["Vm.❌ Enter the Ergani username."];

            ErganiTestSuccess =
                false;

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormErganiPassword))
        {
            ErganiTestResult =
                Loc["Vm.❌ Enter the Ergani password."];

            ErganiTestSuccess =
                false;

            return;
        }

        IsTesting =
            true;

        ErganiTestResult =
            Loc["Vm.⏳ Connecting to Ergani..."];

        ErganiTestSuccess =
            false;

        try
        {
            var credentials =
                BuildCredentials();

            var test =
                await _erganiImport
                    .TestCredentialsAsync(
                        credentials)
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    ErganiTestSuccess =
                        test.Valid;

                    if (test.Valid)
                    {
                        ErganiTestResult =
                            string.IsNullOrWhiteSpace(
                                test.CompanyName)
                                ? Loc["Vm.✅ Ergani connection successful."]
                                : string.Format(Loc["Vm.✅ Connected — {0}"], test.CompanyName);

                        if (!string.IsNullOrWhiteSpace(
                                test.CompanyName))
                        {
                            FormName =
                                test.CompanyName;
                        }
                    }
                    else
                    {
                        ErganiTestResult =
                            $"❌ {test.Error ?? Loc["Vm.Authentication failed."]}";
                    }
                });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    ErganiTestResult =
                        $"❌ {ex.Message}";

                    ErganiTestSuccess =
                        false;
                });
        }
        finally
        {
            IsTesting =
                false;
        }
    }

    // ---------------------------------------------------------------------
    // COMPLETE IMPORT FROM ERGANI
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task ImportFromErganiAsync()
    {
        if (string.IsNullOrWhiteSpace(
                FormErganiUsername))
        {
            StatusMessage =
                Loc["Vm.❌ Enter the Ergani username."];

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormErganiPassword))
        {
            StatusMessage =
                Loc["Vm.❌ Enter the Ergani password."];

            return;
        }

        IsImporting =
            true;

        StatusMessage =
            Loc["Vm.⏳ Connecting to Ergani and retrieving company information..."];

        try
        {
            var credentials =
                BuildCredentials();

            /*
             * IMPORTANT:
             *
             * We do NOT check FormId here.
             *
             * The import service will:
             *
             * 1. Authenticate
             * 2. Call EX_BASE_01
             * 3. Obtain AFM + company name
             * 4. Create or update the company
             * 5. Import branches
             * 6. Import employees
             */
            var result =
                await _erganiImport
                    .ImportFromErganiAsync(
                        credentials)
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(async () =>
                {
                    if (!result.Success)
                    {
                        StatusMessage =
                            string.Format(Loc["Vm.❌ Import failed: {0}"], result.ErrorMessage);

                        return;
                    }

                    /*
                     * Update the form with the official Ergani information.
                     */
                    FormId =
                        result.CompanyId;

                    if (!string.IsNullOrWhiteSpace(
                            result.CompanyName))
                    {
                        FormName =
                            result.CompanyName;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            result.CompanyTaxId))
                    {
                        FormTaxId =
                            result.CompanyTaxId;
                    }

                    var companyMessage =
                        result.CompanyCreated
                            ? Loc["Vm.Company created"]
                            : Loc["Vm.Company updated"];

                    StatusMessage =
                        string.Format(Loc["Vm.✅ {0}. AFM: {1}. Branches: {2}. Employees: {3}."],
                            companyMessage, result.CompanyTaxId, result.TotalBranches, result.TotalEmployees);

                    if (result.Warnings.Count > 0)
                    {
                        StatusMessage +=
                            Environment.NewLine +
                            Environment.NewLine +
                            string.Join(
                                Environment.NewLine,
                                result.Warnings);
                    }

                    /*
                     * Refresh company list so the newly created company
                     * immediately appears in the UI.
                     */
                    await LoadAsync();
                });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    StatusMessage =
                        $"❌ {ex.Message}";
                });
        }
        finally
        {
            IsImporting =
                false;
        }
    }

    // ---------------------------------------------------------------------
    // Credentials
    // ---------------------------------------------------------------------

    private ErganiCredentials BuildCredentials()
    {
        return new ErganiCredentials
        {
            Username =
                FormErganiUsername.Trim(),

            Password =
                FormErganiPassword,

            Usertype =
                string.IsNullOrWhiteSpace(
                    FormErganiUsertype)
                    ? "01"
                    : FormErganiUsertype,

            BaseUrl =
                string.IsNullOrWhiteSpace(
                    FormErganiBaseUrl)
                    ? ErganiEndpoints.TrialBaseUrl
                    : FormErganiBaseUrl.Trim()
        };
    }


}
