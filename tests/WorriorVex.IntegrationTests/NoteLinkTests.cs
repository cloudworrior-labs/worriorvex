using WorriorVex.Application.Content;
using WorriorVex.Infrastructure.Content;

namespace WorriorVex.IntegrationTests;

public class NoteLinkTests
{
    private static string LinkTo(Guid id, string text) => $"<a href=\"{NoteContentRules.NoteLinkAddress(id)}\">{text}</a>";

    [Fact]
    public void A_link_to_another_note_survives_sanitising_in_one_form_and_a_bad_one_does_not()
    {
        var sanitizer = new NoteHtmlSanitizer();
        var id = Guid.NewGuid();

        var kept = sanitizer.Sanitize($"<p>See <a href=\"NOTE:{id.ToString("D").ToUpperInvariant()}\" target=\"_blank\" rel=\"x\">the plan</a></p>");
        Assert.Equal($"<p>See {LinkTo(id, "the plan")}</p>", kept);

        Assert.Equal("<p><a>bad</a></p>", sanitizer.Sanitize("<p><a href=\"note:not-a-guid\">bad</a></p>"));
        Assert.Equal("<p><a>bad</a></p>", sanitizer.Sanitize($"<p><a href=\"note:{Guid.Empty}\">bad</a></p>"));
        Assert.Equal("<p><a>bad</a></p>", sanitizer.Sanitize($"<p><a href=\"note:{id}/../x\">bad</a></p>"));
    }

    [Fact]
    public void The_notes_a_text_links_to_are_read_from_it()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var html = $"<p>{LinkTo(a, "A")} and {LinkTo(b, "B")} and {LinkTo(a, "A again")} and <a href=\"https://x.example/\">web</a></p>";

        var targets = NoteContentRules.NoteLinkTargets(html);

        Assert.Equal(new[] { a, b }.Order(), targets.Order());
        Assert.Equal(a, NoteContentRules.NoteLinkTarget(NoteContentRules.NoteLinkAddress(a)));
        Assert.Null(NoteContentRules.NoteLinkTarget("https://example.com"));
        Assert.Empty(NoteContentRules.NoteLinkTargets(null));
    }

    [Fact]
    public async Task Links_written_in_a_note_are_recorded_and_show_as_backlinks_on_the_other_note()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var plan = await app.Notes.CreateAsync(title: "Plan");
        var design = await app.Notes.CreateAsync(title: "Design");
        var meeting = await app.Notes.CreateAsync(title: "Meeting", content: $"<p>See {LinkTo(plan.Id, "the plan")} and {LinkTo(design.Id, "design")}.</p>");

        Assert.Equal(["Design", "Plan"], (await app.Links.GetLinksAsync(meeting.Id)).Select(l => l.Title));
        Assert.Equal(meeting.Id, Assert.Single(await app.Links.GetBacklinksAsync(plan.Id)).NoteId);

        // Removing a link from the text removes the record; a link to a note that does not exist is not recorded.
        await app.Notes.UpdateAsync(meeting.Id, "Meeting", $"<p>Only {LinkTo(design.Id, "design")} and {LinkTo(Guid.NewGuid(), "nothing")} and {LinkTo(meeting.Id, "myself")}.</p>");

        Assert.Equal(["Design"], (await app.Links.GetLinksAsync(meeting.Id)).Select(l => l.Title));
        Assert.Empty(await app.Links.GetBacklinksAsync(plan.Id));
        Assert.Empty(await app.Links.GetBacklinksAsync(meeting.Id));
    }

    [Fact]
    public async Task A_duplicate_and_a_restored_revision_carry_their_links_too()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(data, clock);
        var target = await app.Notes.CreateAsync(title: "Target");
        var source = await app.Notes.CreateAsync(title: "Source", content: $"<p>{LinkTo(target.Id, "target")}</p>");

        var copy = await app.Notes.DuplicateAsync(source.Id);
        Assert.Equal(2, (await app.Links.GetBacklinksAsync(target.Id)).Count);

        clock.Advance(TimeSpan.FromMinutes(15));
        await app.Notes.UpdateAsync(source.Id, "Source", "<p>no link any more</p>");
        Assert.Equal([copy.Id], (await app.Links.GetBacklinksAsync(target.Id)).Select(l => l.NoteId));

        var earlier = Assert.Single(await app.Revisions.ListAsync(source.Id));
        await app.Revisions.RestoreAsync(earlier.Id);
        Assert.Equal(2, (await app.Links.GetBacklinksAsync(target.Id)).Count);
    }

    [Fact]
    public async Task Related_notes_share_tags_and_are_not_already_linked()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(data, clock);
        var here = await app.Notes.CreateAsync(title: "Here");
        var twoShared = await app.Notes.CreateAsync(title: "Two shared");
        clock.Advance(TimeSpan.FromMinutes(1));
        var oneShared = await app.Notes.CreateAsync(title: "One shared");
        var linked = await app.Notes.CreateAsync(title: "Linked");
        var trashed = await app.Notes.CreateAsync(title: "Trashed");
        await app.Notes.CreateAsync(title: "Unrelated");

        foreach (var id in new[] { here.Id, twoShared.Id, oneShared.Id, linked.Id, trashed.Id })
        {
            await app.Tags.AddToNoteAsync(id, "alpha");
        }

        await app.Tags.AddToNoteAsync(here.Id, "beta");
        await app.Tags.AddToNoteAsync(twoShared.Id, "beta");
        await app.Notes.UpdateAsync(here.Id, "Here", $"<p>{LinkTo(linked.Id, "linked")}</p>");
        await app.Trash.MoveToTrashAsync(trashed.Id);

        var related = await app.Links.GetRelatedAsync(here.Id);

        Assert.Equal(["Two shared", "One shared"], related.Select(r => r.Title));
        Assert.Equal(["Two shared"], (await app.Links.GetRelatedAsync(here.Id, 1)).Select(r => r.Title));
        Assert.Empty(await app.Links.GetRelatedAsync(Guid.NewGuid()));
    }
}
