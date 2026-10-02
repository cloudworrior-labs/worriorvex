using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Photino.Blazor;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Platform;
using WorriorVex.Application.Storage;
using WorriorVex.Infrastructure;
using WorriorVex.Infrastructure.Attachments;
using WorriorVex.Infrastructure.Logging;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Storage;
using WorriorVex.UI;

namespace WorriorVex.Desktop;

internal static class Program
{
    private static readonly TimeSpan SaveOnCloseTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    private static void Main(string[] args)
    {
        var paths = new ApplicationDataPathProvider();
        var startup = new StartupStatus { DataDirectory = paths.DataDirectory };

        // The window loads the UI from wwwroot and the images in notes from the attachments folder.
        var files = new CompositeFileProvider(
            new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")),
            new AttachmentImageFileProvider(paths.AttachmentsDirectory));
        var builder = PhotinoBlazorAppBuilder.CreateDefault(files, args);
        builder.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            logging.AddProvider(new FileLoggerProvider(paths.LogsDirectory));
        });
        builder.Services.AddWorriorVexInfrastructure(paths.DataDirectory);
        builder.Services.AddSingleton(startup);
        builder.Services.AddSingleton(new AppInfo(DisplayVersion(), paths.DataDirectory));
        builder.Services.AddSingleton<PlatformShell>();
        builder.Services.AddSingleton<IPlatformShell>(provider => provider.GetRequiredService<PlatformShell>());
        builder.Services.AddSingleton(provider => new NoteAutosaver(
            provider.GetRequiredService<INoteService>(),
            provider.GetRequiredService<TimeProvider>()));
        builder.RootComponents.Add<App>("app");

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WorriorVex.Desktop");
        logger.LogInformation(
            "Starting WorriorVex {Version} on {OS}; data in {DataDirectory}",
            typeof(Program).Assembly.GetName().Version,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            paths.DataDirectory);

        try
        {
            // Off the UI thread: nothing here may wait on the window's message loop.
            Task.Run(() => app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The database could not be opened or migrated");
            startup.Fail("The notes database could not be opened. Details are in the log file in the logs folder.");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");

        app.Services.GetRequiredService<PlatformShell>().Window = app.MainWindow;
        var autosaver = app.Services.GetRequiredService<NoteAutosaver>();
        if (!OperatingSystem.IsMacOS())
        {
            // On macOS the icon comes from the application bundle.
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", OperatingSystem.IsWindows() ? "worriorvex.ico" : "worriorvex.png");
            if (File.Exists(icon))
            {
                app.MainWindow.SetIconFile(icon);
            }
        }

        app.MainWindow
            .SetTitle("WorriorVex")
            .SetSize(1280, 800)
            .SetMinSize(760, 480)
            .Center()
            .RegisterWindowClosingHandler((_, _) =>
            {
                // Last chance to write edits still waiting for the autosave delay.
                var flush = Task.Run(() => autosaver.FlushAsync());
                if (!flush.Wait(SaveOnCloseTimeout) || !flush.Result)
                {
                    logger.LogError("Edits could not be saved while closing");
                }

                return false;
            });

        app.Run();
        logger.LogInformation("WorriorVex closed");
    }

    /// <summary>"0.1.0 (1a2b3c4)": the version, and the commit it was built from when the build recorded one.</summary>
    private static string DisplayVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var parts = informational.Split('+', 2);
        return parts.Length == 2 && parts[1].Length >= 7 ? $"{parts[0]} ({parts[1][..7]})" : parts[0];
    }
}
