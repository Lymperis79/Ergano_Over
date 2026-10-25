namespace ErganiManager.Licensing;

/// <summary>The license of this running program, set once at startup (and when a new key is entered).</summary>
public static class LicenseState
{
    /// <summary>False while no public key is built in: then there is no license check at all.</summary>
    public static bool Enforced => LicenseKeys.IsConfigured;

    public static LicensePayload? Current { get; set; }

    public static bool IsModuleLicensed(string? module)
    {
        if (!Enforced || module == null) return true;            // no check, or the base program
        var license = Current;
        if (license == null) return false;
        return license.Modules.Contains(LicenseModules.All) ||
               license.Modules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the menu / section with this key is part of the license.</summary>
    public static bool IsMenuLicensed(string menuKey) => IsModuleLicensed(LicenseModules.ModuleOfMenu(menuKey));
}
