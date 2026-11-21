using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261121210000_AddCalendarNotes")]
public sealed class AddCalendarNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE CalendarNotes (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CalendarDate TEXT NOT NULL,
                    Header TEXT NOT NULL,
                    Note TEXT NOT NULL,
                    CreatedBy TEXT NOT NULL,
                    IsShared INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_CalendarNotes_CalendarDate_IsShared_CreatedBy
                    ON CalendarNotes(CalendarDate, IsShared, CreatedBy);
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE [CalendarNotes] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CalendarNotes] PRIMARY KEY,
                    [CalendarDate] date NOT NULL,
                    [Header] nvarchar(200) NOT NULL,
                    [Note] nvarchar(max) NOT NULL,
                    [CreatedBy] nvarchar(255) NOT NULL,
                    [IsShared] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_CalendarNotes_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );
                CREATE INDEX [IX_CalendarNotes_CalendarDate_IsShared_CreatedBy]
                    ON [CalendarNotes]([CalendarDate], [IsShared], [CreatedBy]);
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE CalendarNotes (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CalendarDate date NOT NULL,
                    Header varchar(200) NOT NULL,
                    Note text NOT NULL,
                    CreatedBy varchar(255) NOT NULL,
                    IsShared boolean NOT NULL,
                    CreatedAt datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    INDEX IX_CalendarNotes_CalendarDate_IsShared_CreatedBy (CalendarDate, IsShared, CreatedBy)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """);
            return;
        }

        throw new NotSupportedException($"The calendar notes migration does not support provider '{ActiveProvider}'.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("DROP TABLE IF EXISTS CalendarNotes;");
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("DROP TABLE [CalendarNotes];");
        else if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("DROP TABLE CalendarNotes;");
        else
            throw new NotSupportedException($"The calendar notes migration does not support provider '{ActiveProvider}'.");
    }
}
