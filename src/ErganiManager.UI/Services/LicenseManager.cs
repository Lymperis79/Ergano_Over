using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ErganiManager.Licensing;
using ErganiManager.LocalCache;

namespace ErganiManager.UI.Services;

/// <summary>
/// Reads, checks and stores the license of this PC. The license key is kept in the application data folder;
/// it only works on the PC whose code is inside it, so copying the program (or the folder) elsewhere asks again.
/// </summary>
public static class LicenseManager
{
    private static string KeyFile => Path.Combine(AppPaths.GetAppDataFolder(), "license.key");
    private static string ClockFile => Path.Combine(AppPaths.GetAppDataFolder(), "license.clock");

    /// <summary>The outcome of the last check.</summary>
    public static LicenseResult Result { get; private set; } = new(LicenseCheck.Missing, null);

    /// <summary>True when the program may run: no license check built in, or a valid license.</summary>
    public static bool IsOk => !LicenseState.Enforced || Result.IsValid;

    public static string MachineCodeText => MachineCode.Get();

    /// <summary>Called once at startup.</summary>
    public static void Load()
    {
        if (!LicenseState.Enforced)
        {
            LicenseState.Current = null;
            Result = new LicenseResult(LicenseCheck.Valid, null);
            return;
        }

        string? key = null;
        try { if (File.Exists(KeyFile)) key = File.ReadAllText(KeyFile); } catch { /* treated as missing */ }

        Result = Evaluate(key, checkClock: true);
        LicenseState.Current = Result.IsValid ? Result.Payload : null;
        if (Result.IsValid) RecordToday(reset: false);
    }

    /// <summary>Checks a key typed or pasted by the user and, when it is good, stores it.</summary>
    public static LicenseResult Activate(string? key)
    {
        var result = Evaluate(key, checkClock: false);   // a new key may repair a moved-back clock
        if (!result.IsValid) return result;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);
            File.WriteAllText(KeyFile, LicenseCodec.Clean(key));
        }
        catch (Exception)
        {
            // Not saved: the license works for this run only. The activation window shows it again next time.
        }

        Result = result;
        LicenseState.Current = result.Payload;
        RecordToday(reset: true);
        return result;
    }

    private static LicenseResult Evaluate(string? key, bool checkClock)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var result = LicenseCodec.Evaluate(key, LicenseKeys.PublicKeyBase64, MachineCode.Get(), today);

        // A license with an end date: if the system date was set back since the last run, do not trust it.
        if (checkClock && result.IsValid && result.Payload?.ExpiresDate != null && ClockWentBack(today))
            return new LicenseResult(LicenseCheck.ClockTampered, result.Payload);

        return result;
    }

    // -- A small tamper-evident note of the last day the program ran -----------------------------

    private static bool ClockWentBack(DateOnly today)
    {
        var last = ReadLastDay();
        return last != null && today < last.Value.AddDays(-1);
    }

    private static DateOnly? ReadLastDay()
    {
        try
        {
            if (!File.Exists(ClockFile)) return null;
            var parts = File.ReadAllText(ClockFile).Trim().Split('|');
            if (parts.Length != 2 || !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day))
                return null;
            return Sign(parts[0]) == parts[1] ? day : null;          // edited by hand = ignored
        }
        catch { return null; }
    }

    private static void RecordToday(bool reset)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var last = reset ? null : ReadLastDay();
            if (last != null && last.Value >= today) return;

            var text = today.ToString("yyyy-MM-dd");
            File.WriteAllText(ClockFile, text + "|" + Sign(text));
        }
        catch { /* not critical */ }
    }

    private static string Sign(string text)
    {
        var key = Encoding.UTF8.GetBytes(MachineCode.Normalize(MachineCode.Get()));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text)))[..16];
    }
}
