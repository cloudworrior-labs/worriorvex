using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Photino.NET;
using WorriorVex.Application.Platform;

namespace WorriorVex.Desktop;

/// <summary>Hands web addresses and folders to the operating system's default handler.</summary>
internal sealed class PlatformShell(ILogger<PlatformShell> logger) : IPlatformShell
{
    public void OpenWebPage(Uri address)
    {
        if (!address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            logger.LogWarning("Refused to open an address that is not a web page");
            return;
        }

        Start(address.AbsoluteUri);
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Start(path);
        }
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Start(path);
        }
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Window is null)
        {
            return null;
        }

        try
        {
            var chosen = await Window.ShowOpenFolderAsync(title, null, false);
            var path = chosen?.FirstOrDefault(p => !string.IsNullOrEmpty(p));
            return path is not null && Directory.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The folder dialog could not be shown");
            return null;
        }
    }

    public async Task<string?> PickSaveLocationAsync(string title, string suggestedFileName)
    {
        if (Window is null)
        {
            return null;
        }

        try
        {
            var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), suggestedFileName);
            var chosen = await Window.ShowSaveFileAsync(title, start, null);
            return string.IsNullOrWhiteSpace(chosen) ? null : chosen;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The save dialog could not be shown");
            return null;
        }
    }

    /// <summary>The application window; set once it exists. File dialogs belong to it.</summary>
    public PhotinoWindow? Window { get; set; }

    public async Task<string?> PickFileAsync(string title, string kind, IReadOnlyCollection<string> extensions)
    {
        if (Window is null)
        {
            return null;
        }

        try
        {
            var filters = extensions.Count == 0 ? null : new[] { (kind, extensions.Select(e => "*" + e).ToArray()) };
            var chosen = await Window.ShowOpenFileAsync(title, null, false, filters);
            var path = chosen?.FirstOrDefault(p => !string.IsNullOrEmpty(p));
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            // The dialog's filter is a convenience; what was actually chosen is checked here.
            return extensions.Count == 0 || extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The file dialog could not be shown");
            return null;
        }
    }

    private void Start(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The operating system could not open the requested item");
        }
    }
}
