using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmbyGitHubPlugins.Configuration;
using EmbyGitHubPlugins.GitHub;
using EmbyGitHubPlugins.UI;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Serialization;

namespace EmbyGitHubPlugins
{
    /// <summary>
    /// Installs and updates Emby plugins straight from GitHub releases. Admin-only: a plugin DLL runs with
    /// all of Emby's rights, so only add repositories you trust.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasUIPages
    {
        public const string PluginName = "GitHub Plugins";
        public const string OwnRepository = "Riomdrion/EmbyGitHubPlugins";

        private List<IPluginUIPageController> pages;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logManager, IApplicationHost applicationHost)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            this.Logger = logManager.GetLogger(PluginName);
            this.AppHost = applicationHost;
            this.Installer = new PluginInstaller(
                new GitHubClient(this.Logger),
                applicationPaths.PluginsPath,
                () => applicationHost.Plugins.Select(p => new InstalledPlugin
                {
                    FileName = Path.GetFileName(p.AssemblyFilePath ?? string.Empty),
                    Name = p.Name,
                    Version = p.Version,
                }),
                this.Logger);
        }

        public static Plugin Instance { get; private set; }

        public override string Name => PluginName;

        public override string Description => "Install and update Emby plugins from GitHub releases.";

        public override Guid Id => new Guid("6b79b8fd-223e-4db3-876a-267811dabf75");

        public ILogger Logger { get; }

        public IApplicationHost AppHost { get; }

        public PluginInstaller Installer { get; }

        public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
        {
            get
            {
                if (this.pages == null)
                {
                    this.pages = new List<IPluginUIPageController> { new PluginsPageController(this.GetPluginInfo(), this) };
                }

                return this.pages.AsReadOnly();
            }
        }
    }
}
