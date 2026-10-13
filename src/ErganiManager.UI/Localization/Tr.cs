using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.UI.Localization;

/// <summary>
/// Static access to the localization service for code that has no ViewModel
/// (value converters, window code-behind, the splash screen).
/// <c>Tr.Loc["Vm.Some text"]</c> works exactly like <c>Loc[...]</c> in a ViewModel.
/// </summary>
public static class Tr
{
    public static ILocalizationService Loc => Program.Services.GetRequiredService<ILocalizationService>();
}
