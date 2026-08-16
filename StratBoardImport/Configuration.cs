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

    /// <summary>
    /// Translation culture, or "auto" to follow the game client language.
    /// </summary>
    public string Language { get; set; } = Localization.Loc.Auto;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
