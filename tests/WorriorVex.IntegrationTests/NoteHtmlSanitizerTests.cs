using WorriorVex.Application.Content;
using WorriorVex.Infrastructure.Content;

namespace WorriorVex.IntegrationTests;

public class NoteHtmlSanitizerTests
{
    private const string Image = "attachments/0123456789abcdef0123456789abcdef.png";
    private readonly NoteHtmlSanitizer _sanitizer = new();

    [Theory]
    [InlineData("<p>Plain <strong>bold</strong> <em>italic</em> <u>under</u> <s>struck</s> <code>code</code></p>")]
    [InlineData("<h1>One</h1><h2>Two</h2><h3>Three</h3><blockquote><p>Quote</p></blockquote><hr>")]
    [InlineData("<ul><li><p>a</p></li></ul><ol start=\"3\"><li><p>b</p></li></ol>")]
    [InlineData("<pre><code class=\"language-csharp\">var x = 1 &lt; 2;</code></pre>")]
    [InlineData("<ul data-type=\"taskList\"><li data-checked=\"true\" data-type=\"taskItem\"><label><input type=\"checkbox\" checked=\"checked\"><span></span></label><div><p>done</p></div></li></ul>")]
    [InlineData("<table><tbody><tr><th colspan=\"1\" rowspan=\"1\" colwidth=\"120\"><p>Head</p></th></tr><tr><td colspan=\"2\" rowspan=\"1\"><p>Cell</p></td></tr></tbody></table>")]
    [InlineData("<p><a target=\"_blank\" rel=\"noopener noreferrer nofollow\" href=\"https://www.cloudworrior.com/a?b=1&amp;c=2\">link</a></p>")]
    [InlineData("<p>Zażółć gęślą jaźń ✓</p>")]
    public void What_the_editor_writes_is_kept_as_it_is(string html)
    {
        Assert.Equal(html, _sanitizer.Sanitize(html));
    }

    [Fact]
    public void An_image_of_an_attachment_is_kept()
    {
        var html = $"<p>See</p><img src=\"{Image}\" alt=\"Diagram\" title=\"A diagram\">";

        Assert.Equal(html, _sanitizer.Sanitize(html));
    }

    [Theory]
    [InlineData("<img src=\"https://tracker.example/pixel.gif\">")]
    [InlineData("<img src=\"http://example.com/a.png\">")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<img src=\"attachments/../../worriorvex.db\">")]
    [InlineData("<img src=\"attachments/0123456789abcdef0123456789abcdef.svg\">")]
    [InlineData("<img src=\"/etc/passwd\">")]
    [InlineData("<img src=\"file:///etc/passwd\">")]
    [InlineData("<img alt=\"no source\">")]
    public void Any_other_image_is_removed(string image)
    {
        Assert.Equal("<p>before</p><p>after</p>", _sanitizer.Sanitize($"<p>before</p>{image}<p>after</p>"));
    }

    [Theory]
    [InlineData("<p><mark>marked</mark> text</p>", "<p><mark>marked</mark> text</p>")]
    [InlineData("<div data-callout=\"warning\" class=\"wn-callout\"><p>mind</p></div>", "<div data-callout=\"warning\"><p>mind</p></div>")]
    [InlineData("<img src=\"attachments/0123456789abcdef0123456789abcdef.png\" width=\"240\">", "<img src=\"attachments/0123456789abcdef0123456789abcdef.png\" width=\"240\">")]
    [InlineData("<table><colgroup><col style=\"width: 120px\"><col></colgroup><tbody><tr><td>a</td><td>b</td></tr></tbody></table>", "<table><colgroup><col style=\"width: 120px\"><col></colgroup><tbody><tr><td>a</td><td>b</td></tr></tbody></table>")]
    public void Marks_callouts_image_widths_and_column_widths_are_kept(string html, string expected)
    {
        Assert.Equal(expected, _sanitizer.Sanitize(html));
    }

    [Theory]
    [InlineData("<table><colgroup><col style=\"width: 10px; background: url(https://x.example/y)\"></colgroup></table>", "<table><colgroup><col style=\"width: 10px\"></colgroup></table>")]
    [InlineData("<p style=\"width: 10px\">a</p>", "<p>a</p>")]
    [InlineData("<p>a</p><script>alert(1)</script>", "<p>a</p>")]
    [InlineData("<p onclick=\"alert(1)\" onmouseover=\"x()\">a</p>", "<p>a</p>")]
    [InlineData("<p style=\"background:url(https://x.example/y)\">a</p>", "<p>a</p>")]
    [InlineData("<style>p{color:red}</style><p>a</p>", "<p>a</p>")]
    [InlineData("<iframe src=\"https://example.com\"></iframe><p>a</p>", "<p>a</p>")]
    [InlineData("<object data=\"x.swf\"></object><embed src=\"x.swf\"><p>a</p>", "<p>a</p>")]
    [InlineData("<form action=\"https://example.com\"><input type=\"text\" name=\"q\"><button>go</button></form><p>a</p>", "<p>a</p>")]
    [InlineData("<svg onload=\"alert(1)\"><circle r=\"1\"></circle></svg><p>a</p>", "<p>a</p>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://example.com\"><p>a</p>", "<p>a</p>")]
    [InlineData("<p class=\"evil language-js\" id=\"x\">a</p>", "<p>a</p>")]
    [InlineData("<p src=\"https://example.com/x\" href=\"https://example.com\">a</p>", "<p>a</p>")]
    public void Anything_that_could_run_load_or_restyle_is_removed(string html, string expected)
    {
        Assert.Equal(expected, _sanitizer.Sanitize(html));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("vbscript:x")]
    [InlineData("/relative/path")]
    [InlineData("attachments/0123456789abcdef0123456789abcdef.png")]
    public void A_link_that_is_not_a_web_address_keeps_its_words_but_stops_being_a_link(string address)
    {
        var cleaned = _sanitizer.Sanitize($"<p><a href=\"{address}\">words</a></p>");

        Assert.Equal("<p><a>words</a></p>", cleaned);
    }

    [Fact]
    public void A_web_link_always_opens_outside_and_tells_the_site_nothing()
    {
        var cleaned = _sanitizer.Sanitize("<p><a href=\"https://example.com/\" target=\"_self\" rel=\"opener\">x</a></p>");

        Assert.Equal("<p><a href=\"https://example.com/\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">x</a></p>", cleaned);
    }

    [Fact]
    public void Sanitising_twice_gives_the_same_result()
    {
        var messy = $"<div onclick=\"x()\"><h1 style=\"color:red\">T</h1><img src=\"{Image}\" onerror=\"x()\"><a href=\"javascript:x\">l</a>"
            + "<table><tr><td>c</td></tr></table><ul><li>one<li>two</ul><p>unclosed <b>bold</div>";

        var once = _sanitizer.Sanitize(messy);

        Assert.Equal(once, _sanitizer.Sanitize(once));
        Assert.DoesNotContain("onclick", once);
        Assert.DoesNotContain("onerror", once);
        Assert.DoesNotContain("javascript", once);
        Assert.Contains(Image, once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_in_gives_nothing_out(string? html)
    {
        Assert.Equal(string.Empty, _sanitizer.Sanitize(html));
    }

    [Fact]
    public async Task Notes_are_sanitised_on_the_way_into_the_database_whoever_writes_them()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        var created = await app.Notes.CreateAsync(title: "Pasted", content: "<p>hello</p><script>steal()</script>");
        var updated = await app.Notes.UpdateAsync(created.Id, "Pasted", "<p onclick=\"x()\">hello again</p><img src=\"https://tracker.example/p.gif\">");

        Assert.Equal("<p>hello</p>", created.Content);
        Assert.Equal("<p>hello again</p>", updated.Content);
        Assert.Equal("<p>hello again</p>", (await app.Notes.GetAsync(created.Id))!.Content);
    }

    [Fact]
    public async Task Saving_content_that_only_differs_in_what_is_removed_is_not_a_change()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(data, clock);
        var note = await app.Notes.CreateAsync(title: "Stable", content: "<p>same</p>");

        clock.Advance(TimeSpan.FromHours(1));
        var saved = await app.Notes.UpdateAsync(note.Id, "Stable", "<p style=\"color:red\">same</p>");

        Assert.Equal(note.UpdatedAt, saved.UpdatedAt);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/a b", true)]
    [InlineData("mailto:a@example.com", false)]
    [InlineData("example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_absolute_web_addresses_count_as_links(string? address, bool expected)
    {
        Assert.Equal(expected, NoteContentRules.IsWebLink(address));
    }
}
