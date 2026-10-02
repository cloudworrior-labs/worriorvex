using System.Reflection;

namespace WorriorVex.UI;

/// <summary>Addresses of the scripts the components load. Each carries the build's version, so a web view never keeps using a script from before an upgrade.</summary>
internal static class UiAssets
{
    private static readonly string Version = Uri.EscapeDataString(
        typeof(UiAssets).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0");

    public static string Editor => $"./_content/WorriorVex.UI/editor/editor.bundle.js?v={Version}";
    public static string Shortcuts => $"./_content/WorriorVex.UI/js/shortcuts.js?v={Version}";
}
