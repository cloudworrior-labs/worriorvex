namespace WorriorVex.Application.Platform;

/// <summary>The few things the app asks of the operating system outside its own window.</summary>
public interface IPlatformShell
{
    /// <summary>Opens a web page in the user's browser. Only http and https addresses are opened.</summary>
    void OpenWebPage(Uri address);

    /// <summary>Shows a folder in the system's file manager.</summary>
    void OpenFolder(string path);

    /// <summary>
    /// Lets the user choose one file with the system's file dialog.
    /// Returns its path, or <c>null</c> when the dialog was cancelled.
    /// </summary>
    /// <param name="extensions">Extensions to offer, with their dot, such as ".png".</param>
    Task<string?> PickFileAsync(string title, string kind, IReadOnlyCollection<string> extensions);
}
