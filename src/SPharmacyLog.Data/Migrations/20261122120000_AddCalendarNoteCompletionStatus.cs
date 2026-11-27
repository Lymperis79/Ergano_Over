using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261122120000_AddCalendarNoteCompletionStatus")]
public sealed class AddCalendarNoteCompletionStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE CalendarNotes ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE CalendarNotes ADD COLUMN CompletedBy TEXT NULL;
                ALTER TABLE CalendarNotes ADD COLUMN CompletedAt TEXT NULL;
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE [CalendarNotes] ADD
                    [IsCompleted] bit NOT NULL CONSTRAINT [DF_CalendarNotes_IsCompleted] DEFAULT (0),
                    [CompletedBy] nvarchar(255) NULL,
                    [CompletedAt] datetime2 NULL;
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE CalendarNotes
                    ADD COLUMN IsCompleted boolean NOT NULL DEFAULT FALSE,
                    ADD COLUMN CompletedBy varchar(255) NULL,
                    ADD COLUMN CompletedAt datetime(6) NULL;
                """);
            return;
        }

        throw new NotSupportedException($"The calendar note completion migration does not support provider '{ActiveProvider}'.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE CalendarNotes DROP COLUMN CompletedAt;
                ALTER TABLE CalendarNotes DROP COLUMN CompletedBy;
                ALTER TABLE CalendarNotes DROP COLUMN IsCompleted;
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE [CalendarNotes] DROP CONSTRAINT [DF_CalendarNotes_IsCompleted];
                ALTER TABLE [CalendarNotes] DROP COLUMN [IsCompleted], [CompletedBy], [CompletedAt];
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE CalendarNotes
                    DROP COLUMN CompletedAt,
                    DROP COLUMN CompletedBy,
                    DROP COLUMN IsCompleted;
                """);
            return;
        }

        throw new NotSupportedException($"The calendar note completion migration does not support provider '{ActiveProvider}'.");
    }
}
