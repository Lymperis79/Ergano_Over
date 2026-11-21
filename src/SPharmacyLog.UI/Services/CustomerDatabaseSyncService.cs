using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;

namespace SPharmacyLog.UI.Services;

public sealed class CustomerDatabaseSyncSettings
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public bool IntegratedSecurity { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string FilterColumn { get; set; } = "KPP_KATHGOR";
    public string FilterValue { get; set; } = "ΠΕΛ";
    public Dictionary<string, string> FieldMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CustomerNumber"] = "PEL_PRO_CODE",
        ["FullName"] = "EPONYM",
        ["Address"] = "ODOS",
        ["Postcode"] = "TAX_KOD",
        ["City"] = "POLH",
        ["Telephone"] = "THLEF",
        ["Amka"] = "PEL_PRO_AMKA",
        ["Insurance"] = "PEL_PRO_CODE_AT"
    };
}

public sealed record CustomerImportResult(int Added, int Updated, int InactiveSkipped, int InvalidSkipped);

/// <summary>
/// One-way SQL Server customer import. The source database is read-only from this service.
/// </summary>
public static class CustomerDatabaseSyncService
{
    private const string Entropy = "SPharmacyLog.CustomerDatabaseSync.v1";
    private static string SettingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static string SettingsPath => Path.Combine(SettingsFolder, "customer-database-sync.dat");

    public static CustomerDatabaseSyncSettings LoadSettings()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(SettingsPath))
            return new CustomerDatabaseSyncSettings();
        try
        {
            var encrypted = File.ReadAllBytes(SettingsPath);
            var clear = ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes(Entropy), DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<CustomerDatabaseSyncSettings>(clear) ?? new(); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch { return new CustomerDatabaseSyncSettings(); }
    }

    public static void SaveSettings(CustomerDatabaseSyncSettings settings)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Protected connection settings require Windows.");
        Directory.CreateDirectory(SettingsFolder);
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings);
        try
        {
            var encrypted = ProtectedData.Protect(clear, Encoding.UTF8.GetBytes(Entropy), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(SettingsPath, encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static string BuildConnectionString(CustomerDatabaseSyncSettings settings)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server.Trim(),
            InitialCatalog = settings.Database.Trim(),
            IntegratedSecurity = settings.IntegratedSecurity,
            Encrypt = true,
            TrustServerCertificate = false,
            ConnectTimeout = 10,
            ApplicationName = "S Pharmacy Log customer import"
        };
        if (!settings.IntegratedSecurity)
        {
            builder.UserID = settings.Username;
            builder.Password = settings.Password;
        }
        return builder.ConnectionString;
    }

    public static async Task TestConnectionAsync(CustomerDatabaseSyncSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildConnectionString(settings));
        await connection.OpenAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<string>> GetTablesAsync(CustomerDatabaseSyncSettings settings, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME;
            """;
        await using var connection = new SqlConnection(BuildConnectionString(settings));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return tables;
    }

    public static async Task<IReadOnlyList<string>> GetColumnsAsync(CustomerDatabaseSyncSettings settings, string table, CancellationToken cancellationToken = default)
    {
        var (schema, name) = ParseTable(table);
        const string sql = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        await using var connection = new SqlConnection(BuildConnectionString(settings));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", name);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(0));
        return columns;
    }

    public static async Task<int> PreviewCountAsync(CustomerDatabaseSyncSettings settings, CancellationToken cancellationToken = default)
    {
        var (schema, table) = ParseTable(settings.Table);
        var filterColumn = ResolveColumn(settings.FilterColumn, await GetColumnsAsync(settings, settings.Table, cancellationToken));
        var sql = $"SELECT COUNT_BIG(1) FROM {Quote(schema)}.{Quote(table)}" +
                  (string.IsNullOrWhiteSpace(settings.FilterValue) || string.IsNullOrWhiteSpace(filterColumn)
                      ? ";" : $" WHERE {Quote(filterColumn)} = @filter;");
        await using var connection = new SqlConnection(BuildConnectionString(settings));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        if (sql.Contains("@filter", StringComparison.Ordinal))
            command.Parameters.AddWithValue("@filter", settings.FilterValue);
        var count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count);
    }

    public static async Task<CustomerImportResult> ImportAsync(
        CustomerDatabaseSyncSettings settings,
        PharmacyDbContext destination,
        CancellationToken cancellationToken = default)
    {
        var columns = await GetColumnsAsync(settings, settings.Table, cancellationToken);
        var mappings = settings.FieldMappings
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => ResolveColumn(pair.Value, columns), StringComparer.OrdinalIgnoreCase);
        if (!mappings.TryGetValue(nameof(Customer.CustomerNumber), out var customerNumberColumn) ||
            string.IsNullOrWhiteSpace(customerNumberColumn))
            throw new InvalidOperationException("Map a source column to Customer number before importing.");

        var selectedColumns = mappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var (schema, table) = ParseTable(settings.Table);
        var filterColumn = ResolveColumn(settings.FilterColumn, columns);
        var sql = $"SELECT {string.Join(", ", selectedColumns.Select(Quote))} FROM {Quote(schema)}.{Quote(table)}";
        if (!string.IsNullOrWhiteSpace(settings.FilterValue) && !string.IsNullOrWhiteSpace(filterColumn))
            sql += $" WHERE {Quote(filterColumn)} = @filter";

        await using var source = new SqlConnection(BuildConnectionString(settings));
        await source.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, source);
        if (!string.IsNullOrWhiteSpace(settings.FilterValue) && !string.IsNullOrWhiteSpace(filterColumn))
            command.Parameters.AddWithValue("@filter", settings.FilterValue);

        var added = 0;
        var updated = 0;
        var inactiveSkipped = 0;
        var invalidSkipped = 0;
        await using var transaction = await destination.Database.BeginTransactionAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        var ordinalByColumn = selectedColumns.ToDictionary(column => column, reader.GetOrdinal, StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in mappings)
            {
                var ordinal = ordinalByColumn[mapping.Value];
                values[mapping.Key] = await reader.IsDBNullAsync(ordinal, cancellationToken)
                    ? null
                    : Convert.ToString(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)?.Trim();
            }

            var customerNumber = values.GetValueOrDefault(nameof(Customer.CustomerNumber));
            if (string.IsNullOrWhiteSpace(customerNumber))
            {
                invalidSkipped++;
                continue;
            }

            var existing = await destination.Customers
                .FirstOrDefaultAsync(customer => customer.CustomerNumber == customerNumber, cancellationToken);

            // Critical safeguard: an inactive customer is an explicit local decision.
            // Do not update, reactivate, or otherwise modify any part of that record.
            if (existing is not null && !existing.IsActive)
            {
                inactiveSkipped++;
                continue;
            }

            if (existing is null)
            {
                existing = new Customer
                {
                    CustomerNumber = customerNumber,
                    FullName = values.GetValueOrDefault(nameof(Customer.FullName)) ?? customerNumber,
                    Phone = string.Empty,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                ApplyMappedValues(existing, values, isNew: true);
                destination.Customers.Add(existing);
                added++;
            }
            else
            {
                ApplyMappedValues(existing, values, isNew: false);
                updated++;
            }
        }

        await reader.CloseAsync();
        await destination.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CustomerImportResult(added, updated, inactiveSkipped, invalidSkipped);
    }

    private static void ApplyMappedValues(Customer customer, IReadOnlyDictionary<string, string?> values, bool isNew)
    {
        string? Value(string key) => values.TryGetValue(key, out var value) ? value : null;
        void Set(string key, Action<string> setter)
        {
            var value = Value(key);
            // Blank source values never erase data already maintained in S Pharmacy Log.
            if (!string.IsNullOrWhiteSpace(value)) setter(value);
        }

        Set(nameof(Customer.CustomerNumber), value => customer.CustomerNumber = value);
        Set(nameof(Customer.FullName), value => customer.FullName = value);
        Set(nameof(Customer.FirstName), value => customer.FirstName = value);
        Set(nameof(Customer.Surname), value => customer.Surname = value);
        Set(nameof(Customer.Address), value => customer.Address = value);
        Set(nameof(Customer.City), value => customer.City = value);
        Set(nameof(Customer.Postcode), value => customer.Postcode = value);
        Set(nameof(Customer.Telephone), value => customer.Telephone = value);
        Set(nameof(Customer.Mobile), value => customer.Mobile = value);
        Set(nameof(Customer.Email), value => customer.Email = value);
        Set(nameof(Customer.Amka), value => customer.Amka = value);
        Set(nameof(Customer.Insurance), value => customer.Insurance = value);
        Set(nameof(Customer.Phone), value => customer.Phone = value);
        if (isNew && string.IsNullOrWhiteSpace(customer.Phone))
            customer.Phone = customer.Telephone ?? customer.Mobile ?? string.Empty;
    }

    private static string ResolveColumn(string requested, IReadOnlyList<string> available)
    {
        if (string.IsNullOrWhiteSpace(requested)) return string.Empty;
        var actual = available.FirstOrDefault(column => string.Equals(column, requested, StringComparison.OrdinalIgnoreCase));
        if (actual is null)
            throw new InvalidOperationException($"Source column '{requested}' does not exist in the selected table.");
        return actual;
    }

    private static (string Schema, string Table) ParseTable(string value)
    {
        var parts = (value ?? string.Empty).Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Select a table using its schema and table name.");
        return (parts[0], parts[1]);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
