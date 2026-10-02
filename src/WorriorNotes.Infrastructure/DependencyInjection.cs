using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorriorNotes.Application.Attachments;
using WorriorNotes.Application.Links;
using WorriorNotes.Application.Notes;
using WorriorNotes.Application.Revisions;
using WorriorNotes.Application.Search;
using WorriorNotes.Application.Storage;
using WorriorNotes.Application.Tags;
using WorriorNotes.Application.Trash;
using WorriorNotes.Application.Tree;
using WorriorNotes.Infrastructure.Attachments;
using WorriorNotes.Infrastructure.Links;
using WorriorNotes.Infrastructure.Notes;
using WorriorNotes.Infrastructure.Revisions;
using WorriorNotes.Infrastructure.Search;
using WorriorNotes.Infrastructure.Tags;
using WorriorNotes.Infrastructure.Trash;
using WorriorNotes.Infrastructure.Tree;
using WorriorNotes.Infrastructure.Persistence;
using WorriorNotes.Infrastructure.Storage;

namespace WorriorNotes.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers local storage and the application services. No network, no server.</summary>
    public static IServiceCollection AddWorriorNotesInfrastructure(this IServiceCollection services, string? dataDirectory = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IApplicationDataPathProvider>(_ => new ApplicationDataPathProvider(dataDirectory));

        services.AddDbContextFactory<WorriorNotesDbContext>((provider, options) =>
        {
            var paths = provider.GetRequiredService<IApplicationDataPathProvider>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });

        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<INotebookService, NotebookService>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<ITreeService, TreeService>();
        services.AddSingleton<AttachmentFileStore>();
        services.AddSingleton<ITrashService, TrashService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<INoteLinkService, NoteLinkService>();
        services.AddSingleton<IRevisionService, RevisionService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();
        services.AddSingleton<INoteSearchService, NoteSearchService>();
        return services;
    }
}
