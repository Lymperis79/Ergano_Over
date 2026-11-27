using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;

namespace SPharmacyLog.UI.Services;

/// <summary>
/// Imports the open Greek medicine registry into the application's existing
/// medicine catalogue. Existing names and barcode assignments are never replaced.
/// </summary>
public static class MedicineCatalogImportService
{
    public const string RegistryUrl = "https://raw.githubusercontent.com/lighteternal/drugbase-gr/main/registry.jsonl";
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SPharmacyLog", "1.0"));
        return client;
    }

    public sealed record ImportResult(int RowsRead, int MedicinesAdded, int BarcodesAdded, int RowsWithoutBarcode);

    public static async Task<ImportResult> UpdateFromPublicRegistryAsync(
        Action<string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(RegistryUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);

        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var medicines = await db.Medicines.Include(x => x.Barcodes).ToListAsync(cancellationToken);
        var byName = medicines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => NormalizeName(x.Name))
            .ToDictionary(x => x.Key, x => x.First());
        var barcodeOwners = new Dictionary<string, MedicineCatalogItem>(StringComparer.Ordinal);
        foreach (var medicine in medicines)
        {
            foreach (var barcode in medicine.Barcodes)
                barcodeOwners.TryAdd(barcode.BarcodeValue, medicine);
            if (!string.IsNullOrWhiteSpace(medicine.Barcode))
                barcodeOwners.TryAdd(medicine.Barcode.Trim(), medicine);
        }

        var rowsRead = 0;
        var medicinesAdded = 0;
        var barcodesAdded = 0;
        var rowsWithoutBarcode = 0;
        var pendingRows = 0;
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rowsRead++;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var brand = ReadString(root, "brand_name");
                if (string.IsNullOrWhiteSpace(brand)) continue;

                var strength = ReadString(root, "strength");
                var form = ReadString(root, "form");
                var displayName = BuildDisplayName(brand, strength, form);
                var rawBarcodes = ReadBarcodes(root);
                if (rawBarcodes.Count == 0)
                {
                    rowsWithoutBarcode++;
                    continue;
                }

                foreach (var rawBarcode in rawBarcodes)
                {
                    var aliases = ExpandBarcode(rawBarcode);
                    if (aliases.Count == 0)
                    {
                        rowsWithoutBarcode++;
                        continue;
                    }

                    var medicine = aliases
                        .Select(alias => barcodeOwners.TryGetValue(alias.Value, out var owner) ? owner : null)
                        .FirstOrDefault(owner => owner is not null);

                    if (medicine is null)
                    {
                        var key = NormalizeName(displayName);
                        if (!byName.TryGetValue(key, out medicine))
                        {
                            medicine = new MedicineCatalogItem
                            {
                                Name = displayName,
                                Barcode = rawBarcode,
                                IsActive = true
                            };
                            db.Medicines.Add(medicine);
                            medicines.Add(medicine);
                            byName[key] = medicine;
                            medicinesAdded++;
                        }
                        else if (string.IsNullOrWhiteSpace(medicine.Barcode))
                        {
                            medicine.Barcode = rawBarcode;
                        }
                    }

                    foreach (var alias in aliases)
                    {
                        if (barcodeOwners.ContainsKey(alias.Value)) continue;
                        var mapping = new MedicineBarcode
                        {
                            Medicine = medicine,
                            BarcodeValue = alias.Value,
                            BarcodeType = alias.Type
                        };
                        db.MedicineBarcodes.Add(mapping);
                        medicine.Barcodes.Add(mapping);
                        barcodeOwners[alias.Value] = medicine;
                        barcodesAdded++;
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore a malformed registry line without aborting the full import.
            }

            if (++pendingRows >= 400)
            {
                await db.SaveChangesAsync(cancellationToken);
                pendingRows = 0;
                reportProgress?.Invoke($"Read {rowsRead:N0} registry rows; added {medicinesAdded:N0} medicines and {barcodesAdded:N0} barcode mappings…");
            }
        }

        if (pendingRows > 0)
            await db.SaveChangesAsync(cancellationToken);

        reportProgress?.Invoke($"Completed: {rowsRead:N0} rows read; {medicinesAdded:N0} medicines and {barcodesAdded:N0} barcode mappings added.");
        return new ImportResult(rowsRead, medicinesAdded, barcodesAdded, rowsWithoutBarcode);
    }

    private static string NormalizeName(string value) =>
        value.Trim().ToUpperInvariant();

    private static string BuildDisplayName(string brand, string? strength, string? form)
    {
        var parts = new List<string> { brand.Trim() };
        foreach (var part in new[] { strength, form })
        {
            var value = part?.Trim();
            if (!string.IsNullOrWhiteSpace(value) &&
                !brand.Contains(value, StringComparison.OrdinalIgnoreCase) &&
                !parts.Contains(value, StringComparer.OrdinalIgnoreCase))
                parts.Add(value);
        }
        return string.Join(" — ", parts);
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static List<string> ReadBarcodes(JsonElement root)
    {
        if (!TryGetProperty(root, "barcode", out var value) || value.ValueKind == JsonValueKind.Null)
            return new List<string>();
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        var single = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return string.IsNullOrWhiteSpace(single) ? new List<string>() : new List<string> { single.Trim() };
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static List<(string Value, string Type)> ExpandBarcode(string raw)
    {
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        var result = new List<(string Value, string Type)>();
        if (digits.Length is < 8 or > 14) return result;

        void Add(string value, string type)
        {
            if (!result.Any(item => item.Value == value))
                result.Add((value, type));
        }

        Add(digits, digits.Length switch
        {
            14 => "GTIN14",
            13 => digits.StartsWith("280", StringComparison.Ordinal) ? "EOF_EAN13" : "EAN13",
            _ => "OTHER"
        });

        // A GS1 DataMatrix carries a 14-digit GTIN. Expand standard EAN/UPC
        // identifiers to their GTIN-14 representation so the scan can match.
        if (digits.Length == 13)
            Add("0" + digits, "GTIN14");
        else if (digits.Length == 12)
            Add("00" + digits, "GTIN14");
        else if (digits.Length == 14 && digits.StartsWith("0", StringComparison.Ordinal))
        {
            var ean = digits[1..];
            Add(ean, ean.StartsWith("280", StringComparison.Ordinal) ? "EOF_EAN13" : "EAN13");
        }

        return result;
    }
}
