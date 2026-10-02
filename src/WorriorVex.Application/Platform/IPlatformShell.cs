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
    /// <param name="extensions">Extensions to offer, with their dot, such as ".png"; empty for any file.</param>
    Task<string?> PickFileAsync(string title, string kind, IReadOnlyCollection<string> extensions);

    /// <summary>Lets the user choose a folder. Returns its path, or <c>null</c> when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Lets the user choose where to save a file, starting from a suggested name.
    /// Returns the chosen path, or <c>null</c> when cancelled.
    /// </summary>
    Task<string?> PickSaveLocationAsync(string title, string suggestedFileName);

    /// <summary>Opens a file with the program the operating system uses for it.</summary>
    void OpenFile(string path);
}
