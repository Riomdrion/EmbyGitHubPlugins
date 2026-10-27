using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmbyGitHubPlugins.GitHub;
using MediaBrowser.Model.Tasks;

namespace EmbyGitHubPlugins.Tasks
{
    /// <summary>Daily check (Dashboard > Scheduled Tasks). Installs updates only when "Install updates automatically" is on.</summary>
    public class UpdatePluginsTask : IScheduledTask
    {
        public string Name => "Update GitHub plugins";

        public string Key => "EmbyGitHubPluginsUpdate";

        public string Description => "Checks the GitHub repositories from the GitHub Plugins page and installs new releases if enabled.";

        public string Category => Plugin.PluginName;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
            => new[] { new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks } };

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var plugin = Plugin.Instance;
            var config = plugin.Configuration;
            var repos = config.Repositories.ToList();
            var installed = 0;

            for (var i = 0; i < repos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = await plugin.Installer.GetStatus(repos[i], config.GitHubToken, cancellationToken).ConfigureAwait(false);

                if (status.State == RepoState.UpdateAvailable)
                {
                    if (!config.AutoUpdate)
                    {
                        plugin.Logger.Info("Update available for {0}: {1}", repos[i], status.Tag);
                    }
                    else
                    {
                        try
                        {
                            await plugin.Installer.Install(repos[i], config.GitHubToken, cancellationToken).ConfigureAwait(false);
                            installed++;
                        }
                        catch (GitHubException ex)
                        {
                            plugin.Logger.Warn("Updating {0} failed: {1}", repos[i], ex.Message);
                        }
                    }
                }
                else if (status.State == RepoState.Error)
                {
                    plugin.Logger.Warn("Checking {0} failed: {1}", repos[i], status.Error);
                }

                progress.Report(100.0 * (i + 1) / repos.Count);
            }

            if (installed > 0)
            {
                plugin.AppHost.NotifyPendingRestart();
            }
        }
    }
}
