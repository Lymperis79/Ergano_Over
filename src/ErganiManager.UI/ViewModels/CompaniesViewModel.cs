using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.ErganiApi;

namespace ErganiManager.UI.ViewModels;

public partial class CompaniesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly ICompanyService _companyService;
    private UserSession? _session;

    /// <summary>Raised after the company list changes (create, edit, toggle
    /// active) so the AdminShell's company-switcher dropdown can refresh.</summary>
    public event EventHandler? CompaniesChanged;

    public ObservableCollection<CompanyDto> Companies { get; } = new();

    [ObservableProperty] private CompanyDto? _selectedCompany;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // Form fields (bound while creating/editing)
    [ObservableProperty] private int _formId;
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formTaxId = string.Empty;
    [ObservableProperty] private string _formErganiUsername = string.Empty;
    [ObservableProperty] private string _formErganiPassword = string.Empty;
    [ObservableProperty] private string _formErganiBaseUrl = ErganiEndpoints.TrialBaseUrl;
    [ObservableProperty] private bool _formIsActive = true;
    [ObservableProperty] private int _formEarlyClockInBlockMinutes = 15;
    [ObservableProperty] private int _formEarlyDepartureAlertMinutes = 10;
    [ObservableProperty] private bool _formBlockClockInWithoutSchedule = true;
    [ObservableProperty] private bool _formAlertEmailEnabled;
    [ObservableProperty] private string _formAlertEmailRecipients = string.Empty;
    [ObservableProperty] private bool _formAutoRetryFailedSubmissions = true;
    [ObservableProperty] private string _formSmtpHost = string.Empty;
    [ObservableProperty] private int? _formSmtpPort = 587;
    [ObservableProperty] private string _formSmtpUser = string.Empty;
    [ObservableProperty] private string _formSmtpPassword = string.Empty;
    [ObservableProperty] private bool _formSmtpUseTls = true;

    public CompaniesViewModel(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var list = await _companyService.GetAllAsync().ConfigureAwait(false);
        // Must update ObservableCollection on UI thread
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Companies.Clear();
            foreach (var c in list)
                Companies.Add(c);
        });
        CompaniesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void StartCreate()
    {
        SelectedCompany = null;
        FormId = 0;
        FormName = string.Empty;
        FormTaxId = string.Empty;
        FormErganiUsername = string.Empty;
        FormErganiPassword = string.Empty;
        FormErganiBaseUrl = ErganiEndpoints.TrialBaseUrl;
        FormIsActive = true;
        FormEarlyClockInBlockMinutes = 15;
        FormEarlyDepartureAlertMinutes = 10;
        FormBlockClockInWithoutSchedule = true;
        FormAlertEmailEnabled = false;
        FormAutoRetryFailedSubmissions = true;
        FormAlertEmailRecipients = string.Empty;
        FormSmtpHost = string.Empty;
        FormSmtpPort = 587;
        FormSmtpUser = string.Empty;
        FormSmtpPassword = string.Empty;
        FormSmtpUseTls = true;
        StatusMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit(CompanyDto company)
    {
        SelectedCompany = company;
        FormId = company.Id;
        FormName = company.Name;
        FormTaxId = company.TaxId;
        FormErganiUsername = company.ErganiUsername;
        FormErganiPassword = string.Empty; // never pre-filled — blank means "leave unchanged"
        FormErganiBaseUrl = company.ErganiBaseUrl;
        FormIsActive = company.IsActive;
        FormEarlyClockInBlockMinutes = company.EarlyClockInBlockMinutes;
        FormEarlyDepartureAlertMinutes = company.EarlyDepartureAlertMinutes;
        FormBlockClockInWithoutSchedule = company.BlockClockInWithoutSchedule;
        FormAlertEmailEnabled = company.AlertEmailEnabled;
        FormAutoRetryFailedSubmissions = company.AutoRetryFailedSubmissions;
        FormAlertEmailRecipients = company.AlertEmailRecipients ?? string.Empty;
        FormSmtpHost = company.SmtpHost ?? string.Empty;
        FormSmtpPort = company.SmtpPort ?? 587;
        FormSmtpUser = company.SmtpUser ?? string.Empty;
        FormSmtpPassword = string.Empty;
        FormSmtpUseTls = company.SmtpUseTls;
        StatusMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FormName) || string.IsNullOrWhiteSpace(FormTaxId))
        {
            StatusMessage = "Name and Tax ID are required.";
            return;
        }

        var dto = new CompanyDto
        {
            Id = FormId,
            Name = FormName,
            TaxId = FormTaxId,
            ErganiUsername = FormErganiUsername,
            ErganiPasswordPlainText = string.IsNullOrWhiteSpace(FormErganiPassword) ? null : FormErganiPassword,
            ErganiBaseUrl = FormErganiBaseUrl,
            IsActive = FormIsActive,
            EarlyClockInBlockMinutes = FormEarlyClockInBlockMinutes,
            EarlyDepartureAlertMinutes = FormEarlyDepartureAlertMinutes,
            BlockClockInWithoutSchedule = FormBlockClockInWithoutSchedule,
            AlertEmailEnabled = FormAlertEmailEnabled,
            AutoRetryFailedSubmissions = FormAutoRetryFailedSubmissions,
            AlertEmailRecipients = FormAlertEmailRecipients,
            SmtpHost = FormSmtpHost,
            SmtpPort = FormSmtpPort,
            SmtpUser = FormSmtpUser,
            SmtpPasswordPlainText = string.IsNullOrWhiteSpace(FormSmtpPassword) ? null : FormSmtpPassword,
            SmtpUseTls = FormSmtpUseTls
        };

        try
        {
            if (FormId == 0)
            {
                if (string.IsNullOrWhiteSpace(FormErganiPassword))
                {
                    StatusMessage = "Ergani password is required for a new company.";
                    return;
                }
                await _companyService.CreateAsync(dto);
            }
            else
            {
                await _companyService.UpdateAsync(dto);
            }

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(CompanyDto company)
    {
        await _companyService.SetActiveAsync(company.Id, !company.IsActive);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(CompanyDto item)
    {
        if (!await ConfirmDeleteAsync(item)) return;
        try
        {
            await _companyService.DeleteAsync(item.Id);
            await LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    // Simple confirm: returns true always for now; 
    // replace with a dialog service if desired.
    private static Task<bool> ConfirmDeleteAsync(object item) 
        => Task.FromResult(true);

}