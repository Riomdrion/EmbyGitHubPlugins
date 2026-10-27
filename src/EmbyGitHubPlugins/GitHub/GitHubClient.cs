using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace EmbyGitHubPlugins.GitHub
{
    /// <summary>Reads releases from the GitHub REST API and downloads release assets.</summary>
    public class GitHubClient
    {
        private static readonly Regex RepoPattern = new Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$");

        private static readonly TimeSpan ReleaseCacheTime = TimeSpan.FromMinutes(5);

        private readonly HttpClient http;
        private readonly ILogger logger;
        private readonly ConcurrentDictionary<string, (DateTime Fetched, Release Release)> releaseCache = new ConcurrentDictionary<string, (DateTime, Release)>(StringComparer.OrdinalIgnoreCase);

        public GitHubClient(ILogger logger, HttpMessageHandler handler = null)
        {
            this.logger = logger;
            this.http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(60) };
            this.http.DefaultRequestHeaders.UserAgent.ParseAdd("EmbyGitHubPlugins");
        }

        /// <summary>Accepts "owner/repo" or a GitHub URL and returns "owner/repo", or null if it isn't one.</summary>
        public static string NormalizeRepository(string input)
        {
            var value = (input ?? string.Empty).Trim();
            foreach (var prefix in new[] { "https://github.com/", "http://github.com/", "github.com/" })
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(prefix.Length);
                }
            }

            var parts = value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return null;
            }

            var repo = parts[0] + "/" + (parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1].Substring(0, parts[1].Length - 4) : parts[1]);
            return RepoPattern.IsMatch(repo) ? repo : null;
        }

        /// <summary>The latest release. Cached for 5 minutes unless <paramref name="fresh"/> is set (installs always fetch fresh).</summary>
        public async Task<Release> GetLatestRelease(string repository, string token, CancellationToken cancellationToken, bool fresh = false)
        {
            if (!fresh && this.releaseCache.TryGetValue(repository, out var cached) && DateTime.UtcNow - cached.Fetched < ReleaseCacheTime)
            {
                return cached.Release;
            }

            using (var request = Request("https://api.github.com/repos/" + repository + "/releases/latest", "application/vnd.github+json", token))
            using (var response = await this.Send(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new GitHubException(repository + " has no published release (or the repository doesn't exist / is private).");
                }

                EnsureSuccess(response, repository);
                var release = Read<Release>(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
                release.Assets = release.Assets ?? new List<ReleaseAsset>();
                this.releaseCache[repository] = (DateTime.UtcNow, release);
                return release;
            }
        }

        public async Task<byte[]> Download(ReleaseAsset asset, string token, CancellationToken cancellationToken)
        {
            // Private repos need the API asset URL + token; public ones can use the plain download URL.
            var url = string.IsNullOrEmpty(token) ? asset.BrowserDownloadUrl : asset.ApiUrl;
            using (var request = Request(url, "application/octet-stream", token))
            using (var response = await this.Send(request, cancellationToken).ConfigureAwait(false))
            {
                EnsureSuccess(response, asset.Name);
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private static HttpRequestMessage Request(string url, string accept, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd(accept);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return request;
        }

        private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await this.http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                this.logger.Warn("GitHub request failed: {0}", ex.Message);
                throw new GitHubException("Could not reach GitHub (" + ex.Message + ").");
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response, string what)
        {
            var code = (int)response.StatusCode;
            if (code >= 200 && code < 300)
            {
                return;
            }

            if ((code == 403 || code == 429)
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            {
                throw new GitHubException("GitHub's rate limit (60 requests per hour without a token) was reached. Wait an hour or add a GitHub token.");
            }

            if (code == 401 || code == 403)
            {
                throw new GitHubException("GitHub refused access to " + what + " (HTTP " + code + "). Check the GitHub token.");
            }

            throw new GitHubException("GitHub returned HTTP " + code + " for " + what + ".");
        }

        private static T Read<T>(Stream stream)
        {
            try
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (Exception)
            {
                throw new GitHubException("Unexpected response from GitHub.");
            }
        }
    }

    public class GitHubException : Exception
    {
        public GitHubException(string message)
            : base(message)
        {
        }
    }

    [DataContract]
    public class Release
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "html_url")]
        public string HtmlUrl { get; set; }

        [DataMember(Name = "published_at")]
        public string PublishedAt { get; set; }

        [DataMember(Name = "assets")]
        public List<ReleaseAsset> Assets { get; set; }
    }

    [DataContract]
    public class ReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>API URL; with Accept: application/octet-stream it downloads the file (works for private repos).</summary>
        [DataMember(Name = "url")]
        public string ApiUrl { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        /// <summary>"sha256:&lt;hex&gt;", published by GitHub for release assets.</summary>
        [DataMember(Name = "digest")]
        public string Digest { get; set; }
    }
}
