using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

public partial class BranchesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IBranchService _branchService;
    private UserSession? _session;

    public ObservableCollection<BranchDto> Branches { get; } = new();

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private bool _hasActiveCompany;

    [ObservableProperty] private int _formId;
    [ObservableProperty] private int _formBranchNumber = 1;
    [ObservableProperty] private string _formAddress = string.Empty;
    [ObservableProperty] private string _formSepeServiceCode = string.Empty;
    [ObservableProperty] private string _formOaedServiceCode = string.Empty;
    [ObservableProperty] private string _formActivityCode = string.Empty;
    [ObservableProperty] private string _formKallikratisMunicipalCode = string.Empty;
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private bool _formIsActive = true;

    public BranchesViewModel(IBranchService branchService)
    {
        _branchService = branchService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue
            ? string.Empty
            : "Select a company first (Super Admin: pick a company from the Companies tab).";

        if (HasActiveCompany)
            _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        var list = await _branchService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Branches.Clear();
            foreach (var b in list)
                Branches.Add(b);
        });
    }

    [RelayCommand]
    private void StartCreate()
    {
        FormId = 0;
        FormBranchNumber = Branches.Count + 1;
        FormAddress = string.Empty;
        FormSepeServiceCode = string.Empty;
        FormOaedServiceCode = string.Empty;
        FormActivityCode = string.Empty;
        FormKallikratisMunicipalCode = string.Empty;
        FormName = string.Empty;
        FormIsActive = true;
        StatusMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit(BranchDto branch)
    {
        FormId = branch.Id;
        FormBranchNumber = branch.BranchNumber;
        FormAddress = branch.Address;
        FormSepeServiceCode = branch.SepeServiceCode;
        FormOaedServiceCode = branch.OaedServiceCode ?? string.Empty;
        FormActivityCode = branch.ActivityCode;
        FormKallikratisMunicipalCode = branch.KallikratisMunicipalCode;
        FormName = branch.Name ?? string.Empty;
        FormIsActive = branch.IsActive;
        StatusMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        if (string.IsNullOrWhiteSpace(FormAddress) || string.IsNullOrWhiteSpace(FormSepeServiceCode))
        {
            StatusMessage = "Address and SEPE service code are required.";
            return;
        }

        var dto = new BranchDto
        {
            Id = FormId,
            CompanyId = companyId,
            BranchNumber = FormBranchNumber,
            Address = FormAddress,
            SepeServiceCode = FormSepeServiceCode,
            OaedServiceCode = FormOaedServiceCode,
            ActivityCode = FormActivityCode,
            KallikratisMunicipalCode = FormKallikratisMunicipalCode,
            Name = FormName,
            IsActive = FormIsActive
        };

        try
        {
            if (FormId == 0)
                await _branchService.CreateAsync(dto);
            else
                await _branchService.UpdateAsync(dto);

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(BranchDto branch)
    {
        await _branchService.SetActiveAsync(branch.Id, !branch.IsActive);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(BranchDto item)
    {
        if (!await ConfirmDeleteAsync(item)) return;
        try
        {
            await _branchService.DeleteAsync(item.Id);
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