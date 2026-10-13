using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ErganiManager.Core.Interfaces;
using ErganiManager.LocalCache;
using Serilog;

namespace ErganiManager.UI.Services;

/// <summary>
/// Holds the company picture that the login window and the terminal window display.
/// The picture is stored with the company in the database. The last picture is also kept
/// in the application data folder, so the login window (shown before anyone is signed in)
/// and an offline start can still show it.
/// </summary>
public sealed class CompanyLogoService : ObservableObject
{
    /// <summary>Pictures wider than this are scaled down before they are stored.</summary>
    public const int MaxWidth = 480;

    /// <summary>Largest picture file the user may choose.</summary>
    public const long MaxFileBytes = 8 * 1024 * 1024;

    private static string CachePath => Path.Combine(AppPaths.GetAppDataFolder(), "company-logo.png");

    private readonly ICompanyService _companies;
    private Bitmap? _current;

    public CompanyLogoService(ICompanyService companies)
    {
        _companies = companies;
        try
        {
            if (File.Exists(CachePath))
                _current = new Bitmap(CachePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The cached company picture could not be read.");
        }
    }

    /// <summary>The picture to show, or null when the company has none.</summary>
    public Bitmap? Current
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value)) return;
            _current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLogo));
        }
    }

    public bool HasLogo => _current != null;

    /// <summary>Shows the given picture (null removes it) and remembers it for the next start.</summary>
    public void SetLogo(byte[]? bytes)
    {
        Bitmap? bitmap = null;
        try
        {
            if (bytes is { Length: > 0 })
                bitmap = new Bitmap(new MemoryStream(bytes));

            if (bytes is { Length: > 0 })
                File.WriteAllBytes(CachePath, bytes);
            else if (File.Exists(CachePath))
                File.Delete(CachePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The company picture could not be applied or cached.");
        }

        if (Dispatcher.UIThread.CheckAccess()) Current = bitmap;
        else Dispatcher.UIThread.Post(() => Current = bitmap);
    }

    /// <summary>Loads the picture of the given company from the database. If the database
    /// cannot be reached the picture that is already shown stays.</summary>
    public async Task RefreshAsync(int? companyId)
    {
        if (companyId is not int id) return;
        try
        {
            var company = await _companies.GetByIdAsync(id);
            SetLogo(company?.Logo);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The company picture could not be loaded; keeping the cached one.");
        }
    }

    /// <summary>Reads a picture file and returns bytes that are small enough to keep in the
    /// database (wide pictures are scaled down to <see cref="MaxWidth"/> pixels, saved as PNG).
    /// Throws when the file is not a picture.</summary>
    public static byte[] PrepareFromFile(Stream source)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var raw = buffer.ToArray();

        using var probe = new Bitmap(new MemoryStream(raw));
        if (probe.PixelSize.Width <= MaxWidth && raw.Length <= 600 * 1024)
            return raw;

        using var scaled = Bitmap.DecodeToWidth(new MemoryStream(raw), Math.Min(MaxWidth, probe.PixelSize.Width));
        using var output = new MemoryStream();
        scaled.Save(output);
        return output.ToArray();
    }
}
