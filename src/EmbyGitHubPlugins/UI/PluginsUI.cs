using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.Attributes;

namespace EmbyGitHubPlugins.UI
{
    /// <summary>The page. Emby renders it from these properties; no HTML/JS needed.</summary>
    public class PluginsUI : EditableOptionsBase
    {
        public const string AddCommand = "add";
        public const string RefreshCommand = "refresh";
        public const string RestartCommand = "restart";
        public const string InstallPrefix = "install|";
        public const string RemovePrefix = "remove|";

        /// <summary>Shown instead of a saved token so it is never sent to the browser.</summary>
        public const string SecretMask = "********";

        public override string EditorTitle => "GitHub Plugins";

        public override string EditorDescription => "Install and update Emby plugins that publish their DLL on GitHub releases.\n"
            + "A plugin runs with all of Emby's rights: only add repositories you trust.";

        public CaptionItem AddCaption { get; set; } = new CaptionItem("Add a plugin");

        [DisplayName("GitHub repository")]
        [Description("owner/repo or a github.com link, e.g. Riomdrion/EmbyRadarr")]
        public string NewRepository { get; set; }

        public ButtonItem AddButton { get; set; } = new ButtonItem("Add") { Icon = IconNames.add, Data1 = AddCommand };

        public StatusItem ActionStatus { get; set; } = new StatusItem("Status", "Ready", ItemStatus.None);

        public SpacerItem Spacer1 { get; set; } = new SpacerItem();

        public CaptionItem ListCaption { get; set; } = new CaptionItem("Plugins");

        [DisplayName("Repositories")]
        public GenericItemList Repositories { get; set; } = new GenericItemList();

        public ButtonItem RefreshButton { get; set; } = new ButtonItem("Check for updates") { Icon = IconNames.refresh, Data1 = RefreshCommand };

        public ButtonItem RestartButton { get; set; } = new ButtonItem("Restart Emby now")
        {
            Icon = IconNames.restart_alt,
            Data1 = RestartCommand,
            IsVisible = false,
            ConfirmationPrompt = "Restart Emby now? Anything that is playing will stop.",
        };

        public SpacerItem Spacer2 { get; set; } = new SpacerItem();

        public CaptionItem OptionsCaption { get; set; } = new CaptionItem("Options");

        [DisplayName("GitHub token (optional)")]
        [Description("Raises GitHub's limit of 60 requests per hour and allows private repositories. A fine-grained token with read-only 'Contents' access is enough.")]
        [IsPassword]
        public string GitHubToken { get; set; }

        [DisplayName("Install updates automatically")]
        [Description("The daily task 'Update GitHub plugins' installs new releases. Emby still needs a restart to load them.")]
        public bool AutoUpdate { get; set; }
    }
}
