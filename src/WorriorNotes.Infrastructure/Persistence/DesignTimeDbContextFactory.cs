using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorriorNotes.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the desktop host.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorriorNotesDbContext>
{
    public WorriorNotesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorriorNotesDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new WorriorNotesDbContext(options);
    }
}
