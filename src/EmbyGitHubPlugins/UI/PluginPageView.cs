using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;

namespace EmbyGitHubPlugins.UI
{
    /// <summary>Trimmed-down copy of the page view base class from the Emby SDK's EmbyPluginUiTemplate.</summary>
    internal abstract class PluginPageView : IPluginPageView
    {
        protected PluginPageView(string pluginId)
        {
            this.PluginId = pluginId;
        }

        public event EventHandler<GenericEventArgs<IPluginUIView>> UIViewInfoChanged;

        public virtual string Caption => this.ContentData.EditorTitle;

        public virtual string SubCaption => this.ContentData.EditorDescription;

        public string PluginId { get; }

        public IEditableObject ContentData { get; set; }

        public UserDto User { get; set; }

        public string RedirectViewUrl { get; set; }

        public bool ShowSave { get; set; } = true;

        public bool ShowBack { get; set; } = false;

        public bool AllowSave { get; set; } = true;

        public bool AllowBack { get; set; } = true;

        public virtual bool IsCommandAllowed(string commandKey) => true;

        public virtual Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
            => Task.FromResult<IPluginUIView>(null);

        public virtual Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
            => Task.FromResult<IPluginUIView>(this);

        public virtual Task Cancel() => Task.CompletedTask;

        public virtual void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
        }

        protected void RaiseUIViewInfoChanged()
            => this.UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
    }
}
