using WorriorNotes.Application.Storage;

namespace WorriorNotes.Infrastructure.Storage;

/// <summary>
/// Places data under the per-user application data folder of the current OS
/// (Windows: %LOCALAPPDATA%, macOS: ~/Library/Application Support, Linux: $XDG_DATA_HOME or ~/.local/share).
/// The WORRIORNOTES_DATA_DIR environment variable, or an explicit root, overrides the location.
/// </summary>
public sealed class ApplicationDataPathProvider : IApplicationDataPathProvider
{
    public const string DataDirectoryEnvironmentVariable = "WORRIORNOTES_DATA_DIR";
    private const string AppFolderName = "WorriorNotes";
    private const string DatabaseFileName = "worriornotes.db";

    public ApplicationDataPathProvider(string? rootOverride = null)
    {
        var root = rootOverride;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create);
            root = Path.Combine(appData, AppFolderName);
        }

        DataDirectory = Path.GetFullPath(root);
        DatabasePath = Path.Combine(DataDirectory, DatabaseFileName);
        AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
        BackupsDirectory = Path.Combine(DataDirectory, "backups");
        ExportsDirectory = Path.Combine(DataDirectory, "exports");
        LogsDirectory = Path.Combine(DataDirectory, "logs");

        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string AttachmentsDirectory { get; }
    public string BackupsDirectory { get; }
    public string ExportsDirectory { get; }
    public string LogsDirectory { get; }
}
