using System;
using System.IO;
using ErganiManager.Licensing;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Loads, validates, and stores the S Pharmacy Log licence independently
/// from the Ergani Manager licence file.
/// </summary>
public static class PharmacyLicenseManager
{
    private static string LicenseFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SPharmacyLog",
            "license.key");

    /// <summary>
    /// The machine identifier displayed on the activation screen.
    /// </summary>
    public static string PcCode => MachineCode.Get();

    /// <summary>
    /// The result of the most recent licence validation.
    /// </summary>
    public static LicenseResult Result { get; private set; } =
        new(LicenseCheck.Missing, null);

    /// <summary>
    /// Loads and validates the locally stored licence.
    /// </summary>
    public static bool Load()
    {
        // Keep the existing development/unconfigured-key behavior.
        if (!LicenseKeys.IsConfigured)
        {
            Result = new LicenseResult(LicenseCheck.Valid, null);
            return true;
        }

        string? key = null;

        try
        {
            if (File.Exists(LicenseFile))
            {
                key = File.ReadAllText(LicenseFile);
            }
        }
        catch (IOException)
        {
            // Validation below will report a missing or invalid licence.
        }
        catch (UnauthorizedAccessException)
        {
            // Validation below will report a missing or invalid licence.
        }

        Result = LicenseCodec.Evaluate(
            key,
            LicenseKeys.PublicKeyBase64,
            PcCode,
            DateOnly.FromDateTime(DateTime.Today));

        if (Result.IsValid && !IsPharmacyModuleLicensed(Result))
        {
            Result = new LicenseResult(
                LicenseCheck.ModuleNotLicensed,
                Result.Payload);
        }

        return Result.IsValid;
    }

    /// <summary>
    /// Validates a supplied key and saves it only when it is valid
    /// and includes the SPharmacyLog module.
    /// </summary>
    public static LicenseResult Activate(string? key)
    {
        var result = LicenseCodec.Evaluate(
            key,
            LicenseKeys.PublicKeyBase64,
            PcCode,
            DateOnly.FromDateTime(DateTime.Today));

        if (result.IsValid && !IsPharmacyModuleLicensed(result))
        {
            result = new LicenseResult(
                LicenseCheck.ModuleNotLicensed,
                result.Payload);
        }

        if (!result.IsValid)
        {
            return result;
        }

        var directory = Path.GetDirectoryName(LicenseFile);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return new LicenseResult(LicenseCheck.InvalidFormat, null);
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(LicenseFile, LicenseCodec.Clean(key));

        Result = result;
        return result;
    }

    private static bool IsPharmacyModuleLicensed(LicenseResult result)
    {
        var modules = result.Payload?.Modules;

        return modules is not null &&
               (modules.Contains(LicenseModules.All) ||
                modules.Contains(
                    LicenseModules.SPharmacyLog,
                    StringComparer.OrdinalIgnoreCase));
    }
}
