namespace WorriorNotes.UI;

/// <summary>What the host learned while starting, for the UI to show instead of failing silently.</summary>
public sealed class StartupStatus
{
    public string? Error { get; private set; }
    public string? DataDirectory { get; set; }

    public void Fail(string message) => Error = message;
}
