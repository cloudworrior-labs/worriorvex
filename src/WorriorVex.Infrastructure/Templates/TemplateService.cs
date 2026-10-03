using System.Globalization;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Templates;

namespace WorriorVex.Infrastructure.Templates;

public sealed class TemplateService(INoteService notes, INotebookService notebooks, TimeProvider timeProvider) : ITemplateService
{
    /// <summary>The starters that come with a fresh Templates notebook.</summary>
    private static readonly (string Title, string Content)[] Starters =
    [
        ("Meeting notes {{date}}",
            "<h2>Attendees</h2><ul><li></li></ul><h2>Agenda</h2><ol><li></li></ol><h2>Notes</h2><p></p><h2>Actions</h2><ul data-type=\"taskList\"><li data-type=\"taskItem\" data-checked=\"false\"></li></ul>"),
        ("Daily note {{date}}",
            "<h2>Today</h2><ul data-type=\"taskList\"><li data-type=\"taskItem\" data-checked=\"false\"></li></ul><h2>Notes</h2><p></p><h2>Tomorrow</h2><ul><li></li></ul>"),
        ("Project",
            "<h2>Goal</h2><p></p><h2>Why</h2><p></p><h2>Plan</h2><ol><li></li></ol><h2>Open questions</h2><ul><li></li></ul><h2>Links</h2><ul><li></li></ul>"),
    ];

    public async Task<IReadOnlyList<TemplateSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var notebook = await TemplatesNotebookAsync(cancellationToken);
        var list = await notes.ListAsync(notebook.Id, null, cancellationToken);
        return [.. list.OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).Select(n => new TemplateSummary(n.Id, n.Title))];
    }

    public async Task<NoteDetail> CreateFromAsync(Guid templateId, Guid? notebookId, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var template = await notes.GetAsync(templateId, cancellationToken)
            ?? throw new InvalidOperationException("The template no longer exists.");
        var today = timeProvider.GetLocalNow().ToString("d", CultureInfo.CurrentCulture);
        return await notes.CreateAsync(notebookId, parentId, Fill(template.Title, today), Fill(template.Content, today), cancellationToken);
    }

    private static string Fill(string text, string date) => text.Replace("{{date}}", date, StringComparison.OrdinalIgnoreCase);

    private async Task<NotebookSummary> TemplatesNotebookAsync(CancellationToken cancellationToken)
    {
        var existing = (await notebooks.ListAsync(cancellationToken))
            .FirstOrDefault(n => string.Equals(n.Name, ITemplateService.NotebookName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var created = await notebooks.CreateAsync(ITemplateService.NotebookName, cancellationToken);
        foreach (var (title, content) in Starters)
        {
            await notes.CreateAsync(created.Id, null, title, content, cancellationToken);
        }

        return created;
    }
}
