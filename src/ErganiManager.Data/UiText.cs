namespace ErganiManager.Data;

/// <summary>
/// Lets the lower layers (Data, Core, ErganiApi) produce messages that the user will read
/// without depending on the UI project. Write the English text as the key:
/// <c>UiText.T("Username is required.")</c> or <c>UiText.F("User {0} not found.", id)</c>.
/// The UI replaces <see cref="Translator"/> at start-up with the active-language lookup
/// (keys are stored as <c>Core.&lt;English text&gt;</c> in the language files).
/// Until then — and for any text with no translation — the English text is returned.
/// </summary>
public static class UiText
{
    public static Func<string, string> Translator { get; set; } = static s => s;

    public static string T(string english) => Translator(english);

    public static string F(string english, params object?[] args)
    {
        var template = Translator(english);
        try { return string.Format(template, args); }
        catch (FormatException) { return string.Format(english, args); }
    }
}
