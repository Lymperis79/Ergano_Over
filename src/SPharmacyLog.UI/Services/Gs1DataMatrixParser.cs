using System.Globalization;
using System.Text;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Parses GS1 element strings from pharmaceutical DataMatrix scanners.
/// Variable-length AIs (10 and 21) must be terminated by ASCII 29 when
/// another AI follows. The original scanner payload is stored separately and
/// is never modified by this parser.
/// </summary>
public static class Gs1DataMatrixParser
{
    public const char GroupSeparator = '\u001d';

    public sealed record ParsedPack(
        string Gtin14,
        string SerialNumber,
        DateTime ExpiryDate,
        string BatchLot);

    public static bool TryParse(string rawPayload, out ParsedPack? result, out string error)
    {
        result = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            error = "Scan a GS1 DataMatrix barcode.";
            return false;
        }

        // ]d2 is the common GS1 DataMatrix symbology identifier. It is
        // deliberately removed only from the parsing copy, never the stored raw value.
        var payload = rawPayload.StartsWith("]d2", StringComparison.Ordinal)
            ? rawPayload[3..]
            : rawPayload;

        // Also accept human-readable separator spellings for test/paste workflows.
        payload = payload.Replace("\\u001d", GroupSeparator.ToString(), StringComparison.OrdinalIgnoreCase)
                         .Replace("<GS>", GroupSeparator.ToString(), StringComparison.OrdinalIgnoreCase);

        string? gtin = null;
        string? serial = null;
        string? lot = null;
        DateTime? expiry = null;
        var position = 0;

        while (position < payload.Length)
        {
            if (payload[position] == GroupSeparator)
            {
                position++;
                continue;
            }

            if (position + 2 > payload.Length)
            {
                error = "Incomplete GS1 Application Identifier at the end of the scan.";
                return false;
            }

            var ai = payload.Substring(position, 2);
            position += 2;

            switch (ai)
            {
                case "01":
                    if (!TryReadFixed(payload, ref position, 14, out var gtinValue) ||
                        gtinValue.Any(ch => !char.IsDigit(ch)))
                    {
                        error = "AI (01) must contain exactly 14 numeric GTIN digits.";
                        return false;
                    }
                    gtin = gtinValue;
                    break;

                case "17":
                    if (!TryReadFixed(payload, ref position, 6, out var dateValue) ||
                        !TryParseGs1Expiry(dateValue, out var parsedExpiry))
                    {
                        error = "AI (17) must contain a valid YYMMDD expiry date.";
                        return false;
                    }
                    expiry = parsedExpiry;
                    break;

                case "10":
                case "21":
                    var variableValue = ReadVariable(payload, ref position);
                    if (variableValue.Length == 0)
                    {
                        error = $"AI ({ai}) is present but its value is empty.";
                        return false;
                    }
                    if (variableValue.Length > 20)
                    {
                        error = $"AI ({ai}) exceeds the GS1 maximum of 20 characters.";
                        return false;
                    }
                    if (ai == "10") lot = variableValue;
                    else serial = variableValue;
                    break;

                default:
                    error = $"Unsupported GS1 Application Identifier ({ai}). This screen currently expects AIs 01, 10, 17 and 21.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(gtin) || string.IsNullOrWhiteSpace(serial) ||
            string.IsNullOrWhiteSpace(lot) || !expiry.HasValue)
        {
            error = "The scan must contain all four fields: GTIN (01), serial (21), expiry (17) and batch/lot (10).";
            return false;
        }

        result = new ParsedPack(gtin, serial, expiry.Value, lot);
        return true;
    }

    private static bool TryReadFixed(string payload, ref int position, int length, out string value)
    {
        value = string.Empty;
        if (position + length > payload.Length) return false;
        value = payload.Substring(position, length);
        position += length;
        return value.All(ch => ch != GroupSeparator);
    }

    private static string ReadVariable(string payload, ref int position)
    {
        var separator = payload.IndexOf(GroupSeparator, position);
        if (separator < 0)
        {
            var remaining = payload[position..];
            position = payload.Length;
            return remaining;
        }

        var value = payload[position..separator];
        position = separator + 1;
        return value;
    }

    private static bool TryParseGs1Expiry(string value, out DateTime expiry)
    {
        expiry = default;
        if (value.Length != 6 || value.Any(ch => !char.IsDigit(ch))) return false;

        if (!DateTime.TryParseExact(value, "yyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;

        // GS1 AI (17) permits DD=00 to mean the final day of that month.
        if (value.EndsWith("00", StringComparison.Ordinal))
        {
            var year = parsed.Year;
            var month = parsed.Month;
            expiry = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            return true;
        }

        expiry = parsed.Date;
        return true;
    }
}
