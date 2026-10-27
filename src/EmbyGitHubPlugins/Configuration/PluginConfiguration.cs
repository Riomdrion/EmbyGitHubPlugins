using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace EmbyGitHubPlugins.Configuration
{
    /// <summary>Persisted settings (written by Emby to plugins/configurations/EmbyGitHubPlugins.xml).</summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>"owner/repo" entries.</summary>
        public List<string> Repositories { get; set; } = new List<string>();

        /// <summary>Optional. Raises GitHub's rate limit and gives access to private repositories. Never logged.</summary>
        public string GitHubToken { get; set; } = string.Empty;

        /// <summary>Let the daily scheduled task install updates (a restart is still needed to activate them).</summary>
        public bool AutoUpdate { get; set; }
    }
}
