using Dalamud.Configuration;

namespace StratBoardImport;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Unused. Kept so older configs still load.
    /// </summary>
    public bool AutoConfirm { get; set; } = true;

    /// <summary>
    /// Unused. Kept so older configs still load.
    /// </summary>
    public int ConfirmCallbackId { get; set; }

    /// <summary>
    /// Translation culture, or "auto" to follow the game client language.
    /// </summary>
    public string Language { get; set; } = Localization.Loc.Auto;

    /// <summary>
    /// Print [SBI] status lines in the game chat. The plugin window still shows them.
    /// </summary>
    public bool ShowChatMessages { get; set; } = true;

    /// <summary>
    /// Write import/delete details to the Dalamud log and an in-plugin copy buffer.
    /// </summary>
    public bool DebugLog { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
