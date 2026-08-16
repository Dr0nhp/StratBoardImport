using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
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

    public Configuration Configuration { get; }
    public GameImporter Importer { get; } = new();
    public FolderImportJob FolderJob { get; } = new();
    public List<string> LastAddonScan { get; set; } = [];
    internal static Plugin Instance { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("StratBoardImport");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Instance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Strategy Board importer for long share codes.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;

        Log.Information("Strategy Board Import loaded. Command: /sbi");
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
                ChatGui.PrintError("[SBI] No share code in the command arguments.");
                ToggleMainUi();
                return;
            }

            var result = Importer.Import(first.Code, Configuration.AutoConfirm, Configuration.ConfirmCallbackId);
            if (result.Success)
                ChatGui.Print($"[SBI] {result.Message}");
            else
                ChatGui.PrintError($"[SBI] {result.Message}");
            return;
        }

        ToggleMainUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        FolderJob.Tick();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
}
