namespace WorriorVex.UI;

/// <summary>What the host learned while starting, for the UI to show instead of failing silently.</summary>
public sealed class StartupStatus
{
    public string? Error { get; private set; }
    public string? DataDirectory { get; set; }

    /// <summary>A .worriorvex package the app was asked to open (double-clicked, or given on the command line).</summary>
    public string? PackageToImport { get; set; }

    public void Fail(string message) => Error = message;
}
