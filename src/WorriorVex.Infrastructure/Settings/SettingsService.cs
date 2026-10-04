using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Settings;
using WorriorVex.Application.Storage;

namespace WorriorVex.Infrastructure.Settings;

/// <summary>Settings live in <c>settings.json</c> beside the database. A missing or broken file means defaults.</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _writing = new(1, 1);

    public SettingsService(IApplicationDataPathProvider paths, ILogger<SettingsService> logger)
    {
        _path = Path.Combine(paths.DataDirectory, "settings.json");
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var clamped = settings.Clamped();
        await _writing.WaitAsync(cancellationToken);
        try
        {
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(clamped, Json), cancellationToken);
            File.Move(temporary, _path, overwrite: true);
            Current = clamped;
        }
        finally
        {
            _writing.Release();
        }

        Changed?.Invoke(clamped);
    }

    /// <summary>Writes the file without raising <see cref="Changed"/>: used while loading, before anyone listens.</summary>
    private void Write(AppSettings settings)
    {
        try
        {
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The adjusted settings could not be written; they apply for this run");
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var text = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings();
                // A file from before the version field was written with every value filled in, so a default that
                // changed later looks like a choice. The version says which defaults the file was written under.
                var version = text.Contains("\"settingsVersion\"", StringComparison.OrdinalIgnoreCase) ? loaded.SettingsVersion : 0;
                if (version < 1 && loaded.NoteSort == NoteSort.Updated)
                {
                    loaded = loaded with { NoteSort = NoteSort.Added };
                }

                if (version < 2 && !loaded.ConfirmDeletion)
                {
                    loaded = loaded with { ConfirmDeletion = true };
                }

                var current = (loaded with { SettingsVersion = AppSettings.CurrentSettingsVersion }).Clamped();
                if (version < AppSettings.CurrentSettingsVersion)
                {
                    // Written back at once, so the adjustment is made exactly once and later choices are kept.
                    Write(current);
                }

                return current;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The settings file could not be read; defaults are used");
        }

        return new AppSettings();
    }
}
