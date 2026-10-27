using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;

namespace EmbyGitHubPlugins.UI
{
    internal class PluginsPageController : IPluginUIPageController
    {
        private readonly PluginInfo pluginInfo;
        private readonly Plugin plugin;

        public PluginsPageController(PluginInfo pluginInfo, Plugin plugin)
        {
            this.pluginInfo = pluginInfo;
            this.plugin = plugin;
            this.PageInfo = new PluginPageInfo
            {
                Name = "GitHubPlugins",
                DisplayName = "GitHub Plugins",
                EnableInMainMenu = true,
                MenuIcon = "extension",
                IsMainConfigPage = true,
            };
        }

        public PluginPageInfo PageInfo { get; }

        public string PluginId => this.pluginInfo.Id;

        public Task Initialize(CancellationToken token) => Task.CompletedTask;

        public async Task<IPluginUIView> CreateDefaultPageView()
        {
            var view = new PluginsPageView(this.pluginInfo, this.plugin);
            await view.Refresh().ConfigureAwait(false);
            return view;
        }
    }
}
