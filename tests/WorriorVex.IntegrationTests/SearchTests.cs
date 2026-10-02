using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Search;
using WorriorVex.Infrastructure.Content;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.IntegrationTests;

public class SearchTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_word_must_occur_and_a_match_in_the_title_comes_first()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var inBody = await app.Notes.CreateAsync(title: "Meeting notes", content: "<p>We chose SQLite for the architecture.</p>");
        clock.Advance(TimeSpan.FromMinutes(1));
        var inTitle = await app.Notes.CreateAsync(title: "SQLite architecture", content: "<p>Details.</p>");
        clock.Advance(TimeSpan.FromMinutes(1));
        await app.Notes.CreateAsync(title: "Only one word", content: "<p>architecture</p>");
        var trashed = await app.Notes.CreateAsync(title: "Old SQLite architecture", content: "<p>x</p>");
        await app.Trash.MoveToTrashAsync(trashed.Id);

        var found = await app.Search.SearchAsync("architecture sqlite");

        Assert.Equal([inTitle.Id, inBody.Id], found.Select(r => r.NoteId));
        Assert.Empty(await app.Search.SearchAsync("nowhere"));
        Assert.Empty(await app.Search.SearchAsync("   "));
    }

    [Fact]
    public async Task A_word_matches_from_its_start_whatever_the_case_or_accents()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var note = await app.Notes.CreateAsync(title: "Résumé", content: "<p>Zażółć gęślą jaźń. Architecture.</p>");

        foreach (var query in new[] { "resume", "RÉSUMÉ", "res", "gesla", "jazn", "archit", "ARCHITECTURE" })
        {
            Assert.Equal(note.Id, Assert.Single(await app.Search.SearchAsync(query)).NoteId);
        }

        Assert.Empty(await app.Search.SearchAsync("tecture"));
    }

    [Fact]
    public async Task Markup_is_not_searched_only_the_words()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        await app.Notes.CreateAsync(title: "Formatted", content: "<p><strong>bold</strong> text</p><table><tbody><tr><td><p>cell</p></td></tr></tbody></table>");

        Assert.Empty(await app.Search.SearchAsync("strong"));
        Assert.Empty(await app.Search.SearchAsync("tbody"));
        Assert.Single(await app.Search.SearchAsync("bold"));
        Assert.Single(await app.Search.SearchAsync("cell"));
    }

    [Fact]
    public async Task A_quoted_phrase_must_occur_as_written()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var exact = await app.Notes.CreateAsync(title: "A", content: "<p>the search architecture is simple</p>");
        await app.Notes.CreateAsync(title: "B", content: "<p>architecture of the search</p>");

        Assert.Equal(2, (await app.Search.SearchAsync("search architecture")).Count);
        Assert.Equal(exact.Id, Assert.Single(await app.Search.SearchAsync("\"search architecture\"")).NoteId);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("AND")]
    [InlineData("a OR b")]
    [InlineData("NEAR(a b)")]
    [InlineData("title:x")]
    [InlineData("*")]
    [InlineData("(")]
    [InlineData("x\" OR \"y")]
    [InlineData("-")]
    [InlineData("^start")]
    [InlineData("100%")]
    [InlineData("tag:")]
    [InlineData("is:nothing")]
    public async Task Anything_typed_is_searched_for_never_run_as_a_command(string query)
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        await app.Notes.CreateAsync(title: "100% done", content: "<p>a AND b, NEAR the start</p>");

        var found = await app.Search.SearchAsync(query);

        Assert.True(found.Count <= 1);
    }

    [Fact]
    public async Task Tags_can_be_searched_and_filtered_and_follow_changes()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var tagged = await app.Notes.CreateAsync(title: "Plan", content: "<p>roadmap</p>");
        var mention = await app.Notes.CreateAsync(title: "Other", content: "<p>this project is mentioned in the text</p>");
        var tag = await app.Tags.AddToNoteAsync(tagged.Id, "project");

        Assert.Equal(2, (await app.Search.SearchAsync("project")).Count);
        Assert.Equal(tagged.Id, Assert.Single(await app.Search.SearchAsync("tag:project")).NoteId);
        Assert.Equal(tagged.Id, Assert.Single(await app.Search.SearchAsync("tag:#Project roadmap")).NoteId);
        Assert.Empty(await app.Search.SearchAsync("tag:project nowhere"));

        await app.Tags.RenameAsync(tag.Id, "work");
        Assert.Empty(await app.Search.SearchAsync("tag:project"));
        Assert.Single(await app.Search.SearchAsync("tag:work"));

        await app.Notes.UpdateAsync(tagged.Id, "Plan", "<p>roadmap, edited</p>");
        Assert.Single(await app.Search.SearchAsync("tag:work"));

        await app.Tags.DeleteAsync(tag.Id);
        Assert.Empty(await app.Search.SearchAsync("tag:work"));
        Assert.Equal(mention.Id, Assert.Single(await app.Search.SearchAsync("project")).NoteId);
    }

    [Fact]
    public async Task Results_can_be_limited_to_a_notebook_or_folder_and_say_where_each_note_is()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var projects = await app.Notebooks.CreateAsync("My Projects");
        var folder = await app.Tree.CreateFolderAsync(projects.Id, null, "WorriorVex");
        var design = await app.Tree.CreateFolderAsync(projects.Id, folder.Id, "Design");
        var deep = await app.Notes.CreateAsync(parentId: design.Id, title: "Colours", content: "<p>palette</p>");
        var top = await app.Notes.CreateAsync(projects.Id, title: "Overview", content: "<p>palette</p>");
        var inbox = await app.Notes.CreateAsync(title: "Loose", content: "<p>palette</p>");

        var all = await app.Search.SearchAsync("palette");
        Assert.Equal(3, all.Count);
        Assert.Equal("My Projects / WorriorVex / Design", all.Single(r => r.NoteId == deep.Id).Location);
        Assert.Equal("My Projects", all.Single(r => r.NoteId == top.Id).Location);
        Assert.Equal("Inbox", all.Single(r => r.NoteId == inbox.Id).Location);

        Assert.Equal([deep.Id], (await app.Search.SearchAsync("palette in:design")).Select(r => r.NoteId));
        Assert.Equal(2, (await app.Search.SearchAsync("palette in:\"my projects\"")).Count);
        Assert.Equal([inbox.Id], (await app.Search.SearchAsync("in:inbox")).Select(r => r.NoteId));
        Assert.Empty(await app.Search.SearchAsync("palette in:elsewhere"));
    }

    [Fact]
    public async Task The_title_and_a_piece_of_the_text_come_back_with_the_matches_marked()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var filler = string.Join(' ', Enumerable.Repeat("filler", 80));
        await app.Notes.CreateAsync(title: "Search architecture", content: $"<p>{filler} the needle is here {filler}</p>");

        var byTitle = Assert.Single(await app.Search.SearchAsync("architecture"));
        Assert.Equal([new TextSegment("Search ", false), new TextSegment("architecture", true)], byTitle.TitleSegments);
        Assert.StartsWith("filler", string.Concat(byTitle.Snippet.Select(s => s.Text)));

        var byBody = Assert.Single(await app.Search.SearchAsync("needle"));
        Assert.Equal("needle", Assert.Single(byBody.Snippet, s => s.IsMatch).Text);
        var snippet = string.Concat(byBody.Snippet.Select(s => s.Text));
        Assert.Contains("the needle is here", snippet);
        Assert.True(snippet.Length < 250);
        Assert.Equal("Search architecture", string.Concat(byBody.TitleSegments.Select(s => s.Text)));
    }

    [Fact]
    public async Task The_index_follows_edits_restores_and_permanent_deletion()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var note = await app.Notes.CreateAsync(title: "Draft", content: "<p>alpha</p>");

        clock.Advance(TimeSpan.FromHours(1));
        await app.Notes.UpdateAsync(note.Id, "Final", "<p>beta</p>");
        Assert.Empty(await app.Search.SearchAsync("alpha"));
        Assert.Empty(await app.Search.SearchAsync("draft"));
        Assert.Single(await app.Search.SearchAsync("beta"));
        Assert.Single(await app.Search.SearchAsync("final"));

        var revision = Assert.Single(await app.Revisions.ListAsync(note.Id));
        await app.Revisions.RestoreAsync(revision.Id);
        Assert.Single(await app.Search.SearchAsync("alpha"));
        Assert.Empty(await app.Search.SearchAsync("beta"));

        await app.Trash.MoveToTrashAsync(note.Id);
        Assert.Empty(await app.Search.SearchAsync("alpha"));
        await app.Trash.RestoreAsync(note.Id);
        Assert.Single(await app.Search.SearchAsync("alpha"));

        await app.Trash.MoveToTrashAsync(note.Id);
        await app.Trash.EmptyAsync();
        await using var context = await app.Get<IDbContextFactory<WorriorVexDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM NoteSearch").SingleAsync());
    }

    [Fact]
    public async Task An_incomplete_index_is_repaired_at_the_next_start()
    {
        using var data = new TempDataDirectory();
        Guid noteId;
        await using (var first = await TestApp.StartAsync(data.Path))
        {
            noteId = (await first.Notes.CreateAsync(title: "Survivor", content: "<p>findable</p>")).Id;
            await using var context = await first.Get<IDbContextFactory<WorriorVexDbContext>>().CreateDbContextAsync();
            await context.Database.ExecuteSqlAsync($"DELETE FROM NoteSearch");
            Assert.Empty(await first.Search.SearchAsync("findable"));
        }

        await using var second = await TestApp.StartAsync(data.Path);

        Assert.Equal(noteId, Assert.Single(await second.Search.SearchAsync("findable")).NoteId);
    }

    [Theory]
    [InlineData("<p>One</p><p>Two</p>", "One Two")]
    [InlineData("<h1>Title</h1><ul><li><p>a</p></li><li><p>b</p></li></ul>", "Title a b")]
    [InlineData("<p>line<br>break and <strong>bo</strong>ld</p>", "line break and bold")]
    [InlineData("<table><tbody><tr><th><p>H</p></th><td><p>C</p></td></tr></tbody></table>", "H C")]
    [InlineData("<p>a &amp; b &lt;tag&gt;</p>", "a & b <tag>")]
    [InlineData("<img src=\"attachments/x.png\" alt=\"A diagram\"><p>after</p>", "A diagram after")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void The_text_of_a_note_is_its_words_without_markup(string? html, string expected)
    {
        Assert.Equal(expected, NoteText.FromHtml(html));
    }

    [Fact]
    public void A_query_is_split_into_words_phrases_and_filters()
    {
        var query = SearchQuery.Parse("  budget \"q3 plan\" tag:#Finance in:\"My Projects\" is:favourite IS:pinned in:Inbox %  ");

        Assert.Equal(["budget", "q3 plan"], query.Terms);
        Assert.Equal(["Finance"], query.Tags);
        Assert.Equal(["My Projects", "Inbox"], query.Places);
        Assert.True(query.FavoritesOnly);
        Assert.True(query.PinnedOnly);
        Assert.False(query.IsEmpty);
        Assert.True(SearchQuery.Parse("  % \"\" tag: ").IsEmpty);
        Assert.Equal(["http://example.com"], SearchQuery.Parse("http://example.com").Terms);
    }
}
