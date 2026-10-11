using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace ErganiManager.Data;

/// <summary>
/// Brings an EXISTING database up to the current EF model WITHOUT touching its data.
///
/// Strictly additive:
///   * a table that is in the model but not in the database  -> CREATE TABLE (+ its indexes)
///   * a column that is in the model but not in the database -> ALTER TABLE ... ADD COLUMN
/// It never drops, renames or changes the type of anything, so existing rows are never lost.
/// New NOT NULL columns get a safe default so they can be added to tables that already hold rows.
///
/// Not handled automatically (add a manual entry to DbSchemaUpdater.Patches, or use EF migrations):
/// renamed or removed columns, changed column types, and indexes/foreign keys on columns that
/// were added to an existing table.
///
/// For SQLite a backup copy of the database file is made before anything is changed.
/// </summary>
public static class ModelSchemaSync
{
    /// <summary>Prefix of the entries of the result that describe a change that could NOT be made.</summary>
    public const string FailedPrefix = "FAILED: ";

    /// <summary>
    /// Returns one line per change made. A line starting with <see cref="FailedPrefix"/> is a change
    /// that failed; a failure never stops the other changes from being tried.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnsureAsync(AppDbContext db)
    {
        var changes = new List<string>();
        var helper = db.GetService<ISqlGenerationHelper>();
        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlServer = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        // ── 1. Find what is missing ───────────────────────────────────────────
        var entities = db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() != null)
            .ToList();

        var tables = entities
            .Select(e => (Table: e.GetTableName()!, Schema: e.GetSchema()))
            .Distinct()
            .ToList();

        var missingTables = new List<(string Table, string? Schema)>();
        foreach (var (table, schema) in tables)
        {
            if (!await ProbeAsync(db, $"SELECT 1 FROM {helper.DelimitIdentifier(table, schema)} WHERE 1 = 0"))
                missingTables.Add((table, schema));
        }

        // Every table "missing" means we are not talking to the database properly (connection lost,
        // wrong database...): do not try to create anything.
        if (tables.Count > 0 && missingTables.Count == tables.Count)
        {
            changes.Add(FailedPrefix + "no table of the model could be read — the database is not reachable or is empty.");
            return changes;
        }

        var missingTableNames = new HashSet<string>(missingTables.Select(t => t.Table), StringComparer.OrdinalIgnoreCase);

        var addColumns = new List<(string Description, string Sql)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var table = entity.GetTableName()!;
            if (missingTableNames.Contains(table)) continue; // the whole table is created below

            var schema = entity.GetSchema();
            var tableId = StoreObjectIdentifier.Table(table, schema);
            var delimitedTable = helper.DelimitIdentifier(table, schema);

            foreach (var property in entity.GetProperties())
            {
                if (property.IsPrimaryKey()) continue;
                if (property.GetComputedColumnSql() != null) continue;

                var column = property.GetColumnName(tableId);
                if (column == null) continue;
                if (!seen.Add($"{table}.{column}")) continue; // shared by several entity types (TPH)

                var delimitedColumn = helper.DelimitIdentifier(column);
                if (await ProbeAsync(db, $"SELECT {delimitedColumn} FROM {delimitedTable} WHERE 1 = 0"))
                    continue; // already there

                var mapping = property.GetRelationalTypeMapping();
                var sql = BuildAddColumn(isSqlServer, delimitedTable, delimitedColumn, property, mapping, out var note);
                addColumns.Add(($"{table}.{column} ({mapping.StoreType}){note}", sql));
            }
        }

        if (missingTables.Count == 0 && addColumns.Count == 0)
            return changes;

        // ── 2. Safety net: back up the SQLite file before changing anything ─────
        if (isSqlite)
        {
            var backup = BackupSqliteFile(db);
            if (backup != null) changes.Add($"Backup written: {backup}");
        }

        // ── 3. Create missing tables (each one on its own, then verified) ──────
        if (missingTables.Count > 0)
        {
            IReadOnlyList<MigrationOperation> all;
            IMigrationsSqlGenerator generator;
            IModel designModel;
            try
            {
                var differ = db.GetService<IMigrationsModelDiffer>();
                generator = db.GetService<IMigrationsSqlGenerator>();
                designModel = db.GetService<IDesignTimeModel>().Model;
                all = differ.GetDifferences(null, designModel.GetRelationalModel());
            }
            catch (Exception ex)
            {
                foreach (var (table, _) in missingTables)
                    changes.Add($"{FailedPrefix}create table {table}: cannot build the table definition ({ex.Message})");
                missingTables.Clear();
                all = Array.Empty<MigrationOperation>();
                generator = null!;
                designModel = null!;
            }

            foreach (var (table, schema) in missingTables)
            {
                try
                {
                    var wanted = all
                        .Where(o => o is EnsureSchemaOperation
                                 || (o is CreateTableOperation ct && string.Equals(ct.Name, table, StringComparison.OrdinalIgnoreCase))
                                 || (o is CreateIndexOperation ci && string.Equals(ci.Table, table, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (!wanted.Any(o => o is CreateTableOperation))
                        throw new InvalidOperationException("EF produced no CREATE TABLE statement for it.");

                    foreach (var command in generator.Generate(wanted, designModel))
                        await db.Database.ExecuteSqlRawAsync(command.CommandText, Array.Empty<object>());

                    // Do not trust "no exception": make sure the table is really there now.
                    if (!await ProbeAsync(db, $"SELECT 1 FROM {helper.DelimitIdentifier(table, schema)} WHERE 1 = 0"))
                        throw new InvalidOperationException("the table is still missing after it was created.");

                    changes.Add($"Created table {table}");
                }
                catch (Exception ex)
                {
                    changes.Add($"{FailedPrefix}create table {table}: {ex.Message}");
                }
            }
        }

        // ── 4. Add missing columns (each one on its own) ──────────────────────
        foreach (var (description, sql) in addColumns)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, Array.Empty<object>());
                changes.Add($"Added column {description}");
            }
            catch (Exception ex)
            {
                changes.Add($"{FailedPrefix}add column {description}: {ex.Message}");
            }
        }

        return changes;
    }

    private static string BuildAddColumn(
        bool isSqlServer, string table, string column,
        IProperty property, RelationalTypeMapping mapping, out string note)
    {
        note = string.Empty;
        var nullable = property.IsNullable;
        string? defaultClause = null;

        if (!nullable)
        {
            var literal = DefaultLiteral(property, mapping);
            if (literal != null)
                defaultClause = $" DEFAULT {literal}";
            else
            {
                // No sensible default exists: add it as NULLable rather than fail on existing rows.
                nullable = true;
                note = " [added as NULL — no default available]";
            }
        }

        // SQL Server uses "ADD <col>", SQLite and MariaDB use "ADD COLUMN <col>".
        var add = isSqlServer ? "ADD" : "ADD COLUMN";
        return $"ALTER TABLE {table} {add} {column} {mapping.StoreType}{(nullable ? " NULL" : " NOT NULL")}{defaultClause}";
    }

    private static string? DefaultLiteral(IProperty property, RelationalTypeMapping mapping)
    {
        var sqlDefault = property.GetDefaultValueSql();
        if (!string.IsNullOrWhiteSpace(sqlDefault))
            return $"({sqlDefault})";

        object? value = property.GetDefaultValue();
        if (value == null || value == DBNull.Value)
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            value = type == typeof(string) ? string.Empty
                  : type == typeof(byte[]) ? Array.Empty<byte>()
                  : type.IsValueType ? Activator.CreateInstance(type)
                  : null;
        }

        if (value != null && mapping.Converter != null)
            value = mapping.Converter.ConvertToProvider(value);

        // Avoid year-0001 values that some servers reject.
        value = value switch
        {
            DateTime d when d == default => DateTime.UtcNow,
            DateTimeOffset o when o == default => DateTimeOffset.UtcNow,
            DateOnly d when d == default => DateOnly.FromDateTime(DateTime.UtcNow),
            _ => value
        };

        return value == null ? null : mapping.GenerateSqlLiteral(value);
    }

    private static async Task<bool> ProbeAsync(AppDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, Array.Empty<object>());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? BackupSqliteFile(AppDbContext db)
    {
        try
        {
            var connectionString = db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString)) return null;

            var path = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            var backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, overwrite: true);

            // Keep only the 3 newest backups.
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var name = Path.GetFileName(path);
            foreach (var old in Directory.GetFiles(dir, name + ".*.bak")
                         .OrderByDescending(f => f, StringComparer.Ordinal).Skip(3))
            {
                try { File.Delete(old); } catch { /* best effort */ }
            }

            return backup;
        }
        catch
        {
            return null; // a failed backup must not block the upgrade
        }
    }
}
