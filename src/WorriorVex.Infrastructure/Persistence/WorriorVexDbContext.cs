using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorriorVex.Domain;

namespace WorriorVex.Infrastructure.Persistence;

public sealed class WorriorVexDbContext(DbContextOptions<WorriorVexDbContext> options) : DbContext(options)
{
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<NoteTag> NoteTags => Set<NoteTag>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<NoteLink> NoteLinks => Set<NoteLink>();
    public DbSet<NoteRevision> NoteRevisions => Set<NoteRevision>();

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
            notebook.Ignore(n => n.IsDeleted);
            notebook.HasIndex(n => n.SortOrder);
            notebook.HasIndex(n => n.DeletedAt);

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
            // Rows are only ever removed by emptying the trash, and then a folder goes with all it holds.
            node.HasOne<Node>().WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.HasKey(t => t.Id);
            tag.Property(t => t.Id).ValueGeneratedNever();
            tag.Property(t => t.Name).HasMaxLength(Tag.MaxNameLength).IsRequired();
            tag.Property(t => t.NormalizedName).HasMaxLength(Tag.MaxNameLength).IsRequired();
            tag.HasIndex(t => t.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<NoteTag>(noteTag =>
        {
            noteTag.HasKey(nt => new { nt.NoteId, nt.TagId });
            noteTag.HasOne<Note>().WithMany().HasForeignKey(nt => nt.NoteId).OnDelete(DeleteBehavior.Cascade);
            noteTag.HasOne<Tag>().WithMany().HasForeignKey(nt => nt.TagId).OnDelete(DeleteBehavior.Cascade);
            noteTag.HasIndex(nt => nt.TagId);
        });

        modelBuilder.Entity<Attachment>(attachment =>
        {
            attachment.HasKey(a => a.Id);
            attachment.Property(a => a.Id).ValueGeneratedNever();
            attachment.Property(a => a.OriginalFileName).HasMaxLength(Attachment.MaxFileNameLength).IsRequired();
            attachment.Property(a => a.StoredFileName).HasMaxLength(64).IsRequired();
            attachment.Property(a => a.ContentType).HasMaxLength(127).IsRequired();
            attachment.Property(a => a.Hash).HasMaxLength(64).IsRequired();
            attachment.HasOne<Note>().WithMany().HasForeignKey(a => a.NoteId).OnDelete(DeleteBehavior.Cascade);
            attachment.HasIndex(a => a.NoteId);
            attachment.HasIndex(a => a.StoredFileName).IsUnique();
            attachment.HasIndex(a => a.Hash);
        });

        modelBuilder.Entity<NoteLink>(link =>
        {
            link.HasKey(l => l.Id);
            link.Property(l => l.Id).ValueGeneratedNever();
            link.HasOne<Note>().WithMany().HasForeignKey(l => l.SourceNoteId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne<Note>().WithMany().HasForeignKey(l => l.TargetNoteId).OnDelete(DeleteBehavior.Cascade);
            link.HasIndex(l => new { l.SourceNoteId, l.TargetNoteId }).IsUnique();
            link.HasIndex(l => l.TargetNoteId);
        });

        modelBuilder.Entity<NoteRevision>(revision =>
        {
            revision.HasKey(r => r.Id);
            revision.Property(r => r.Id).ValueGeneratedNever();
            revision.Property(r => r.Title).HasMaxLength(Node.MaxNameLength).IsRequired();
            revision.Property(r => r.Content).IsRequired();
            revision.Property(r => r.ChangeReason).HasMaxLength(NoteRevision.MaxChangeReasonLength).IsRequired();
            revision.HasOne<Note>().WithMany().HasForeignKey(r => r.NoteId).OnDelete(DeleteBehavior.Cascade);
            revision.HasIndex(r => new { r.NoteId, r.CreatedAt });
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
