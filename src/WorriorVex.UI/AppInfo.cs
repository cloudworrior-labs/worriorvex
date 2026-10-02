namespace WorriorVex.UI;

/// <summary>What the app says about itself on the About and Documentation pages.</summary>
/// <param name="Version">Version of the running build, as shown to the user.</param>
/// <param name="DataDirectory">Folder that holds the database, attachments and logs.</param>
public sealed record AppInfo(string Version, string DataDirectory)
{
    public const string ProductName = "WorriorVex";
    public const string Tagline = "Your personal knowledge workspace.";
    public const string Publisher = "Musa Consulting";
    public const string Copyright = "© 2026 Musa Consulting";
    public const string License = "MIT License";

    public static readonly Uri Website = new("https://www.cloudworrior.com");
    public static readonly Uri Repository = new("https://github.com/cloudworrior-labs/worriorvex");
    public static readonly Uri Releases = new("https://github.com/cloudworrior-labs/worriorvex/releases");
    public static readonly Uri Issues = new("https://github.com/cloudworrior-labs/worriorvex/issues");
}
