using SPharmacyLog.Core;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;

namespace SPharmacyLog.UI.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly PharmacyAuthService auth = new();
    private string username = string.Empty;
    private string password = string.Empty;
    private string message = string.Empty;
    private bool isBusy;
    private bool isFirstRun;

    public string Username { get => username; set { username = value; OnPropertyChanged(); } }
    public string Password { get => password; set { password = value; OnPropertyChanged(); } }
    public string Message { get => message; private set { message = value; OnPropertyChanged(); } }
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); } }
    public bool IsFirstRun { get => isFirstRun; private set { isFirstRun = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubmitText)); } }
    public string SubmitText => Loc[IsFirstRun ? "Login.CreateAdmin" : "Login.SignIn"];

    public async Task InitializeAsync()
    {
        IsFirstRun = !await auth.HasUsersAsync();
        Message = IsFirstRun ? Loc["Login.FirstAdminHint"] : string.Empty;
    }

    public async Task<PharmacyUser?> SubmitAsync()
    {
        if (IsBusy) return null;
        IsBusy = true;
        try
        {
            if (IsFirstRun)
            {
                await auth.CreateFirstAdminAsync(Username.Trim(), Password);
                IsFirstRun = false;
                Password = string.Empty;
                Message = Loc["Login.AdminCreated"];
                return null;
            }

            var user = await auth.LoginAsync(Username.Trim(), Password);
            Message = user is null ? Loc["Login.InvalidCredentials"] : string.Empty;
            return user;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            return null;
        }
        finally { IsBusy = false; }
    }
}