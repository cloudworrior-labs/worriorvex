using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Common;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Tags;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Search;

namespace WorriorVex.Infrastructure.Tags;

public sealed class TagService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    TimeProvider timeProvider) : ITagService
{
    public async Task<IReadOnlyList<TagSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Summaries(context, context.Tags).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagSummary>> GetForNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var onNote = context.Tags.Where(t => context.NoteTags.Any(nt => nt.TagId == t.Id && nt.NoteId == noteId));
        return await Summaries(context, onNote).ToListAsync(cancellationToken);
    }

    public async Task<TagSummary> AddToNoteAsync(Guid noteId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = Tag.NormalizeName(name);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Nodes.AnyAsync(n => n.Id == noteId && n.Type == NodeType.Note, cancellationToken))
        {
            throw new NoteNotFoundException(noteId);
        }

        var tag = await context.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalized, cancellationToken);
        if (tag is null)
        {
            tag = Tag.Create(name, timeProvider.GetUtcNow());
            context.Tags.Add(tag);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Someone else created the same tag in the meantime. Use theirs.
                context.Entry(tag).State = EntityState.Detached;
                tag = await context.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalized, cancellationToken);
                if (tag is null)
                {
                    throw;
                }
            }
        }

        if (!await context.NoteTags.AnyAsync(nt => nt.NoteId == noteId && nt.TagId == tag.Id, cancellationToken))
        {
            context.NoteTags.Add(NoteTag.Create(noteId, tag.Id));
            await context.SaveChangesAsync(cancellationToken);
            await SearchIndex.RefreshTagsAsync(context, [noteId], cancellationToken);
        }

        return await Summaries(context, context.Tags.Where(t => t.Id == tag.Id)).FirstAsync(cancellationToken);
    }

    public async Task RemoveFromNoteAsync(Guid noteId, Guid tagId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.NoteTags.Where(nt => nt.NoteId == noteId && nt.TagId == tagId).ExecuteDeleteAsync(cancellationToken);
        await SearchIndex.RefreshTagsAsync(context, [noteId], cancellationToken);
    }

    public async Task<TagSummary> RenameAsync(Guid tagId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = Tag.NormalizeName(name);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tag = await context.Tags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken)
            ?? throw new EntityNotFoundException("Tag", tagId);
        if (await context.Tags.AnyAsync(t => t.NormalizedName == normalized && t.Id != tagId, cancellationToken))
        {
            throw new DomainException("There is already a tag with that name.");
        }

        tag.Rename(name);
        await context.SaveChangesAsync(cancellationToken);
        await SearchIndex.RefreshTagsAsync(context, await NotesWithAsync(context, tagId, cancellationToken), cancellationToken);
        return await Summaries(context, context.Tags.Where(t => t.Id == tagId)).FirstAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tagged = await NotesWithAsync(context, tagId, cancellationToken);
        await context.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync(cancellationToken);
        await SearchIndex.RefreshTagsAsync(context, tagged, cancellationToken);
    }

    private static Task<List<Guid>> NotesWithAsync(WorriorVexDbContext context, Guid tagId, CancellationToken cancellationToken) =>
        context.NoteTags.Where(nt => nt.TagId == tagId).Select(nt => nt.NoteId).ToListAsync(cancellationToken);

    private static IQueryable<TagSummary> Summaries(WorriorVexDbContext context, IQueryable<Tag> tags) =>
        tags.AsNoTracking()
            .OrderBy(t => t.NormalizedName)
            .Select(t => new TagSummary(
                t.Id,
                t.Name,
                context.NoteTags.Count(nt => nt.TagId == t.Id && context.Nodes.Any(n => n.Id == nt.NoteId && n.DeletedAt == null))));
}
