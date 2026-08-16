using Dalamud.Configuration;

namespace StratBoardImport;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// After filling the share-code field, fire the addon's confirm callback.
    /// </summary>
    public bool AutoConfirm { get; set; }

    public int ConfirmCallbackId { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
