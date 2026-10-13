# Localization completion + company picture

## How the text is localized now
- **Views (.axaml):** `{Binding Loc[Key]}`. Inside list templates: `{Binding $parent[UserControl].((vm:XViewModel)DataContext).Loc[Key]}`.
- **ViewModels:** `Loc["Vm.English text"]`. Messages with variables use `string.Format(Loc["Vm.Applied to {0} day(s)."], count)`.
- **Code without a ViewModel** (converters, row classes, file dialogs): `Tr.Loc["Vm.English text"]`.
- **Data / Core / ErganiApi** (messages, report tables): `UiText.T("English text")` / `UiText.F("... {0}", arg)`;
  the UI connects them to the language files in `Program.cs` (keys are stored as `Core.<English text>`).
- A `Vm.` / `Core.` key with no translation shows its English text instead of `[Vm.…]`.
- Month and weekday names follow the app language (set in `LocalizationService`).

## Company picture
- New nullable column `Company.Logo` (added by the existing automatic schema sync).
- Companies form: Choose picture… / Remove picture (scaled down to 480 px wide).
- Shown in the login window (last used company, cached in the app data folder) and in the terminal window.
