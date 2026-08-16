using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using StratBoardImport.Localization;
using StratBoardImport.Windows;

namespace StratBoardImport;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/sbi";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    public Configuration Configuration { get; }
    public GameImporter Importer { get; } = new();
    public FolderImportJob FolderJob { get; } = new();
    internal static Plugin Instance { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("StratBoardImport");
    private readonly MainWindow mainWindow;
    private readonly CommandInfo commandInfo;

    public Plugin()
    {
        Instance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Loc.Initialize(PluginInterface, Configuration, ClientState, Log);

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        commandInfo = new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get(L.CommandHelp),
        };
        CommandManager.AddHandler(CommandName, commandInfo);

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;

        Log.Information(Loc.Get(L.LogLoaded));
    }

    internal static void ChatPrint(string message)
    {
        if (!Instance.Configuration.ShowChatMessages)
            return;
        ChatGui.Print($"[SBI] {message}");
    }

    internal static void ChatPrintError(string message)
    {
        if (!Instance.Configuration.ShowChatMessages)
            return;
        ChatGui.PrintError($"[SBI] {message}");
    }

    public void ReloadLanguage()
    {
        Loc.Reload();
        commandInfo.HelpMessage = Loc.Get(L.CommandHelp);
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        FolderJob.Cancel();
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        if (!string.IsNullOrWhiteSpace(args))
        {
            var parsed = ShareCodeParser.Parse(args);
            var first = parsed.FirstOrDefault(c => c.IsValid);
            if (first == null)
            {
                ChatPrintError(Loc.Get(L.CommandNoCode));
                ToggleMainUi();
                return;
            }

            var native = TofuImporter.ImportOne(first.Code);
            if (native.Success)
            {
                ChatPrint(native.Message);
                return;
            }

            var result = Importer.Import(first.Code);
            if (result.Success)
                ChatPrint(result.Message);
            else
                ChatPrintError(result.Message);
            return;
        }

        ToggleMainUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        FolderJob.Tick();
        TofuImporter.TickUiRefresh();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
}
