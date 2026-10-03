using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Platform;
using WorriorVex.Application.Storage;
using WorriorVex.Infrastructure;
using WorriorVex.Infrastructure.Attachments;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.UI;

namespace WorriorVex.UITests;

/// <summary>
/// The interface served over HTTP from a throwaway data folder, the way the desktop shell serves it
/// from its window. Each test class gets its own host and folder.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private WebApplication? _app;

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "worriorvex-uitests", Guid.NewGuid().ToString("N"));
    public string Url { get; private set; } = string.Empty;
    public FakeShell Shell { get; } = new();

    /// <summary>Warnings and errors the app logged, for assertions and for reading when a test fails.</summary>
    public List<string> Logs { get; } = [];

    /// <summary>A package path to hand the app at start, as "Open with WorriorVex" would.</summary>
    public string? PackageToImport { get; set; }

    public async Task StartAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AppHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.UseStaticWebAssets();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(new ListLoggerProvider(Logs));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddWorriorVexInfrastructure(DataDirectory);
        builder.Services.AddWorriorVexUI();
        builder.Services.AddSingleton(new StartupStatus { DataDirectory = DataDirectory, PackageToImport = PackageToImport });
        builder.Services.AddSingleton(new AppInfo("0.0.0 (ui tests)", DataDirectory));
        builder.Services.AddSingleton<IPlatformShell>(Shell);
        builder.Services.AddSingleton(p => new NoteAutosaver(p.GetRequiredService<INoteService>(), p.GetRequiredService<TimeProvider>()));

        _app = builder.Build();
        await _app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        _app.UseStaticFiles();
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new AttachmentImageFileProvider(_app.Services.GetRequiredService<IApplicationDataPathProvider>().AttachmentsDirectory),
        });
        _app.UseAntiforgery();
        _app.MapRazorComponents<Host>().AddInteractiveServerRenderMode();
        await _app.StartAsync();
        // The bound port, not the ":0" that was asked for.
        var addresses = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        Url = addresses.Addresses.First().TrimEnd('/') + "/";
    }

    public T Get<T>() where T : notnull => _app!.Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Left for the operating system to clean up.
        }
    }
}

/// <summary>A platform shell whose dialogs answer with whatever the test set up.</summary>
public sealed class FakeShell : IPlatformShell
{
    public List<Uri> OpenedPages { get; } = [];
    public string? NextFolder { get; set; }
    public string? NextFile { get; set; }
    public string? NextSaveLocation { get; set; }

    public void OpenWebPage(Uri address) => OpenedPages.Add(address);
    public void OpenFolder(string path) { }
    public void OpenFile(string path) { }
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(NextFolder);
    public Task<string?> PickFileAsync(string title, string kind, IReadOnlyCollection<string> extensions) => Task.FromResult(NextFile);
    public Task<string?> PickSaveLocationAsync(string title, string suggestedFileName) => Task.FromResult(NextSaveLocation);
}

internal sealed class ListLoggerProvider(List<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, sink);
    public void Dispose() { }

    private sealed class ListLogger(string category, List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink)
            {
                sink.Add($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? "" : " :: " + exception)}");
            }
        }
    }
}
