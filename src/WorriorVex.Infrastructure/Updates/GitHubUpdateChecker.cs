using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Updates;

namespace WorriorVex.Infrastructure.Updates;

/// <summary>Reads the latest release from GitHub. Sends nothing but the request itself.</summary>
public sealed class GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger, HttpMessageHandler? handler = null) : IUpdateChecker
{
    public static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/cloudworrior-labs/worriorvex/releases/latest");
    public static readonly Uri ReleasesPage = new("https://github.com/cloudworrior-labs/worriorvex/releases/latest");

    private readonly HttpClient _http = CreateClient(handler);

    public async Task<UpdateCheck?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion();
        try
        {
            using var response = await _http.GetAsync(LatestReleaseApi, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Update check: the release page answered {Status}", (int)response.StatusCode);
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var tag = json.RootElement.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            var page = json.RootElement.TryGetProperty("html_url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps
                ? parsed
                : ReleasesPage;
            if (tag is null)
            {
                return null;
            }

            var latest = tag.TrimStart('v', 'V');
            var newer = IsNewer(current, latest);
            logger.LogInformation("Update check: this is {Current}, the newest release is {Latest}{Newer}", current, latest, newer ? " (newer)" : string.Empty);
            return new UpdateCheck(current, latest, page, newer);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogInformation(ex, "Update check did not complete");
            return null;
        }
    }

    /// <summary>Compares "major.minor.patch" numbers; anything after a dash or plus is ignored.</summary>
    public static bool IsNewer(string current, string latest)
    {
        static Version Parse(string text)
        {
            var core = text.Split('-', '+')[0];
            return Version.TryParse(core.Count(c => c == '.') == 0 ? core + ".0" : core, out var version) ? version : new Version(0, 0);
        }

        return Parse(latest) > Parse(current);
    }

    private static string CurrentVersion()
    {
        var informational = typeof(GitHubUpdateChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return informational.Split('+')[0];
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WorriorVex", CurrentVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
