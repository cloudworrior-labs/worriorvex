using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorriorVex.Application.Attachments;
using WorriorVex.Application.Backup;
using WorriorVex.Application.Export;
using WorriorVex.Application.Content;
using WorriorVex.Application.Import;
using WorriorVex.Application.Links;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Revisions;
using WorriorVex.Application.Search;
using WorriorVex.Application.Settings;
using WorriorVex.Application.Storage;
using WorriorVex.Application.Tags;
using WorriorVex.Application.Trash;
using WorriorVex.Application.Tree;
using WorriorVex.Application.Updates;
using WorriorVex.Infrastructure.Attachments;
using WorriorVex.Infrastructure.Backup;
using WorriorVex.Infrastructure.Export;
using WorriorVex.Infrastructure.Content;
using WorriorVex.Infrastructure.Import;
using WorriorVex.Infrastructure.Links;
using WorriorVex.Infrastructure.Notes;
using WorriorVex.Infrastructure.Revisions;
using WorriorVex.Infrastructure.Search;
using WorriorVex.Infrastructure.Settings;
using WorriorVex.Infrastructure.Tags;
using WorriorVex.Infrastructure.Trash;
using WorriorVex.Infrastructure.Tree;
using WorriorVex.Infrastructure.Updates;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Storage;

namespace WorriorVex.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers local storage and the application services. No network, no server.</summary>
    public static IServiceCollection AddWorriorVexInfrastructure(this IServiceCollection services, string? dataDirectory = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IApplicationDataPathProvider>(_ => new ApplicationDataPathProvider(dataDirectory));

        services.AddDbContextFactory<WorriorVexDbContext>((provider, options) =>
        {
            var paths = provider.GetRequiredService<IApplicationDataPathProvider>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });

        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<INoteHtmlSanitizer, NoteHtmlSanitizer>();
        services.AddSingleton<INotebookService, NotebookService>();
        services.AddSingleton<WorriorVex.Application.Templates.ITemplateService, Templates.TemplateService>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<ITreeService, TreeService>();
        services.AddSingleton<AttachmentFileStore>();
        services.AddSingleton<ITrashService, TrashService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<INoteLinkService, NoteLinkService>();
        services.AddSingleton<IRevisionService, RevisionService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();
        services.AddSingleton<INoteSearchService, NoteSearchService>();
        services.AddSingleton<IKeepNoteImporter, KeepNoteImporter>();
        services.AddSingleton<IPackageImporter, Import.PackageImporter>();
        services.AddSingleton<IBackupScheduler, Backup.BackupScheduler>();
        services.AddSingleton<IAttachmentIntegrity, Attachments.AttachmentIntegrity>();
        services.AddSingleton<IMarkdownImporter, Import.MarkdownImporter>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUpdateChecker>(provider => new GitHubUpdateChecker(provider.GetRequiredService<ILogger<GitHubUpdateChecker>>()));
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IExportService, ExportService>();
        return services;
    }
}
