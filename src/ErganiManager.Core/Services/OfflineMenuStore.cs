using System.Text.Json;
using ErganiManager.LocalCache;

namespace ErganiManager.Core.Services;

/// <summary>
/// Remembers which menus each user may see, next to the offline user cache, so the same menu
/// restrictions also apply when the user logs in while the main database is not available.
/// A separate small file is used on purpose: changing the local cache database itself would force
/// it to be rebuilt, and it may hold scans that are not synced yet.
/// </summary>
internal static class OfflineMenuStore
{
    private static readonly object Gate = new();

    private static string FilePath =>
        Path.Combine(Path.GetDirectoryName(AppPaths.GetLocalCacheDbPath())!, "user-menus.json");

    public static void Set(int userId, string? allowedMenus)
    {
        try
        {
            lock (Gate)
            {
                var map = Load();
                map[userId.ToString()] = allowedMenus;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(map));
            }
        }
        catch
        {
            // Best effort: a failure here must never block a login or a cache refresh.
        }
    }

    /// <summary>Null = no restriction (also when nothing was stored for the user).</summary>
    public static string? Get(int userId)
    {
        try
        {
            lock (Gate)
                return Load().TryGetValue(userId.ToString(), out var menus) ? menus : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string?> Load()
    {
        if (!File.Exists(FilePath)) return new Dictionary<string, string?>();
        return JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(FilePath))
               ?? new Dictionary<string, string?>();
    }
}
