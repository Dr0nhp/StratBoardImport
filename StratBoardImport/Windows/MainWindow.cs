using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using StratBoardImport.Localization;

namespace StratBoardImport.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string input = string.Empty;
    private IReadOnlyList<ParsedShareCode> parsed = [];
    private string status;
    private bool statusError;
    private int selectedIndex;
    private string folderName = string.Empty;
    private string lastJobStatus = string.Empty;
    private bool confirmDeleteSaved;
    private bool focusSettingsTab;

    public MainWindow(Plugin plugin)
        : base("Strategy Board Import###StratBoardImport")
    {
        this.plugin = plugin;
        status = Loc.Get(L.StatusPasteHint);
        RespectCloseHotkey = true;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public void OpenSettings()
    {
        IsOpen = true;
        focusSettingsTab = true;
    }

    public override void PreDraw()
    {
        WindowName = Loc.Get(L.WindowTitle) + "###StratBoardImport";
    }

    public override void Draw()
    {
        SyncFolderJobStatus();

        using var tabBar = ImRaii.TabBar("##sbi-tabs");
        if (!tabBar.Success)
            return;

        using (var tab = ImRaii.TabItem(Loc.Get(L.UiTabImport)))
        {
            if (tab.Success)
                DrawImportTab();
        }

        var settingsFlags = focusSettingsTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        focusSettingsTab = false;
        if (ImGui.BeginTabItem(Loc.Get(L.UiTabSettings), settingsFlags))
        {
            DrawSettings();
            ImGui.EndTabItem();
        }
    }

    private void DrawImportTab()
    {
        ImGui.TextDisabled(Loc.Get(L.UiHeader));
        ImGuiHelpers.ScaledDummy(4);

        if (ImGui.Button(Loc.Get(L.UiFromClipboard)))
        {
            input = ImGui.GetClipboardText() ?? string.Empty;
            ParseInput();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get(L.UiCheck)))
            ParseInput();

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get(L.UiClear)))
        {
            input = string.Empty;
            parsed = [];
            selectedIndex = 0;
            folderName = string.Empty;
            confirmDeleteSaved = false;
            SetStatus(Loc.Get(L.StatusCleared), false);
        }

        ImGuiHelpers.ScaledDummy(4);
        ImGui.InputTextMultiline("##sharecode", ref input, 4_000_000,
            new Vector2(-1, 110 * ImGuiHelpers.GlobalScale));

        DrawParsedList();
        ImGuiHelpers.ScaledDummy(6);
        DrawPrimaryActions();
        ImGuiHelpers.ScaledDummy(8);
        DrawStatus();
        ImGuiHelpers.ScaledDummy(6);
        ImGui.Separator();
        DrawSavedListRow();
    }

    private void DrawParsedList()
    {
        ImGui.Text(Loc.Format(L.UiFoundCodes, parsed.Count, input.Length));
        using var child = ImRaii.Child("parsed-codes", new Vector2(-1, 140 * ImGuiHelpers.GlobalScale), true);
        if (!child.Success)
            return;

        for (var i = 0; i < parsed.Count; i++)
        {
            var code = parsed[i];
            var name = string.IsNullOrEmpty(code.Name) ? Loc.Get(L.UiUnnamed) : code.Name;
            var label = string.IsNullOrEmpty(code.Error)
                ? Loc.Format(L.UiCodeRow, i + 1, name, code.Length, code.ObjectCount)
                : Loc.Format(L.UiCodeRowError, i + 1, name, code.Length, code.Error);

            if (ImGui.Selectable(label, selectedIndex == i))
                selectedIndex = i;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(code.Code.Length > 180 ? code.Code[..180] + "…" : code.Code);
        }
    }

    private void DrawPrimaryActions()
    {
        var job = plugin.FolderJob;
        var validCount = parsed.Count(c => c.IsValid);
        var canImportSelected = selectedIndex >= 0 && selectedIndex < parsed.Count && parsed[selectedIndex].IsValid;

        if (job.IsRunning)
        {
            if (ImGui.Button(Loc.Get(L.UiCancelFolderImport)))
            {
                job.Cancel();
                SetStatus(job.Status, false);
            }

            if (job.TotalCount > 0)
                ImGui.ProgressBar(job.ProgressFraction, new Vector2(-1, 0), job.ProgressLabel);
            return;
        }

        if (validCount >= 2)
        {
            ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
            ImGui.InputText(Loc.Get(L.UiFolderName), ref folderName, 64);
            Tooltip(L.UiFolderImportHelp, FolderImportJob.MaxSavedBoards, FolderImportJob.MaxBoardsPerFolder);
        }

        using (ImRaii.Disabled(validCount == 0))
        {
            var primary = validCount >= 2
                ? Loc.Format(L.UiImportAllN, validCount)
                : Loc.Get(L.UiImport);
            if (ImGui.Button(primary))
                RunPrimaryImport(validCount);
        }

        if (validCount >= 2)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!canImportSelected))
            {
                if (ImGui.Button(Loc.Get(L.UiImportSelected)))
                    ImportOne(parsed[selectedIndex].Code);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!canImportSelected))
        {
            if (ImGui.Button(Loc.Get(L.UiCopySelected)))
            {
                ImGui.SetClipboardText(parsed[selectedIndex].Code);
                SetStatus(Loc.Get(L.StatusCopiedOne), false);
            }
        }
    }

    private void RunPrimaryImport(int validCount)
    {
        if (validCount <= 1)
        {
            var only = parsed.FirstOrDefault(c => c.IsValid);
            if (only != null)
                ImportOne(only.Code);
            return;
        }

        var name = string.IsNullOrWhiteSpace(folderName)
            ? FolderImportJob.DefaultFolderName(parsed)
            : folderName;
        folderName = name;
        if (plugin.FolderJob.Start(parsed, name))
            SetStatus(plugin.FolderJob.Status, false);
        else
            SetStatus(plugin.FolderJob.Status, true);
    }

    private void DrawSavedListRow()
    {
        var job = plugin.FolderJob;
        var (boards, folders) = TofuImporter.GetSavedCounts();
        var empty = boards == 0 && folders == 0;

        ImGui.Text(Loc.Format(L.UiSavedListCount, boards, FolderImportJob.MaxSavedBoards));

        if (confirmDeleteSaved)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.72f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.86f, 0.24f, 0.24f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.12f, 0.12f, 1f));
            var clicked = false;
            using (ImRaii.Disabled(job.IsRunning || empty))
                clicked = ImGui.Button(Loc.Format(L.UiDeleteAllSavedConfirm, boards, folders));
            ImGui.PopStyleColor(3);
            Tooltip(L.UiDeleteAllSavedHelp);

            if (clicked)
            {
                confirmDeleteSaved = false;
                var result = TofuImporter.DeleteAllSaved();
                SetStatus(result.Message, !result.Success);
                if (result.Success)
                    Plugin.ChatPrint(result.Message);
                else
                    Plugin.ChatPrintError(result.Message);
            }

            ImGui.SameLine();
            if (ImGui.Button(Loc.Get(L.UiDeleteAllSavedCancel)))
                confirmDeleteSaved = false;
            return;
        }

        using (ImRaii.Disabled(job.IsRunning || empty || !TofuImporter.IsAvailable))
        {
            if (ImGui.Button(Loc.Get(L.UiDeleteAllSaved)))
                confirmDeleteSaved = true;
        }

        Tooltip(L.UiDeleteAllSavedHelp);
    }

    private void DrawStatus()
    {
        var color = statusError ? new Vector4(1f, 0.45f, 0.45f, 1f) : new Vector4(0.55f, 0.9f, 0.55f, 1f);
        if (plugin.FolderJob.IsRunning)
            color = new Vector4(0.95f, 0.85f, 0.4f, 1f);

        ImGui.PushTextWrapPos();
        ImGui.TextColored(color, status);
        ImGui.PopTextWrapPos();
    }

    private void DrawSettings()
    {
        ImGuiHelpers.ScaledDummy(4);
        DrawLanguageCombo();
        Tooltip(L.UiLanguageHelp);

        var showChat = plugin.Configuration.ShowChatMessages;
        if (ImGui.Checkbox(Loc.Get(L.UiShowChatMessages), ref showChat))
        {
            plugin.Configuration.ShowChatMessages = showChat;
            plugin.Configuration.Save();
        }
        Tooltip(L.UiShowChatMessagesHelp);

        ImGuiHelpers.ScaledDummy(8);
        if (ImGui.Button(Loc.Get(L.UiOpenStrategyBoard)))
            plugin.Importer.OpenStrategyBoard();

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(input)))
        {
            if (ImGui.Button(Loc.Get(L.UiImportRaw)))
                ImportOne(input.Trim());
        }

        Tooltip(L.UiSingleImportHelp);
    }

    private void DrawLanguageCombo()
    {
        var selected = string.IsNullOrWhiteSpace(plugin.Configuration.Language)
            ? Loc.Auto
            : plugin.Configuration.Language;

        ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginCombo(Loc.Get(L.UiLanguage), Loc.CultureLabel(selected)))
            return;

        if (ImGui.Selectable(Loc.CultureLabel(Loc.Auto), selected.Equals(Loc.Auto, StringComparison.OrdinalIgnoreCase)))
            SetLanguage(Loc.Auto);

        foreach (var culture in Loc.SupportedCultures)
        {
            if (ImGui.Selectable(Loc.CultureLabel(culture), selected.Equals(culture, StringComparison.OrdinalIgnoreCase)))
                SetLanguage(culture);
        }

        ImGui.EndCombo();
    }

    private static void Tooltip(string key, params object?[] args)
    {
        if (!ImGui.IsItemHovered())
            return;

        var text = args.Length == 0 ? Loc.Get(key) : Loc.Format(key, args);
        ImGui.SetTooltip(text);
    }

    private void SetLanguage(string culture)
    {
        plugin.Configuration.Language = culture;
        plugin.Configuration.Save();
        plugin.ReloadLanguage();
    }

    private void ParseInput()
    {
        parsed = ShareCodeParser.Parse(input);
        selectedIndex = 0;
        if (parsed.Count == 0)
        {
            SetStatus(Loc.Get(L.StatusNoCode), true);
            return;
        }

        var valid = parsed.Count(c => c.IsValid);
        if (string.IsNullOrWhiteSpace(folderName) && valid > 0)
            folderName = FolderImportJob.DefaultFolderName(parsed);

        var decodeFailed = parsed.Count(c => !string.IsNullOrEmpty(c.Error));
        SetStatus(decodeFailed == 0
            ? Loc.Format(L.StatusFound, parsed.Count)
            : Loc.Format(L.StatusFoundWithDecodeErrors, parsed.Count, decodeFailed),
            decodeFailed == parsed.Count);
    }

    private void ImportOne(string code)
    {
        var compact = string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
        if (compact.Contains("stgy:", StringComparison.OrdinalIgnoreCase))
            code = compact;

        var native = TofuImporter.ImportOne(code);
        if (native.Success)
        {
            SetStatus(native.Message, false);
            Plugin.ChatPrint(native.Message);
            return;
        }

        var result = plugin.Importer.Import(code);
        SetStatus(result.Message, !result.Success);
        if (result.Success)
            Plugin.ChatPrint(result.Message);
        else
            Plugin.ChatPrintError(result.Message);
    }

    private void SyncFolderJobStatus()
    {
        var job = plugin.FolderJob;
        if (job.Status == lastJobStatus)
            return;

        lastJobStatus = job.Status;
        if (!string.IsNullOrEmpty(job.Status))
            SetStatus(job.Status, job.HasError);
    }

    private void SetStatus(string message, bool error)
    {
        status = message;
        statusError = error;
    }
}
