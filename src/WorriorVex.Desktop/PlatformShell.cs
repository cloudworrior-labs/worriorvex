using System.Diagnostics;
using Microsoft.Extensions.Logging;
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
