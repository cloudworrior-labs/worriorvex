using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorriorNotes.Domain;

namespace WorriorNotes.Infrastructure.Persistence;

public sealed class WorriorNotesDbContext(DbContextOptions<WorriorNotesDbContext> options) : DbContext(options)
{
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Note> Notes => Set<Note>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native date type and cannot order by DateTimeOffset.
        // Fixed-width ISO 8601 UTC text sorts correctly and stays readable in exports.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTextConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcTextConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notebook>(notebook =>
        {
            notebook.HasKey(n => n.Id);
            notebook.Property(n => n.Id).ValueGeneratedNever();
            notebook.Property(n => n.Name).HasMaxLength(Notebook.MaxNameLength).IsRequired();
            notebook.HasIndex(n => n.SortOrder);

            // There is exactly one Inbox; the database enforces it so two callers racing
            // to create it cannot both succeed.
            notebook.HasIndex(n => n.Kind).IsUnique().HasFilter($"\"Kind\" = {(int)NotebookKind.Inbox}");
        });

        modelBuilder.Entity<Node>(node =>
        {
            node.HasKey(n => n.Id);
            node.Property(n => n.Id).ValueGeneratedNever();
            node.Property(n => n.Name).HasMaxLength(Node.MaxNameLength).IsRequired();
            node.Ignore(n => n.IsDeleted);

            node.HasOne<Notebook>().WithMany().HasForeignKey(n => n.NotebookId).OnDelete(DeleteBehavior.Cascade);
            node.HasOne<Node>().WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.Restrict);
            node.HasOne(n => n.Note).WithOne().HasForeignKey<Note>(n => n.NodeId).OnDelete(DeleteBehavior.Cascade);

            node.HasIndex(n => n.NotebookId);
            node.HasIndex(n => n.ParentId);
            node.HasIndex(n => n.Name);
            node.HasIndex(n => n.CreatedAt);
            node.HasIndex(n => n.UpdatedAt);
            node.HasIndex(n => n.DeletedAt);
        });

        modelBuilder.Entity<Note>(note =>
        {
            note.HasKey(n => n.NodeId);
            note.Property(n => n.NodeId).ValueGeneratedNever();
            note.Property(n => n.Content).IsRequired();
        });
    }

    private sealed class UtcTextConverter() : ValueConverter<DateTimeOffset, string>(
        value => value.UtcDateTime.ToString(Format, System.Globalization.CultureInfo.InvariantCulture),
        text => DateTimeOffset.ParseExact(
            text,
            Format,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal))
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    }
}
