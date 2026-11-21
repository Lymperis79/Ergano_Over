using System.Globalization;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Parses GS1 element strings from pharmaceutical DataMatrix scanners.
/// Variable-length AIs must be terminated by ASCII 29 when another AI follows.
/// The caller retains the exact original scanner payload separately.
/// </summary>
public static class Gs1DataMatrixParser
{
    public const char GroupSeparator = '\u001d';

    public sealed record ParsedPack(
        string Gtin14,
        string SerialNumber,
        DateTime ExpiryDate,
        string BatchLot,
        string? Nhrn715,
        string? ResolvedEofBarcode);

    public static bool TryParse(string rawPayload, out ParsedPack? result, out string error)
    {
        result = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            error = "Scan a GS1 DataMatrix barcode.";
            return false;
        }

        // Remove only recognized symbology identifiers from the parsing copy.
        var payload = rawPayload.Trim();
        foreach (var prefix in new[] { "]d2", "]C1", "]e0", "]Q3" })
        {
            if (payload.StartsWith(prefix, StringComparison.Ordinal))
            {
                payload = payload[prefix.Length..];
                break;
            }
        }

        // Human-readable separator spellings are accepted for test/paste workflows.
        payload = payload.Replace("\\u001d", GroupSeparator.ToString(), StringComparison.OrdinalIgnoreCase)
                         .Replace("<GS>", GroupSeparator.ToString(), StringComparison.OrdinalIgnoreCase);

        string? gtin = null;
        string? serial = null;
        string? lot = null;
        string? nhrn715 = null;
        DateTime? expiry = null;
        var seenAis = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;

        while (position < payload.Length)
        {
            if (payload[position] == GroupSeparator)
            {
                position++;
                continue;
            }

            // AI 715 is a three-digit NHRN AI. Check it before reading two-digit AIs.
            if (payload.AsSpan(position).StartsWith("715", StringComparison.Ordinal))
            {
                const string ai = "715";
                if (!seenAis.Add(ai))
                    return Fail("AI (715) occurs more than once in the same scan.", out error);

                position += ai.Length;
                if (!TryReadVariable(payload, ref position, 20, out nhrn715, out error, ai))
                    return false;
                continue;
            }

            if (position + 2 > payload.Length)
                return Fail("Incomplete GS1 Application Identifier at the end of the scan.", out error);

            var ai2 = payload.Substring(position, 2);
            position += 2;

            if (!seenAis.Add(ai2))
                return Fail($"AI ({ai2}) occurs more than once in the same scan.", out error);

            switch (ai2)
            {
                case "01":
                    if (!TryReadFixed(payload, ref position, 14, out var gtinValue) ||
                        !IsAsciiDigits(gtinValue))
                        return Fail("AI (01) must contain exactly 14 numeric GTIN digits.", out error);
                    gtin = gtinValue;
                    break;

                case "17":
                    if (!TryReadFixed(payload, ref position, 6, out var dateValue) ||
                        !TryParseGs1Expiry(dateValue, out var parsedExpiry))
                        return Fail("AI (17) must contain a valid YYMMDD expiry date.", out error);
                    expiry = parsedExpiry;
                    break;

                case "10":
                case "21":
                    if (!TryReadVariable(payload, ref position, 20, out var variableValue, out error, ai2))
                        return false;
                    if (string.IsNullOrWhiteSpace(variableValue))
                        return Fail($"AI ({ai2}) is present but its value is empty.", out error);
                    if (ai2 == "10") lot = variableValue;
                    else serial = variableValue;
                    break;

                default:
                    return Fail($"Unsupported GS1 Application Identifier ({ai2}). The scanner currently supports AIs 01, 10, 17, 21 and optional 715.", out error);
            }
        }

        if (string.IsNullOrWhiteSpace(gtin) || string.IsNullOrWhiteSpace(serial) ||
            string.IsNullOrWhiteSpace(lot) || !expiry.HasValue)
            return Fail("The scan must contain all four fields: GTIN (01), serial (21), expiry (17) and batch/lot (10).", out error);

        result = new ParsedPack(
            gtin,
            serial,
            expiry.Value,
            lot,
            nhrn715,
            TryResolveGreekEofBarcode(gtin));

        return true;
    }

    private static bool TryReadFixed(string payload, ref int position, int length, out string value)
    {
        value = string.Empty;
        if (position + length > payload.Length) return false;

        value = payload.Substring(position, length);
        if (value.Contains(GroupSeparator))
            return false;

        position += length;
        return true;
    }

    private static bool TryReadVariable(
        string payload,
        ref int position,
        int maxLength,
        out string value,
        out string error,
        string ai)
    {
        value = string.Empty;
        error = string.Empty;

        var separator = payload.IndexOf(GroupSeparator, position);
        var end = separator < 0 ? payload.Length : separator;
        var length = end - position;

        if (length == 0)
        {
            error = $"AI ({ai}) is present but its value is empty.";
            return false;
        }

        if (length > maxLength)
        {
            error = $"AI ({ai}) exceeds the GS1 maximum of {maxLength} characters.";
            return false;
        }

        value = payload.Substring(position, length);
        position = separator < 0 ? end : separator + 1;
        return true;
    }

    private static bool TryParseGs1Expiry(string value, out DateTime expiry)
    {
        expiry = default;
        if (value.Length != 6 || !IsAsciiDigits(value)) return false;

        var shortYear = int.Parse(value[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(value.Substring(4, 2), CultureInfo.InvariantCulture);

        // GS1 century rule: 00-49 => 2000-2049; 50-99 => 1950-1999.
        var year = shortYear <= 49 ? 2000 + shortYear : 1900 + shortYear;
        if (month is < 1 or > 12) return false;

        var lastDay = DateTime.DaysInMonth(year, month);
        if (day == 0) day = lastDay;
        if (day < 1 || day > lastDay) return false;

        expiry = new DateTime(year, month, day);
        return true;
    }

    private static string? TryResolveGreekEofBarcode(string gtin14)
    {
        // GS1 Greece defines the Greek medicine identifier as EAN-13 280 + 9 EOF
        // digits + check digit. In GTIN-14 it is zero-padded as 0280...
        if (gtin14.Length != 14 || !gtin14.StartsWith("0280", StringComparison.Ordinal))
            return null;

        var ean13 = gtin14[1..];
        return IsValidEan13(ean13) ? ean13 : null;
    }

    private static bool IsValidEan13(string value)
    {
        if (value.Length != 13 || !IsAsciiDigits(value)) return false;

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = value[i] - '0';
            sum += digit * (i % 2 == 0 ? 1 : 3);
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == value[12] - '0';
    }

    private static bool IsAsciiDigits(string value) =>
        value.Length > 0 && value.All(ch => ch is >= '0' and <= '9');

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}