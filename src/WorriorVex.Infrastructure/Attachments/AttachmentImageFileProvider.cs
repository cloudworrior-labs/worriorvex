using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;
using WorriorVex.Application.Content;

namespace WorriorVex.Infrastructure.Attachments;

/// <summary>
/// Lets the app window load the images of notes from the attachments folder, under
/// <c>attachments/&lt;stored name&gt;</c>. It serves stored image files and nothing else:
/// no other attachment, no other folder, no directory listing.
/// </summary>
public sealed class AttachmentImageFileProvider(string attachmentsDirectory) : IFileProvider
{
    private readonly string _root = Path.GetFullPath(attachmentsDirectory);

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = (subpath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (!NoteContentRules.IsAttachmentImageSource(path))
        {
            return new NotFoundFileInfo(subpath ?? string.Empty);
        }

        var file = new FileInfo(Path.Combine(_root, path[NoteContentRules.AttachmentPathPrefix.Length..]));
        return file.Exists && file.DirectoryName == _root ? new PhysicalFileInfo(file) : new NotFoundFileInfo(subpath!);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}
