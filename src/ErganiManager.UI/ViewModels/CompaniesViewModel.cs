using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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

    private UserSession? _session;

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

    // ---------------------------------------------------------------------
    // Email / SMTP
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private bool _formAlertEmailEnabled;

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
        587;

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
        IErganiDataImportService erganiImport)
    {
        _companyService =
            companyService;

        _erganiImport =
            erganiImport;

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
                        $"❌ Failed to load companies: {ex.Message}";
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

        FormBlockClockInWithoutSchedule =
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
            587;

        FormSmtpUser =
            string.Empty;

        FormSmtpPassword =
            string.Empty;

        FormSmtpUseTls =
            true;

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

        FormBlockClockInWithoutSchedule =
            company.BlockClockInWithoutSchedule;

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
            ?? 587;

        FormSmtpUser =
            company.SmtpUser
            ?? string.Empty;

        FormSmtpPassword =
            string.Empty;

        FormSmtpUseTls =
            company.SmtpUseTls;

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
    // Manual Save
    // ---------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(
                FormName))
        {
            StatusMessage =
                "Name is required.";

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormTaxId))
        {
            StatusMessage =
                "Tax ID is required.";

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

                BlockClockInWithoutSchedule =
                    FormBlockClockInWithoutSchedule,

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
                    FormSmtpUseTls
            };

        try
        {
            if (FormId == 0)
            {
                if (string.IsNullOrWhiteSpace(
                        FormErganiPassword))
                {
                    StatusMessage =
                        "Ergani password is required when creating a new company.";

                    return;
                }

                var newId =
                    await _companyService
                        .CreateAsync(dto);

                FormId =
                    newId;

                StatusMessage =
                    "✅ Company saved.";
            }
            else
            {
                await _companyService
                    .UpdateAsync(dto);

                StatusMessage =
                    "✅ Company updated.";
            }

            IsEditing =
                false;

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
                "Company deleted.";
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
                "❌ Enter the Ergani username.";

            ErganiTestSuccess =
                false;

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormErganiPassword))
        {
            ErganiTestResult =
                "❌ Enter the Ergani password.";

            ErganiTestSuccess =
                false;

            return;
        }

        IsTesting =
            true;

        ErganiTestResult =
            "⏳ Connecting to Ergani...";

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
                                ? "✅ Ergani connection successful."
                                : $"✅ Connected — {test.CompanyName}";

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
                            $"❌ {test.Error ?? "Authentication failed."}";
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
                "❌ Enter the Ergani username.";

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormErganiPassword))
        {
            StatusMessage =
                "❌ Enter the Ergani password.";

            return;
        }

        IsImporting =
            true;

        StatusMessage =
            "⏳ Connecting to Ergani and retrieving company information...";

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
                            $"❌ Import failed: {result.ErrorMessage}";

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
                            ? "Company created"
                            : "Company updated";

                    StatusMessage =
                        $"✅ {companyMessage}. " +
                        $"AFM: {result.CompanyTaxId}. " +
                        $"Branches: {result.TotalBranches}. " +
                        $"Employees: {result.TotalEmployees}.";

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
