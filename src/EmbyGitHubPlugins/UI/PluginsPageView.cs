using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using EmbyGitHubPlugins.GitHub;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;

namespace EmbyGitHubPlugins.UI
{
    internal class PluginsPageView : PluginPageView
    {
        private readonly Plugin plugin;

        // A token typed in the form but not saved yet. Kept server-side so the form only ever holds the mask.
        private string pendingToken;

        public PluginsPageView(PluginInfo pluginInfo, Plugin plugin)
            : base(pluginInfo.Id)
        {
            this.plugin = plugin;
            var config = plugin.Configuration;
            this.ContentData = new PluginsUI
            {
                GitHubToken = string.IsNullOrEmpty(config.GitHubToken) ? string.Empty : PluginsUI.SecretMask,
                AutoUpdate = config.AutoUpdate,
            };
        }

        private PluginsUI Ui => (PluginsUI)this.ContentData;

        /// <summary>Looks up every repository's latest release and rebuilds the list.</summary>
        public async Task Refresh()
        {
            var token = this.Token();
            var repos = this.plugin.Configuration.Repositories.ToList();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
            {
                var statuses = await Task.WhenAll(repos.Select(r => this.plugin.Installer.GetStatus(r, token, cts.Token))).ConfigureAwait(false);

                this.Ui.Repositories.Clear();
                foreach (var status in statuses)
                {
                    this.Ui.Repositories.Add(this.ToListItem(status));
                }
            }

            if (repos.Count == 0)
            {
                this.Ui.Repositories.Add(new GenericListItem(IconNames.info, "No repositories yet",
                    "Add one above, e.g. Riomdrion/EmbyRadarr. Add " + Plugin.OwnRepository + " to keep this plugin itself up to date."));
            }

            this.UpdateRestartButton();
        }

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            this.MaskTypedToken();
            try
            {
                if (commandId == PluginsUI.AddCommand)
                {
                    await this.Add().ConfigureAwait(false);
                }
                else if (commandId == PluginsUI.RefreshCommand)
                {
                    await this.Refresh().ConfigureAwait(false);
                    this.SetStatus(ItemStatus.Succeeded, "Checked " + this.plugin.Configuration.Repositories.Count + " repositories.");
                }
                else if (commandId == PluginsUI.RestartCommand)
                {
                    this.Restart();
                }
                else if (commandId != null && commandId.StartsWith(PluginsUI.InstallPrefix, StringComparison.Ordinal))
                {
                    await this.Install(commandId.Substring(PluginsUI.InstallPrefix.Length)).ConfigureAwait(false);
                }
                else if (commandId != null && commandId.StartsWith(PluginsUI.RemovePrefix, StringComparison.Ordinal))
                {
                    await this.Remove(commandId.Substring(PluginsUI.RemovePrefix.Length)).ConfigureAwait(false);
                }
                else
                {
                    return await base.RunCommand(itemId, commandId, data).ConfigureAwait(false);
                }
            }
            catch (GitHubException ex)
            {
                this.SetStatus(ItemStatus.Failed, ex.Message);
            }
            catch (OperationCanceledException)
            {
                this.SetStatus(ItemStatus.Failed, "GitHub did not answer in time.");
            }
            catch (Exception ex)
            {
                this.plugin.Logger.ErrorException("Unexpected error on the GitHub Plugins page", ex);
                this.SetStatus(ItemStatus.Failed, "Unexpected error: " + ex.Message);
            }

            return this;
        }

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            var config = this.plugin.Configuration;
            config.GitHubToken = this.Token();
            config.AutoUpdate = this.Ui.AutoUpdate;
            this.plugin.SaveConfiguration();

            this.pendingToken = null;
            this.Ui.GitHubToken = string.IsNullOrEmpty(config.GitHubToken) ? string.Empty : PluginsUI.SecretMask;
            return base.OnSaveCommand(itemId, commandId, data);
        }

        private async Task Add()
        {
            var repo = GitHubClient.NormalizeRepository(this.Ui.NewRepository);
            if (repo == null)
            {
                this.SetStatus(ItemStatus.Warning, "Enter a repository as owner/repo, e.g. Riomdrion/EmbyRadarr.");
                return;
            }

            var config = this.plugin.Configuration;
            if (config.Repositories.Any(r => string.Equals(r, repo, StringComparison.OrdinalIgnoreCase)))
            {
                this.SetStatus(ItemStatus.Warning, repo + " is already in the list.");
                return;
            }

            config.Repositories.Add(repo);
            this.plugin.SaveConfiguration();
            this.Ui.NewRepository = string.Empty;
            await this.Refresh().ConfigureAwait(false);
            this.SetStatus(ItemStatus.Succeeded, "Added " + repo + ". Use Install in the list below.");
        }

        private async Task Install(string repo)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                var message = await this.plugin.Installer.Install(repo, this.Token(), cts.Token).ConfigureAwait(false);
                this.plugin.AppHost.NotifyPendingRestart();
                await this.Refresh().ConfigureAwait(false);
                this.SetStatus(ItemStatus.Succeeded, message);
            }
        }

        private async Task Remove(string repo)
        {
            var config = this.plugin.Configuration;
            var message = "Removed " + repo + " from the list.";

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                var status = await this.plugin.Installer.GetStatus(repo, this.Token(), cts.Token).ConfigureAwait(false);
                var ownFile = Path.GetFileName(this.plugin.AssemblyFilePath ?? string.Empty);
                if (status.AssetName != null && !string.Equals(status.AssetName, ownFile, StringComparison.OrdinalIgnoreCase)
                    && status.State != RepoState.NotInstalled)
                {
                    message = this.plugin.Installer.Uninstall(status.AssetName);
                    this.plugin.AppHost.NotifyPendingRestart();
                }
            }

            config.Repositories.RemoveAll(r => string.Equals(r, repo, StringComparison.OrdinalIgnoreCase));
            this.plugin.SaveConfiguration();
            await this.Refresh().ConfigureAwait(false);
            this.SetStatus(ItemStatus.Succeeded, message);
        }

        private GenericListItem ToListItem(RepoStatus s)
        {
            var item = new GenericListItem
            {
                PrimaryText = s.PluginName ?? s.Repository,
                Icon = IconNames.extension,
                IconMode = ItemListIconMode.SmallRegular,
                HyperLink = s.ReleaseUrl,
                HyperLinkTargetExternal = true,
                Button2 = new ButtonItem("Remove")
                {
                    Icon = IconNames.delete,
                    Data1 = PluginsUI.RemovePrefix + s.Repository,
                    ConfirmationPrompt = "Remove " + s.Repository + " from the list" + (s.AssetName != null ? " and delete " + s.AssetName : string.Empty) + "?",
                },
            };

            var installed = s.InstalledVersion != null ? "installed " + s.InstalledVersion.ToString(3) : "not installed";
            switch (s.State)
            {
                case RepoState.NotInstalled:
                    item.Status = ItemStatus.Unavailable;
                    item.SecondaryText = s.Repository + " · latest " + s.Tag + " · not installed";
                    item.Button1 = new ButtonItem("Install") { Icon = IconNames.download, Data1 = PluginsUI.InstallPrefix + s.Repository };
                    break;
                case RepoState.UpdateAvailable:
                    item.Status = ItemStatus.Warning;
                    item.SecondaryText = s.Repository + " · " + installed + " · update " + s.Tag + " available";
                    item.Button1 = new ButtonItem("Update to " + s.Tag) { Icon = IconNames.system_update, Data1 = PluginsUI.InstallPrefix + s.Repository };
                    break;
                case RepoState.RestartNeeded:
                    item.Status = ItemStatus.InProgress;
                    item.SecondaryText = s.Repository + " · changed, restart Emby to activate";
                    break;
                case RepoState.UpToDate:
                    item.Status = ItemStatus.Succeeded;
                    item.SecondaryText = s.Repository + " · " + installed + " · up to date";
                    break;
                default:
                    item.Status = ItemStatus.Failed;
                    item.SecondaryText = s.Repository + " · " + s.Error;
                    break;
            }

            return item;
        }

        private void Restart()
        {
            this.Ui.RestartButton.IsEnabled = false;
            this.SetStatus(ItemStatus.InProgress, "Emby is restarting. Reload the page in a minute.");
            this.plugin.Logger.Info("Restart requested from the {0} page", Plugin.PluginName);

            // Let this response reach the browser before the server goes down.
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                this.plugin.AppHost.Restart();
            });
        }

        private void UpdateRestartButton()
            => this.Ui.RestartButton.IsVisible = this.plugin.Installer.HasPendingRestart && this.plugin.AppHost.CanSelfRestart;

        private void SetStatus(ItemStatus status, string text)
        {
            this.Ui.ActionStatus.Status = status;
            this.Ui.ActionStatus.StatusText = text;
            this.UpdateRestartButton();
        }

        private string Token()
        {
            var typed = this.Ui.GitHubToken?.Trim();
            return typed == PluginsUI.SecretMask ? (this.pendingToken ?? this.plugin.Configuration.GitHubToken) : (typed ?? string.Empty);
        }

        private void MaskTypedToken()
        {
            var typed = this.Ui.GitHubToken?.Trim();
            if (!string.IsNullOrEmpty(typed) && typed != PluginsUI.SecretMask)
            {
                this.pendingToken = typed;
                this.Ui.GitHubToken = PluginsUI.SecretMask;
            }
        }
    }
}
