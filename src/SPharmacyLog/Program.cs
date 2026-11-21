using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using System.IO;

namespace SPharmacyLog;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();

    public static string DatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SPharmacyLog", "s-pharmacy-log.db");

    public static DbContextOptions<PharmacyDbContext> DbOptions()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        return new DbContextOptionsBuilder<PharmacyDbContext>()
            .UseSqlite($"Data Source={DatabasePath};Foreign Keys=True")
            .Options;
    }
}