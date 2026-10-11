using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.LocalCache;

namespace ErganiManager.Core.Services;

public class PortalSettingsService : IPortalSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private PortalSettings? _current;

    private static string FilePath => Path.Combine(AppPaths.GetAppDataFolder(), "portal.json");

    public PortalSettings Current
    {
        get
        {
            lock (_gate)
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<PortalSettings>(File.ReadAllText(FilePath));
                        if (loaded != null)
                        {
                            // Migrate older flat portal.json files into the explicit Schedule profile.
                            loaded.Schedule ??= new ErganiPortalProfile();
                            loaded.WorkCards ??= new ErganiPortalProfile
                            {
                                PortalBaseUrl = loaded.Schedule.PortalBaseUrl,
                                TargetPageUrl = "https://trialv2eservices.yeka.gr/WTO/Workcard/DailyWorkTimesSearch.aspx",
                                FromDateFieldId = "ctl00_ctl00_ContentHolder_ContentHolder_DailyWorkTimesSearchControl_DateFromEdit",
                                ToDateFieldId = "igtxtctl00_ctl00_ContentHolder_ContentHolder_DailyWorkTimesSearchControl_DateToEdit",
                                DateFormat = loaded.Schedule.DateFormat,
                                AutoSearch = true,
                                AutoDownload = false,
                                SearchButtonId = "ctl00_ctl00_ContentHolder_ContentHolder_DailyWorkTimesSearchControl_SearchControlSearchButton"
                            };
                            return _current = loaded;
                        }
                    }
                }
                catch { /* unreadable file: use defaults */ }
                return _current = new PortalSettings();
            }
        }
    }

    public void Save(PortalSettings settings)
    {
        lock (_gate)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
            _current = settings;
        }
    }
}
