using System.Globalization;
using ErganiManager.Licensing;

namespace ErganiManager.LicenseTool;

/// <summary>
/// The supplier's tool for Ergani Manager licenses. Keep this program and license-private.key to yourself:
/// they are what makes a license real. The customer's program only holds the public key.
///
///   genkey                         create your key pair (once)
///   issue                          create a license for a PC (asks for what is missing)
///   machine                        show the PC code of THIS computer (for testing)
/// </summary>
internal static class Program
{
    private const string DefaultPrivateKeyFile = "license-private.key";

    private static int Main(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault()?.ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());

            switch (command)
            {
                case "genkey":  return GenerateKey(options);
                case "issue":   return Issue(options);
                case "machine": Console.WriteLine(MachineCode.Get()); return 0;
                default:        PrintHelp(); return command == null || command is "help" or "--help" or "-h" ? 0 : 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Ergani Manager license tool");
        Console.WriteLine();
        Console.WriteLine("  genkey [--out license-private.key]");
        Console.WriteLine("      Creates your key pair. Keep the private key file secret. Paste the printed public key");
        Console.WriteLine("      into LicenseKeys.cs of the application and rebuild it.");
        Console.WriteLine();
        Console.WriteLine("  issue [--key license-private.key] [--type single|floating|multi|unlimited] [--pc CODE] [--seats N]");
        Console.WriteLine("        [--licensee NAME] [--expires yyyy-MM-dd|none] [--out file.lic]");
        Console.WriteLine("        [--modules *|" + string.Join(",", LicenseModules.Names) + "]");
        Console.WriteLine("      Creates a license key. Missing values are asked for. Types:");
        Console.WriteLine("        single     locked to ONE PC (needs --pc with the PC code the customer sent)");
        Console.WriteLine("        floating   single user: any PC, but only ONE PC at a time");
        Console.WriteLine("        multi      multi-user: any PC, up to --seats N PCs at the same time");
        Console.WriteLine("        unlimited  any PC, no limit (site license)");
        Console.WriteLine("      floating / multi / unlimited need all PCs to use the same shared database (SQL Server / MariaDB).");
        Console.WriteLine();
        Console.WriteLine("  machine");
        Console.WriteLine("      Shows the PC code of this computer.");
    }

    // -- genkey -----------------------------------------------------------------------------------

    private static int GenerateKey(Dictionary<string, string> options)
    {
        var file = options.GetValueOrDefault("out") ?? DefaultPrivateKeyFile;
        if (File.Exists(file))
        {
            Console.Error.WriteLine($"'{file}' already exists. Refusing to overwrite your private key. " +
                                    "Delete or rename it first if you really want a new key pair (old licenses will stop working).");
            return 1;
        }

        var (privateKey, publicKey) = LicenseCodec.CreateKeyPair();
        File.WriteAllText(file, privateKey);

        Console.WriteLine($"Private key written to: {Path.GetFullPath(file)}");
        Console.WriteLine("KEEP IT SECRET and make a backup. Anyone who has it can create licenses; if you lose it you");
        Console.WriteLine("cannot create licenses that the already shipped program accepts.");
        Console.WriteLine();
        Console.WriteLine("Public key - paste it into src/ErganiManager.Licensing/LicenseKeys.cs and rebuild the application:");
        Console.WriteLine();
        Console.WriteLine($"    public const string PublicKeyBase64 = \"{publicKey}\";");
        return 0;
    }

    // -- issue -------------------------------------------------------------------------------------

    private static int Issue(Dictionary<string, string> options)
    {
        var keyFile = options.GetValueOrDefault("key") ?? DefaultPrivateKeyFile;
        if (!File.Exists(keyFile))
        {
            Console.Error.WriteLine($"Private key file '{keyFile}' not found. Run 'genkey' first or pass --key <file>.");
            return 1;
        }

        var licensee = options.GetValueOrDefault("licensee") ?? Ask("Customer / licensee name");
        var type = (options.GetValueOrDefault("type") ?? Ask("License type: single / floating / multi / unlimited")).Trim().ToLowerInvariant();
        if (type is not ("single" or "floating" or "multi" or "unlimited"))
            throw new ArgumentException("The type must be single, floating, multi or unlimited.");

        var pc = "*";
        var seats = 0;
        switch (type)
        {
            case "single":
                pc = options.GetValueOrDefault("pc") ?? Ask("PC code from the customer");
                break;
            case "floating":
                seats = 1;
                break;
            case "multi":
                var seatText = options.GetValueOrDefault("seats") ?? Ask("How many PCs at the same time (2 or more)");
                if (!int.TryParse(seatText, out seats) || seats < 2) throw new ArgumentException("A multi-user license needs 2 or more PCs.");
                break;
        }

        var expiresText = options.GetValueOrDefault("expires") ?? Ask("Valid until (yyyy-MM-dd), empty = permanent");
        var modulesText = options.GetValueOrDefault("modules")
                          ?? Ask($"Modules ({string.Join(", ", LicenseModules.Names)}), empty or * = all");

        if (string.IsNullOrWhiteSpace(licensee)) throw new ArgumentException("The licensee name is required.");
        if (string.IsNullOrWhiteSpace(pc)) throw new ArgumentException("The PC code is required.");

        string? expires = null;
        if (!string.IsNullOrWhiteSpace(expiresText) && !expiresText.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateOnly.TryParseExact(expiresText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ArgumentException("The date must look like 2027-12-31.");
            expires = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var payload = new LicensePayload
        {
            Licensee = licensee.Trim(),
            MachineCode = type != "single" ? "*" : MachineCode.Normalize(pc).Length == 20
                ? FormatCode(MachineCode.Normalize(pc))
                : throw new ArgumentException("A PC code has 20 letters/digits, like 1A2B-3C4D-5E6F-7A8B-9C0D."),
            Seats = seats,
            Issued = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Expires = expires,
            Modules = LicenseModules.Parse(modulesText)
        };

        var key = LicenseCodec.Issue(payload, File.ReadAllText(keyFile));

        var safeName = new string($"{payload.Licensee}_{payload.MachineCode}".Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var outFile = options.GetValueOrDefault("out") ?? $"{safeName}.lic";
        File.WriteAllText(outFile, key);

        Console.WriteLine();
        Console.WriteLine($"Licensee : {payload.Licensee}");
        Console.WriteLine($"Type     : {type}" + (type == "multi" ? $" ({seats} PCs at a time)" : string.Empty));
        Console.WriteLine($"PC code  : {(payload.MachineCode == "*" ? "any PC" : payload.MachineCode)}");
        Console.WriteLine($"Valid    : {(expires == null ? "permanent" : "until " + expires)}");
        Console.WriteLine($"Modules  : {string.Join(", ", payload.Modules)}");
        Console.WriteLine();
        Console.WriteLine("License key (send this to the customer - they paste it into the activation window):");
        Console.WriteLine();
        Console.WriteLine(key);
        Console.WriteLine();
        Console.WriteLine($"Also saved in the file: {Path.GetFullPath(outFile)}");
        return 0;
    }

    // -- helpers -----------------------------------------------------------------------------------

    private static string FormatCode(string normalized) =>
        string.Join("-", Enumerable.Range(0, 5).Select(i => normalized.Substring(i * 4, 4)));

    private static string Ask(string question)
    {
        Console.Write(question + ": ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var name = args[i][2..];
            result[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : string.Empty;
        }
        return result;
    }
}
