namespace WorriorVex.Domain;

public enum NotebookKind
{
    /// <summary>A notebook the user created.</summary>
    User = 0,

    /// <summary>The built-in notebook where new notes land until they are filed.</summary>
    Inbox = 1,

    /// <summary>The built-in notebook that holds the calendar's notes. Shown under its own heading.</summary>
    Calendar = 2,
}
