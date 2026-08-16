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

    public MainWindow(Plugin plugin)
        : base("Strategy Board Import##StratBoardImport")
    {
        this.plugin = plugin;
        status = Loc.Get(L.StatusPasteHint);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(640, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        WindowName = Loc.Get(L.WindowTitle) + "##StratBoardImport";
    }

    public override void Draw()
    {
        SyncFolderJobStatus();

        ImGui.TextWrapped(Loc.Get(L.UiHeader));

        ImGuiHelpers.ScaledDummy(6);

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
            SetStatus(Loc.Get(L.StatusCleared), false);
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get(L.UiOpenStrategyBoard)))
            plugin.Importer.OpenStrategyBoard();

        ImGuiHelpers.ScaledDummy(4);
        ImGui.InputTextMultiline("##sharecode", ref input, 4_000_000,
            new Vector2(-1, 140 * ImGuiHelpers.GlobalScale));

        DrawParsedList();
        ImGuiHelpers.ScaledDummy(4);
        DrawImportActions();
        DrawFolderImport();
        ImGuiHelpers.ScaledDummy(8);
        DrawStatus();
        DrawSettings();
    }

    private void DrawParsedList()
    {
        ImGui.Text(Loc.Format(L.UiFoundCodes, parsed.Count, input.Length));
        using var child = ImRaii.Child("parsed-codes", new Vector2(-1, 150 * ImGuiHelpers.GlobalScale), true);
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

    private void DrawImportActions()
    {
        var canImportSelected = selectedIndex >= 0 && selectedIndex < parsed.Count && parsed[selectedIndex].IsValid;
        var validCount = parsed.Count(c => c.IsValid);

        using (ImRaii.Disabled(!canImportSelected))
        {
            if (ImGui.Button(Loc.Get(L.UiImportSelected)))
                ImportOne(parsed[selectedIndex].Code);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(validCount == 0))
        {
            if (ImGui.Button(validCount <= 1 ? Loc.Get(L.UiImport) : Loc.Format(L.UiCopyAll, validCount)))
            {
                if (validCount == 1)
                {
                    var only = parsed.First(c => c.IsValid);
                    ImportOne(only.Code);
                }
                else
                {
                    CopyAllValid();
                }
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(validCount == 0))
        {
            if (ImGui.Button(Loc.Get(L.UiCopySelected)))
            {
                ImGui.SetClipboardText(parsed[selectedIndex].Code);
                SetStatus(Loc.Get(L.StatusCopiedOne), false);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(input)))
        {
            if (ImGui.Button(Loc.Get(L.UiImportRaw)))
                ImportOne(input.Trim());
        }

        ImGui.TextWrapped(Loc.Get(L.UiSingleImportHelp));
    }

    private void DrawFolderImport()
    {
        var validCount = parsed.Count(c => c.IsValid);
        var job = plugin.FolderJob;

        ImGuiHelpers.ScaledDummy(6);
        ImGui.Separator();
        ImGui.Text(Loc.Get(L.UiFolderImport));
        ImGui.TextWrapped(Loc.Format(L.UiFolderImportHelp, FolderImportJob.MaxSavedBoards, FolderImportJob.MaxBoardsPerFolder));

        ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(job.IsRunning))
        {
            ImGui.InputText(Loc.Get(L.UiFolderName), ref folderName, 64);
        }

        if (job.IsRunning)
        {
            if (ImGui.Button(Loc.Get(L.UiCancelFolderImport)))
            {
                job.Cancel();
                SetStatus(job.Status, false);
            }

            if (job.TotalCount > 0)
            {
                ImGui.ProgressBar(job.ProgressFraction, new Vector2(-1, 0), job.ProgressLabel);
            }
        }
        else
        {
            var label = validCount >= 2
                ? Loc.Format(L.UiImportAllN, validCount)
                : Loc.Get(L.UiImportAll);
            using (ImRaii.Disabled(validCount < 2))
            {
                if (ImGui.Button(label))
                {
                    var name = string.IsNullOrWhiteSpace(folderName)
                        ? FolderImportJob.DefaultFolderName(parsed)
                        : folderName;
                    folderName = name;
                    if (job.Start(parsed, name))
                        SetStatus(job.Status, false);
                    else
                        SetStatus(job.Status, true);
                }
            }

            if (validCount < 2)
                ImGui.TextDisabled(Loc.Get(L.UiNeedTwoCodes));
        }

        DrawDeleteAllSaved();
    }

    private void DrawDeleteAllSaved()
    {
        var job = plugin.FolderJob;
        var (boards, folders) = TofuImporter.GetSavedCounts();
        var empty = boards == 0 && folders == 0;

        ImGuiHelpers.ScaledDummy(4);
        if (confirmDeleteSaved)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.72f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.86f, 0.24f, 0.24f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.12f, 0.12f, 1f));
            var clicked = false;
            using (ImRaii.Disabled(job.IsRunning || empty))
            {
                clicked = ImGui.Button(Loc.Format(L.UiDeleteAllSavedConfirm, boards, folders));
            }

            ImGui.PopStyleColor(3);
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
        }
        else
        {
            using (ImRaii.Disabled(job.IsRunning || empty || !TofuImporter.IsAvailable))
            {
                if (ImGui.Button(Loc.Get(L.UiDeleteAllSaved)))
                    confirmDeleteSaved = true;
            }
        }

        ImGui.TextDisabled(Loc.Get(L.UiDeleteAllSavedHelp));
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
        if (!ImGui.CollapsingHeader(Loc.Get(L.UiSettings)))
            return;

        DrawLanguageCombo();
        DrawHelp(L.UiLanguageHelp);

        var showChat = plugin.Configuration.ShowChatMessages;
        if (ImGui.Checkbox(Loc.Get(L.UiShowChatMessages), ref showChat))
        {
            plugin.Configuration.ShowChatMessages = showChat;
            plugin.Configuration.Save();
        }
        DrawHelp(L.UiShowChatMessagesHelp);
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

    private static void DrawHelp(string key)
    {
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(Loc.Get(key));
        ImGui.PopTextWrapPos();
        ImGuiHelpers.ScaledDummy(4);
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

    private void CopyAllValid()
    {
        var codes = parsed.Where(c => c.IsValid).Select(c => c.Code);
        ImGui.SetClipboardText(string.Join("\n\n", codes));
        SetStatus(Loc.Format(L.StatusCopiedAll, parsed.Count(c => c.IsValid)), false);
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
