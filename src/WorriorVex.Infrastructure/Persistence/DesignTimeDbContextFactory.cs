using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorriorVex.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the desktop host.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorriorVexDbContext>
{
    public WorriorVexDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorriorVexDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new WorriorVexDbContext(options);
    }
}
