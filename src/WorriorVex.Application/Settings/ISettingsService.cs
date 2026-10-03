namespace WorriorVex.Application.Settings;

public enum ThemeSetting
{
    System,
    Light,
    Dark,
}

/// <summary>What the user can adjust. Kept small on purpose; nothing technical is exposed.</summary>
public sealed record AppSettings
{
    public const int MinFontSize = 12;
    public const int MaxFontSize = 24;
    public const int MinAutosaveMilliseconds = 300;
    public const int MaxAutosaveMilliseconds = 5000;

    public ThemeSetting Theme { get; init; } = ThemeSetting.System;

    /// <summary>Font size of the note text, in pixels.</summary>
    public int EditorFontSize { get; init; } = 15;

    public double EditorLineHeight { get; init; } = 1.6;

    public bool SpellCheck { get; init; } = true;

    /// <summary>How long after the last keystroke a note is saved.</summary>
    public int AutosaveDelayMilliseconds { get; init; } = 700;

    /// <summary>Ask before moving a note to the trash.</summary>
    public bool ConfirmDeletion { get; init; }

    /// <summary>
    /// Ask github.com for a newer version when the app starts. Off by default: the app otherwise
    /// never touches the network.
    /// </summary>
    public bool CheckForUpdates { get; init; }

    /// <summary>Open where the user left off instead of the Inbox.</summary>
    public bool OpenLastPlace { get; init; } = true;

    public const int MinPaneWidth = 140;
    public const int MaxPaneWidth = 600;

    /// <summary>Width of the navigation pane in pixels.</summary>
    public int NavigationWidth { get; init; } = 200;

    /// <summary>Width of the note list pane in pixels.</summary>
    public int ListWidth { get; init; } = 280;

    /// <summary>Where the user was when the app closed: a notebook id and, inside it, a folder id.</summary>
    public Guid? LastNotebookId { get; init; }

    public Guid? LastFolderId { get; init; }

    /// <summary>The same settings with every value inside its allowed range.</summary>
    public AppSettings Clamped() => this with
    {
        EditorFontSize = Math.Clamp(EditorFontSize, MinFontSize, MaxFontSize),
        EditorLineHeight = Math.Clamp(EditorLineHeight, 1.2, 2.2),
        AutosaveDelayMilliseconds = Math.Clamp(AutosaveDelayMilliseconds, MinAutosaveMilliseconds, MaxAutosaveMilliseconds),
        NavigationWidth = Math.Clamp(NavigationWidth, MinPaneWidth, MaxPaneWidth),
        ListWidth = Math.Clamp(ListWidth, MinPaneWidth, MaxPaneWidth),
    };
}

/// <summary>Reads and writes the settings file in the data folder. Raises <see cref="Changed"/> after a save.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event Action<AppSettings>? Changed;

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
