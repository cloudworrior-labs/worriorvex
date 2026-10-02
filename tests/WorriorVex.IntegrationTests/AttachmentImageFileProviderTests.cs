using WorriorVex.Application.Content;
using WorriorVex.Application.Storage;
using WorriorVex.Infrastructure.Attachments;

namespace WorriorVex.IntegrationTests;

public class AttachmentImageFileProviderTests
{
    [Fact]
    public async Task The_window_can_load_an_attached_image_and_nothing_else_from_the_data_folder()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var note = await app.Notes.CreateAsync(title: "With picture");
        var image = await app.Attachments.AddAsync(note.Id, "photo.PNG", "image/png", new MemoryStream([1, 2, 3, 4]));
        var document = await app.Attachments.AddAsync(note.Id, "report.pdf", "application/pdf", new MemoryStream([5, 6]));
        var directory = app.Get<IApplicationDataPathProvider>().AttachmentsDirectory;
        var provider = new AttachmentImageFileProvider(directory);

        var source = NoteContentRules.AttachmentSource(image.StoredFileName);
        Assert.True(NoteContentRules.IsAttachmentImageSource(source));

        var served = provider.GetFileInfo(source);
        Assert.True(served.Exists);
        Assert.Equal(4, served.Length);
        Assert.True(provider.GetFileInfo("/" + source).Exists);

        Assert.False(provider.GetFileInfo(NoteContentRules.AttachmentSource(document.StoredFileName)).Exists);
        Assert.False(provider.GetFileInfo("attachments/../worriorvex.db").Exists);
        Assert.False(provider.GetFileInfo("worriorvex.db").Exists);
        Assert.False(provider.GetFileInfo("attachments/" + new string('0', 32) + ".png").Exists);
        Assert.False(provider.GetFileInfo(image.StoredFileName).Exists);
        Assert.False(provider.GetDirectoryContents("attachments").Exists);
    }

    [Theory]
    [InlineData("photo.png", "image/png", "photo.png")]
    [InlineData("photo.PNG", "image/png", "photo.PNG")]
    [InlineData("image", "image/png", "image.png")]
    [InlineData("", "image/jpeg", "image.jpg")]
    [InlineData("shot.png", "image/webp", "shot.png.webp")]
    [InlineData("pic.jpeg", "image/jpeg", "pic.jpeg")]
    public void An_image_gets_a_file_name_that_matches_what_it_is(string name, string contentType, string expected)
    {
        Assert.Equal(expected, NoteContentRules.ImageFileName(name, contentType));
    }

    [Theory]
    [InlineData("drawing.svg", "image/svg+xml")]
    [InlineData("page.html", "text/html")]
    [InlineData("photo.png", null)]
    public void What_is_not_an_accepted_image_type_gets_no_name(string name, string? contentType)
    {
        Assert.Null(NoteContentRules.ImageFileName(name, contentType));
    }
}
