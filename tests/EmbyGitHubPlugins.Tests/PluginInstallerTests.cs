using System.Net;
using System.Security.Cryptography;
using System.Text;
using EmbyGitHubPlugins.GitHub;

namespace EmbyGitHubPlugins.Tests;

public class PluginInstallerTests : IDisposable
{
    private static readonly byte[] Dll = "MZ plugin v2"u8.ToArray();

    private readonly FakeRadarr github = new(); // generic "METHOD /path" fake, reused for GitHub
    private readonly string dir = Directory.CreateTempSubdirectory("ghplugins-").FullName;
    private readonly List<InstalledPlugin> loaded = new();
    private readonly PluginInstaller installer;

    public PluginInstallerTests()
    {
        this.installer = new PluginInstaller(new GitHubClient(new RecordingLogger(), this.github), this.dir, () => this.loaded, new RecordingLogger());
    }

    public void Dispose() => Directory.Delete(this.dir, true);

    private void Publish(string repo, string tag, params (string Name, byte[] Bytes)[] assets)
    {
        var json = string.Join(",", assets.Select((a, i) =>
            $$"""{"name":"{{a.Name}}","size":{{a.Bytes.Length}},"digest":"sha256:{{Convert.ToHexString(SHA256.HashData(a.Bytes)).ToLowerInvariant()}}","url":"https://api.github.com/repos/{{repo}}/releases/assets/{{i}}","browser_download_url":"https://github.com/{{repo}}/releases/download/{{tag}}/{{a.Name}}"}"""));
        this.github.On($"GET /repos/{repo}/releases/latest", $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/{{repo}}/releases/tag/{{tag}}","assets":[{{json}}]}""");
        foreach (var a in assets)
        {
            this.github.On($"GET /{repo}/releases/download/{tag}/{a.Name}", Encoding.Latin1.GetString(a.Bytes));
        }
    }

    [Theory]
    [InlineData("Riomdrion/EmbyRadarr", "Riomdrion/EmbyRadarr")]
    [InlineData(" https://github.com/Riomdrion/EmbyRadarr/releases ", "Riomdrion/EmbyRadarr")]
    [InlineData("github.com/Riomdrion/EmbyRadarr.git", "Riomdrion/EmbyRadarr")]
    [InlineData("EmbyRadarr", null)]
    [InlineData("../../etc/passwd", null)]
    [InlineData("owner/repo name", null)]
    public void Normalizes_repository_input(string input, string? expected)
        => Assert.Equal(expected, GitHubClient.NormalizeRepository(input));

    [Fact]
    public async Task Not_installed_then_install_then_restart_needed()
    {
        this.Publish("me/Cool", "v1.2.0", ("Cool.dll", Dll));

        Assert.Equal(RepoState.NotInstalled, (await this.installer.GetStatus("me/Cool", null, default)).State);

        var message = await this.installer.Install("me/Cool", null, default);

        Assert.Equal(Dll, File.ReadAllBytes(Path.Combine(this.dir, "Cool.dll")));
        Assert.Contains("Restart Emby", message);
        Assert.True(this.installer.HasPendingRestart);
        Assert.Equal(RepoState.RestartNeeded, (await this.installer.GetStatus("me/Cool", null, default)).State);
    }

    [Fact]
    public async Task Detects_update_and_replaces_file_keeping_backup()
    {
        this.Publish("me/Cool", "v1.3.0", ("Cool.dll", Dll));
        File.WriteAllText(Path.Combine(this.dir, "Cool.dll"), "MZ old");
        this.loaded.Add(new InstalledPlugin { FileName = "Cool.dll", Name = "Cool Plugin", Version = new Version(1, 2, 0, 0) });

        var status = await this.installer.GetStatus("me/Cool", null, default);
        Assert.Equal(RepoState.UpdateAvailable, status.State);
        Assert.Equal("Cool Plugin", status.PluginName);

        await this.installer.Install("me/Cool", null, default);

        Assert.Equal(Dll, File.ReadAllBytes(Path.Combine(this.dir, "Cool.dll")));
        Assert.Equal("MZ old", File.ReadAllText(Path.Combine(this.dir, "Cool.dll.bak")));
    }

    [Fact]
    public async Task Same_version_is_up_to_date()
    {
        this.Publish("me/Cool", "v1.2.0", ("Cool.dll", Dll));
        File.WriteAllText(Path.Combine(this.dir, "Cool.dll"), "MZ");
        this.loaded.Add(new InstalledPlugin { FileName = "cool.DLL", Name = "Cool", Version = new Version(1, 2, 0, 0) });

        Assert.Equal(RepoState.UpToDate, (await this.installer.GetStatus("me/Cool", null, default)).State);
    }

    [Fact]
    public async Task Missing_release_is_reported_per_repository_not_thrown()
    {
        var status = await this.installer.GetStatus("me/Nothing", null, default);

        Assert.Equal(RepoState.Error, status.State);
        Assert.Contains("no published release", status.Error);
    }

    [Fact]
    public void Picks_the_dll_named_after_the_repo_when_there_are_several()
    {
        var release = new Release
        {
            TagName = "v1",
            Assets = new() { new() { Name = "Helper.dll" }, new() { Name = "Cool.dll" }, new() { Name = "Cool.zip" } },
        };

        Assert.Equal("Cool.dll", PluginInstaller.PickAsset("me/Cool", release).Name);
        Assert.Throws<GitHubException>(() => PluginInstaller.PickAsset("me/Other", release));
        Assert.Throws<GitHubException>(() => PluginInstaller.PickAsset("me/Cool", new Release { TagName = "v1", Assets = new() { new() { Name = "Cool.zip" } } }));
    }

    [Fact]
    public async Task Asset_names_with_paths_are_never_used()
    {
        this.Publish("me/Evil", "v1", ("../Evil.dll", Dll));

        var status = await this.installer.GetStatus("me/Evil", null, default);

        Assert.Equal(RepoState.Error, status.State);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(this.dir)!, "Evil.dll")));
    }

    [Fact]
    public async Task Tampered_download_changes_nothing()
    {
        this.Publish("me/Cool", "v1.2.0", ("Cool.dll", Dll));
        this.github.On("GET /me/Cool/releases/download/v1.2.0/Cool.dll", "MZ tampered!");

        await Assert.ThrowsAsync<GitHubException>(() => this.installer.Install("me/Cool", null, default));

        Assert.False(File.Exists(Path.Combine(this.dir, "Cool.dll")));
    }

    [Fact]
    public void Uninstall_deletes_only_plain_dll_names()
    {
        File.WriteAllText(Path.Combine(this.dir, "Cool.dll"), "MZ");

        Assert.StartsWith("Removed Cool.dll", this.installer.Uninstall("Cool.dll"));
        Assert.False(File.Exists(Path.Combine(this.dir, "Cool.dll")));
        Assert.Throws<GitHubException>(() => this.installer.Uninstall("../Cool.dll"));
    }

    [Fact]
    public async Task Rate_limit_gets_a_clear_message()
    {
        var limited = new RateLimitedGitHub();
        var installer = new PluginInstaller(new GitHubClient(new RecordingLogger(), limited), this.dir, () => this.loaded, new RecordingLogger());

        var status = await installer.GetStatus("me/Cool", null, default);

        Assert.Contains("rate limit", status.Error);
    }

    [Fact]
    public async Task Release_lookups_are_cached_but_installs_fetch_fresh()
    {
        this.Publish("me/Cool", "v1.2.0", ("Cool.dll", Dll));

        await this.installer.GetStatus("me/Cool", null, default);
        await this.installer.GetStatus("me/Cool", null, default);
        Assert.Single(this.github.Requests, r => r.Request.RequestUri!.AbsolutePath.EndsWith("/latest"));

        await this.installer.Install("me/Cool", null, default);
        Assert.Equal(2, this.github.Requests.Count(r => r.Request.RequestUri!.AbsolutePath.EndsWith("/latest")));
    }

    private sealed class RateLimitedGitHub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return Task.FromResult(response);
        }
    }
}
