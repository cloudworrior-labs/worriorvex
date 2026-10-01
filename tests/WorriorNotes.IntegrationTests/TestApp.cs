using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorriorNotes.Application.Notes;
using WorriorNotes.Infrastructure;
using WorriorNotes.Infrastructure.Persistence;

namespace WorriorNotes.IntegrationTests;

/// <summary>
/// One "run" of the application over a data directory on disk. Disposing it and
/// starting another over the same directory is the same as restarting the app.
/// </summary>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private TestApp(ServiceProvider provider) => _provider = provider;

    public INoteService Notes => _provider.GetRequiredService<INoteService>();
    public INotebookService Notebooks => _provider.GetRequiredService<INotebookService>();
    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public static async Task<TestApp> StartAsync(string dataDirectory, TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        services.AddWorriorNotesInfrastructure(dataDirectory);
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        return new TestApp(provider);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}

/// <summary>A throwaway data directory, removed after the test.</summary>
internal sealed class TempDataDirectory : IDisposable
{
    public TempDataDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "worriornotes-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
