using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorriorNotes.Application.Notes;
using WorriorNotes.Application.Storage;
using WorriorNotes.Infrastructure.Notes;
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
        return services;
    }
}
