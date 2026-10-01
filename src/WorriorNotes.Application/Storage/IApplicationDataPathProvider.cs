namespace WorriorNotes.Application.Storage;

/// <summary>Resolves where WorriorNotes keeps its data on the current platform.</summary>
public interface IApplicationDataPathProvider
{
    /// <summary>Root data directory. Created if it does not exist.</summary>
    string DataDirectory { get; }

    string DatabasePath { get; }
    string AttachmentsDirectory { get; }
    string BackupsDirectory { get; }
    string ExportsDirectory { get; }
    string LogsDirectory { get; }
}
