namespace WorriorVex.Application.Updates;

/// <param name="IsNewer">The published version is newer than the one running.</param>
public sealed record UpdateCheck(string CurrentVersion, string LatestVersion, Uri DownloadPage, bool IsNewer);

/// <summary>
/// Asks the project's release page for the newest version. This is the only thing in WorriorVex that
/// talks to the network, and it runs only when the user has switched it on.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Returns <c>null</c> when the check could not be made (offline, or the page did not answer).</summary>
    Task<UpdateCheck?> CheckAsync(CancellationToken cancellationToken = default);
}
