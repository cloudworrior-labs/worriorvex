using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WorriorVex.Infrastructure.Updates;

namespace WorriorVex.IntegrationTests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("0.2.0", "0.3.0", true)]
    [InlineData("0.2.0", "1.0.0", true)]
    [InlineData("0.2.0", "0.2.1", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.3.0", "0.2.9", false)]
    [InlineData("0.2.0+abc1234", "0.2.0", false)]
    [InlineData("0.2.0", "0.2.0-beta", false)]
    [InlineData("0.2.0", "nonsense", false)]
    public void Newer_means_a_higher_version_number(string current, string latest, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateChecker.IsNewer(current, latest));
    }

    [Fact]
    public async Task The_release_page_answer_is_read_and_a_failure_is_just_null()
    {
        var ok = new GitHubUpdateChecker(NullLogger<GitHubUpdateChecker>.Instance, new StubHandler(HttpStatusCode.OK,
            "{\"tag_name\":\"v9.9.9\",\"html_url\":\"https://github.com/cloudworrior-labs/worriorvex/releases/tag/v9.9.9\"}"));
        var result = await ok.CheckAsync();
        Assert.NotNull(result);
        Assert.Equal("9.9.9", result.LatestVersion);
        Assert.True(result.IsNewer);
        Assert.Equal("https://github.com/cloudworrior-labs/worriorvex/releases/tag/v9.9.9", result.DownloadPage.ToString());

        var down = new GitHubUpdateChecker(NullLogger<GitHubUpdateChecker>.Instance, new StubHandler(HttpStatusCode.ServiceUnavailable, ""));
        Assert.Null(await down.CheckAsync());

        var garbage = new GitHubUpdateChecker(NullLogger<GitHubUpdateChecker>.Instance, new StubHandler(HttpStatusCode.OK, "not json"));
        Assert.Null(await garbage.CheckAsync());

        var insecure = new GitHubUpdateChecker(NullLogger<GitHubUpdateChecker>.Instance, new StubHandler(HttpStatusCode.OK,
            "{\"tag_name\":\"v9.9.9\",\"html_url\":\"http://evil.example/\"}"));
        Assert.Equal(GitHubUpdateChecker.ReleasesPage, (await insecure.CheckAsync())!.DownloadPage);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(GitHubUpdateChecker.LatestReleaseApi, request.RequestUri);
            Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "WorriorVex");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
