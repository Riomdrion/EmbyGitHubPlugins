using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace EmbyGitHubPlugins.GitHub
{
    /// <summary>Installs, updates and removes plugin DLLs taken from GitHub releases.</summary>
    public class PluginInstaller
    {
        private readonly GitHubClient github;
        private readonly string pluginsPath;
        private readonly Func<IEnumerable<InstalledPlugin>> loadedPlugins;
        private readonly ILogger logger;

        // Files written since Emby started: they only take effect after a restart.
        private readonly HashSet<string> pendingRestart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PluginInstaller(GitHubClient github, string pluginsPath, Func<IEnumerable<InstalledPlugin>> loadedPlugins, ILogger logger)
        {
            this.github = github;
            this.pluginsPath = pluginsPath;
            this.loadedPlugins = loadedPlugins;
            this.logger = logger;
        }

        public bool HasPendingRestart => this.pendingRestart.Count > 0;

        public async Task<RepoStatus> GetStatus(string repository, string token, CancellationToken cancellationToken)
        {
            var status = new RepoStatus { Repository = repository };
            try
            {
                var release = await this.github.GetLatestRelease(repository, token, cancellationToken).ConfigureAwait(false);
                var asset = PickAsset(repository, release);
                status.Tag = release.TagName;
                status.ReleaseUrl = release.HtmlUrl;
                status.AssetName = asset.Name;
                status.Latest = ParseVersion(release.TagName);

                var loaded = this.loadedPlugins().FirstOrDefault(p => string.Equals(p.FileName, asset.Name, StringComparison.OrdinalIgnoreCase));
                status.PluginName = loaded?.Name;
                status.InstalledVersion = loaded?.Version;
                var onDisk = File.Exists(Path.Combine(this.pluginsPath, asset.Name));

                if (this.pendingRestart.Contains(asset.Name) || (onDisk && loaded == null))
                {
                    status.State = RepoState.RestartNeeded;
                }
                else if (loaded == null)
                {
                    status.State = RepoState.NotInstalled;
                }
                else if (status.Latest != null && status.Latest > Normalize(loaded.Version))
                {
                    status.State = RepoState.UpdateAvailable;
                }
                else
                {
                    status.State = RepoState.UpToDate;
                }
            }
            catch (GitHubException ex)
            {
                status.State = RepoState.Error;
                status.Error = ex.Message;
            }

            return status;
        }

        /// <summary>Downloads the release DLL and puts it in Emby's plugins folder (keeping a .bak of an older copy).</summary>
        public async Task<string> Install(string repository, string token, CancellationToken cancellationToken)
        {
            var release = await this.github.GetLatestRelease(repository, token, cancellationToken, fresh: true).ConfigureAwait(false);
            var asset = PickAsset(repository, release);
            var bytes = await this.github.Download(asset, token, cancellationToken).ConfigureAwait(false);
            Verify(bytes, asset);

            var target = Path.Combine(this.pluginsPath, asset.Name);
            var temp = target + ".download";
            File.WriteAllBytes(temp, bytes);
            try
            {
                if (File.Exists(target))
                {
                    // Rename-based swap: on Linux the running process keeps its already-loaded copy.
                    File.Replace(temp, target, target + ".bak");
                }
                else
                {
                    File.Move(temp, target);
                }
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("Could not write {0}", ex, target);
                throw new GitHubException("Downloaded " + asset.Name + " but could not put it in the plugins folder (" + ex.Message + ").");
            }

            this.pendingRestart.Add(asset.Name);
            this.logger.Info("Installed {0} {1} from {2} ({3} bytes)", asset.Name, release.TagName, repository, bytes.Length);
            return "Installed " + asset.Name + " " + release.TagName + ". Restart Emby to activate it.";
        }

        /// <summary>Deletes a plugin DLL. Emby unloads it on the next restart.</summary>
        public string Uninstall(string assetName)
        {
            if (!IsPlainDllName(assetName))
            {
                throw new GitHubException("Not a plugin file name: " + assetName);
            }

            var target = Path.Combine(this.pluginsPath, assetName);
            if (!File.Exists(target))
            {
                return assetName + " is not installed.";
            }

            File.Delete(target);
            this.pendingRestart.Add(assetName);
            this.logger.Info("Removed {0}", assetName);
            return "Removed " + assetName + ". Restart Emby to unload it.";
        }

        /// <summary>The single .dll attached to the release; with several, the one named after the repository.</summary>
        public static ReleaseAsset PickAsset(string repository, Release release)
        {
            var dlls = release.Assets.Where(a => IsPlainDllName(a.Name)).ToList();
            if (dlls.Count == 1)
            {
                return dlls[0];
            }

            if (dlls.Count == 0)
            {
                throw new GitHubException("Release " + release.TagName + " of " + repository + " has no .dll attached.");
            }

            var repoName = repository.Substring(repository.IndexOf('/') + 1);
            var match = dlls.FirstOrDefault(a => string.Equals(Path.GetFileNameWithoutExtension(a.Name), repoName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }

            throw new GitHubException("Release " + release.TagName + " has several DLLs (" + string.Join(", ", dlls.Select(d => d.Name)) + ") and none is named " + repoName + ".dll.");
        }

        public static Version ParseVersion(string tag)
            => Version.TryParse((tag ?? string.Empty).TrimStart('v', 'V'), out var v) ? Normalize(v) : null;

        private static Version Normalize(Version v)
            => v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        /// <summary>Only "Name.dll": no folders, so a release can't write outside the plugins folder.</summary>
        private static bool IsPlainDllName(string name)
            => !string.IsNullOrEmpty(name)
               && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0
               && !name.StartsWith(".", StringComparison.Ordinal);

        private static void Verify(byte[] bytes, ReleaseAsset asset)
        {
            if (bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                throw new GitHubException(asset.Name + " is not a valid DLL. Nothing was changed.");
            }

            if (asset.Size > 0 && bytes.Length != asset.Size)
            {
                throw new GitHubException("The download of " + asset.Name + " is incomplete. Nothing was changed.");
            }

            if (asset.Digest != null && asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using (var sha = SHA256.Create())
                {
                    var actual = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
                    if (!string.Equals(actual, asset.Digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new GitHubException("The checksum of " + asset.Name + " does not match GitHub's. Nothing was changed.");
                    }
                }
            }
        }
    }

    public class InstalledPlugin
    {
        public string FileName { get; set; }

        public string Name { get; set; }

        public Version Version { get; set; }
    }

    public enum RepoState
    {
        NotInstalled,
        UpToDate,
        UpdateAvailable,
        RestartNeeded,
        Error,
    }

    public class RepoStatus
    {
        public string Repository { get; set; }

        public RepoState State { get; set; }

        public string Tag { get; set; }

        public Version Latest { get; set; }

        public string AssetName { get; set; }

        public string PluginName { get; set; }

        public Version InstalledVersion { get; set; }

        public string ReleaseUrl { get; set; }

        public string Error { get; set; }
    }
}
