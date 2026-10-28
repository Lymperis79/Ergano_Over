using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using Microsoft.Extensions.Logging;

namespace ErganiManager.UI.Services;

/// <summary>
/// In-process automatic backup scheduler. It runs only while Ergani Manager is running.
/// The initial implementation intentionally supports SQLite; server-side backup paths
/// need provider-specific handling before SQL Server/MariaDB can safely use retention.
/// </summary>
public sealed class AutomaticDatabaseBackupService
{
    private sealed class Settings
    {
        public bool Enabled { get; set; }
        public int IntervalHours { get; set; } = 24;
        public int RetentionCount { get; set; } = 14;
        public string BackupFolder { get; set; } = DefaultFolder;
        public DateTime? LastSuccessLocal { get; set; }
        public string LastStatus { get; set; } = "Automatic backup is disabled.";
    }

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErganiManager");
    private static readonly string SettingsPath = Path.Combine(Root, "automatic-backup.json");
    private static string DefaultFolder => Path.Combine(Root, "Backups");

    private readonly IDatabaseMaintenanceService _maintenance;
    private readonly IConnectionStateService _connection;
    private readonly ILogger<AutomaticDatabaseBackupService> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly object _sync = new();
    private Settings _settings;
    private CancellationTokenSource? _cts;

    public event EventHandler? StatusChanged;

    public AutomaticDatabaseBackupService(IDatabaseMaintenanceService maintenance,
        IConnectionStateService connection, ILogger<AutomaticDatabaseBackupService> logger)
    {
        _maintenance = maintenance;
        _connection = connection;
        _logger = logger;
        _settings = Load();
    }

    public bool IsEnabled { get { lock (_sync) return _settings.Enabled; } }
    public int IntervalHours { get { lock (_sync) return _settings.IntervalHours; } }
    public int RetentionCount { get { lock (_sync) return _settings.RetentionCount; } }
    public string BackupFolder { get { lock (_sync) return _settings.BackupFolder; } }
    public DateTime? LastSuccessLocal { get { lock (_sync) return _settings.LastSuccessLocal; } }
    public string Status { get { lock (_sync) return _settings.LastStatus; } }
    public DateTime? NextRunLocal
    {
        get
        {
            lock (_sync)
            {
                if (!_settings.Enabled) return null;
                return (_settings.LastSuccessLocal ?? DateTime.Now.AddHours(-_settings.IntervalHours)).AddHours(_settings.IntervalHours);
            }
        }
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void SaveSettings(bool enabled, int intervalHours, int retentionCount, string? backupFolder)
    {
        if (intervalHours is < 1 or > 168)
            throw new ArgumentOutOfRangeException(nameof(intervalHours), "Backup interval must be between 1 and 168 hours.");
        if (retentionCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(retentionCount), "Retention must be between 1 and 100 backups.");

        var folder = string.IsNullOrWhiteSpace(backupFolder) ? DefaultFolder : Path.GetFullPath(backupFolder.Trim());
        lock (_sync)
        {
            _settings.Enabled = enabled;
            _settings.IntervalHours = intervalHours;
            _settings.RetentionCount = retentionCount;
            _settings.BackupFolder = folder;
            _settings.LastStatus = enabled ? "Automatic backup is enabled; waiting for the next check." : "Automatic backup is disabled.";
            PersistLocked();
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
        if (enabled) _ = RunIfDueAsync(CancellationToken.None);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            await RunIfDueAsync(token);
            while (await timer.WaitForNextTickAsync(token))
                await RunIfDueAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Automatic database backup loop stopped unexpectedly."); SetStatus("Automatic backup scheduler error: " + ex.Message); }
    }

    private async Task RunIfDueAsync(CancellationToken token)
    {
        if (!IsEnabled || !await _runLock.WaitAsync(0, token)) return;
        try
        {
            Settings snapshot;
            lock (_sync) snapshot = new Settings
            {
                Enabled = _settings.Enabled,
                IntervalHours = _settings.IntervalHours,
                RetentionCount = _settings.RetentionCount,
                BackupFolder = _settings.BackupFolder,
                LastSuccessLocal = _settings.LastSuccessLocal,
                LastStatus = _settings.LastStatus
            };
            if (!snapshot.Enabled) return;
            if (snapshot.LastSuccessLocal.HasValue &&
                DateTime.Now < snapshot.LastSuccessLocal.Value.AddHours(snapshot.IntervalHours)) return;

            var config = _connection.LoadConfig();
            if (config == null) { SetStatus("Automatic backup is waiting for database configuration."); return; }
            if (config.DatabaseProvider != DatabaseProvider.Sqlite)
            {
                SetStatus("Automatic backup currently supports SQLite only. SQL Server/MariaDB need provider-specific server-side backup handling.");
                return;
            }

            Directory.CreateDirectory(snapshot.BackupFolder);
            var path = Path.Combine(snapshot.BackupFolder, $"erganimanager-auto-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            var message = await _maintenance.BackupAsync(path);
            lock (_sync)
            {
                _settings.LastSuccessLocal = DateTime.Now;
                _settings.LastStatus = "Last automatic backup verified: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ". " + message;
                PersistLocked();
            }
            _logger.LogInformation("Automatic database backup verified at {BackupPath}", path);
            try
            {
                var old = Directory.GetFiles(snapshot.BackupFolder, "erganimanager-auto-*.db")
                    .Select(p => new FileInfo(p)).OrderByDescending(f => f.CreationTimeUtc).Skip(snapshot.RetentionCount).ToList();
                foreach (var file in old)
                    try { file.Delete(); } catch (Exception ex) { _logger.LogWarning(ex, "Could not prune old backup {Path}", file.FullName); }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not apply automatic backup retention policy."); }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic database backup failed.");
            SetStatus("Automatic backup failed: " + ex.Message);
        }
        finally { _runLock.Release(); }
    }

    private void SetStatus(string status)
    {
        lock (_sync) { _settings.LastStatus = status; PersistLocked(); }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read automatic backup settings; defaults will be used."); }
        return new Settings();
    }

    private void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(Root);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception ex) { _logger.LogError(ex, "Could not save automatic backup settings."); }
    }
}
