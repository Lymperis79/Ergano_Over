namespace ErganiManager.Core.Interfaces;

/// <summary>Configuration for one Ergani web-portal workflow.</summary>
public sealed class ErganiPortalProfile
{
    public string PortalBaseUrl { get; set; } = "https://trialv2eservices.yeka.gr/";
    public string TargetPageUrl { get; set; } = string.Empty;
    public string FromDateFieldId { get; set; } = string.Empty;
    public string ToDateFieldId { get; set; } = string.Empty;
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public bool AutoSearch { get; set; } = true;
    public bool AutoDownload { get; set; } = false;
    public string SearchButtonId { get; set; } = string.Empty;
    public string DownloadFolder { get; set; } = string.Empty;
}

/// <summary>Separate portal configurations for Schedule and Work Card workflows.</summary>
public sealed class PortalSettings
{
    public ErganiPortalProfile Schedule { get; set; } = new()
    {
        TargetPageUrl = string.Empty,
        FromDateFieldId = "ctl00_ctl00_ContentHolder_ContentHolder_ErgazomenosWorkingSearchControl_DateFromEdit",
        ToDateFieldId = "ctl00_ctl00_ContentHolder_ContentHolder_ErgazomenosWorkingSearchControl_DateToEdit",
        AutoSearch = true,
        AutoDownload = true,
        SearchButtonId = "ctl00_ctl00_ContentHolder_ContentHolder_ErgazomenosWorkingSearchControl_SearchControlSearchButton"
    };

    public ErganiPortalProfile WorkCards { get; set; } = new()
    {
        TargetPageUrl = "https://trialv2eservices.yeka.gr/WTO/Workcard/DailyWorkTimesSearch.aspx",
        FromDateFieldId = "igtxtctl00_ctl00_ContentHolder_ContentHolder_DailyWorkTimesSearchControl_DateFromEdit",
        ToDateFieldId = "igtxtctl00_ctl00_ContentHolder_ContentHolder_DailyWorkTimesSearchControl_DateToEdit",
        AutoSearch = true,
        AutoDownload = false,
        SearchButtonId = "ctl00_ctl00_ContentHolder_ContentHolder_DailyWorkTimesSearchControl_SearchControlSearchButton"
    };

    /// <summary>Compatibility with older code/configuration. New code should use Schedule.</summary>
    public string PortalBaseUrl { get => Schedule.PortalBaseUrl; set => Schedule.PortalBaseUrl = value; }
    public string TargetPageUrl { get => Schedule.TargetPageUrl; set => Schedule.TargetPageUrl = value; }
    public string FromDateFieldId { get => Schedule.FromDateFieldId; set => Schedule.FromDateFieldId = value; }
    public string ToDateFieldId { get => Schedule.ToDateFieldId; set => Schedule.ToDateFieldId = value; }
    public string DateFormat { get => Schedule.DateFormat; set => Schedule.DateFormat = value; }
    public bool AutoSearchAndDownload { get => Schedule.AutoSearch && Schedule.AutoDownload; set { Schedule.AutoSearch = value; Schedule.AutoDownload = value; } }
    public string SearchButtonId { get => Schedule.SearchButtonId; set => Schedule.SearchButtonId = value; }
    public string DownloadFolder { get => Schedule.DownloadFolder; set => Schedule.DownloadFolder = value; }
}

public interface IPortalSettingsService
{
    PortalSettings Current { get; }
    void Save(PortalSettings settings);
}

public interface IErganiPortalService : IDisposable
{
    event Action<string>? Progress;

    /// <summary>Raised (on a background thread) with the full path when the schedule Excel export
    /// has been downloaded completely from the portal.</summary>
    event Action<string>? ExcelDownloaded;

    Task<string> OpenAsync(int companyId, DateOnly? from = null, DateOnly? to = null);
    Task<string> OpenWorkCardsAsync(int companyId, DateOnly date);
}
