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

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings()).Clamped();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The settings file could not be read; defaults are used");
        }

        return new AppSettings();
    }
}
