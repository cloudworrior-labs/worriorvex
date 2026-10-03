using Microsoft.Extensions.Logging;
using WorriorVex.Application.Backup;
using WorriorVex.Application.Settings;
using WorriorVex.Application.Storage;

namespace WorriorVex.Infrastructure.Backup;

public sealed class BackupScheduler(
    IBackupService backups,
    ISettingsService settings,
    IApplicationDataPathProvider paths,
    TimeProvider timeProvider,
    ILogger<BackupScheduler> logger) : IBackupScheduler
{
    public const string AutoPrefix = "auto-";
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _stop;
    private Task? _loop;

    public event Action<string?, Exception?>? Completed;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _stop = new CancellationTokenSource();
        _loop = LoopAsync(_stop.Token);
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        // A short first wait, so a backup that is overdue is taken soon after the app opens, once it has settled.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, cancellationToken);
            using var timer = new PeriodicTimer(Tick, timeProvider);
            do
            {
                await RunNowAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        var current = settings.Current;
        var interval = current.AutoBackup switch
        {
            BackupSchedule.Daily => TimeSpan.FromDays(1),
            BackupSchedule.Weekly => TimeSpan.FromDays(7),
            _ => (TimeSpan?)null,
        };
        if (interval is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (current.LastAutoBackupAt is { } last && now - last < interval)
        {
            return;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var path = Path.Combine(paths.BackupsDirectory, $"{AutoPrefix}{now.ToLocalTime():yyyyMMdd-HHmmss}.zip");
            await backups.CreateBackupAsync(path, cancellationToken);
            await settings.SaveAsync(settings.Current with { LastAutoBackupAt = now });
            Prune(settings.Current.AutoBackupKeep);
            logger.LogInformation("Automatic backup written to {Path}", path);
            Completed?.Invoke(path, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The automatic backup failed");
            Completed?.Invoke(null, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Keeps the newest automatic backups and deletes the rest. Backups made by hand have another name and stay.</summary>
    private void Prune(int keep)
    {
        if (!Directory.Exists(paths.BackupsDirectory))
        {
            return;
        }

        var automatic = Directory.EnumerateFiles(paths.BackupsDirectory, AutoPrefix + "*.zip")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(keep);
        foreach (var file in automatic)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "An old automatic backup could not be deleted: {Path}", file);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop is not null)
        {
            await _stop.CancelAsync();
            _stop.Dispose();
        }

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _gate.Dispose();
    }
}
