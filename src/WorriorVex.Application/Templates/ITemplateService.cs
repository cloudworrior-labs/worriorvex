namespace WorriorVex.Application.Templates;

public sealed record TemplateSummary(Guid Id, string Title);

/// <summary>
/// Note templates. A template is an ordinary note in the "Templates" notebook, so people edit
/// and add templates the way they edit any note. The notebook is created with a few starters
/// the first time it is needed.
/// </summary>
public interface ITemplateService
{
    const string NotebookName = "Templates";

    /// <summary>The templates, in title order. Creates the Templates notebook with starters when there is none.</summary>
    Task<IReadOnlyList<TemplateSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A new note made from a template, filed in the given place (the Inbox when none). The title has
    /// today's date filled in where the template writes <c>{{date}}</c>; the body likewise.
    /// </summary>
    Task<Notes.NoteDetail> CreateFromAsync(Guid templateId, Guid? notebookId, Guid? parentId, CancellationToken cancellationToken = default);
}
