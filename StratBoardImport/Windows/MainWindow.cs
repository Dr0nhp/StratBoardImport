using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace StratBoardImport.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string input = string.Empty;
    private IReadOnlyList<ParsedShareCode> parsed = [];
    private string status = "Paste a share code and check it.";
    private bool statusError;
    private int selectedIndex;
    private bool showAddonScan;
    private string folderName = string.Empty;
    private string lastJobStatus = string.Empty;

    public MainWindow(Plugin plugin)
        : base("Strategy Board Import##StratBoardImport")
    {
        this.plugin = plugin;
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

    public override void Draw()
    {
        SyncFolderJobStatus();

        ImGui.TextWrapped(
            "Paste long Strategy Board codes here (one board or several pages). " +
            "The plugin writes the full string into the in-game import field, so the length limit does not cut it off.");

        ImGuiHelpers.ScaledDummy(6);

        if (ImGui.Button("From clipboard"))
        {
            input = ImGui.GetClipboardText() ?? string.Empty;
            ParseInput();
        }

        ImGui.SameLine();
        if (ImGui.Button("Check"))
            ParseInput();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            input = string.Empty;
            parsed = [];
            selectedIndex = 0;
            folderName = string.Empty;
            SetStatus("Input cleared.", false);
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Strategy Board"))
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
        ImGui.Text($"Found: {parsed.Count} code(s), {input.Length} characters");
        using var child = ImRaii.Child("parsed-codes", new Vector2(-1, 150 * ImGuiHelpers.GlobalScale), true);
        if (!child.Success)
            return;

        for (var i = 0; i < parsed.Count; i++)
        {
            var code = parsed[i];
            var name = string.IsNullOrEmpty(code.Name) ? "Unnamed" : code.Name;
            var label = string.IsNullOrEmpty(code.Error)
                ? $"{i + 1}. {name}  ({code.Length} chars, {code.ObjectCount} objects)"
                : $"{i + 1}. {name}  ({code.Length} chars)  — {code.Error}";

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
            if (ImGui.Button("Import selected code"))
                ImportOne(parsed[selectedIndex].Code);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(validCount == 0))
        {
            if (ImGui.Button(validCount <= 1 ? "Import" : $"Copy all {validCount} codes"))
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
            if (ImGui.Button("Copy selected code"))
            {
                ImGui.SetClipboardText(parsed[selectedIndex].Code);
                SetStatus("Code copied to the clipboard.", false);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(input)))
        {
            if (ImGui.Button("Import raw input"))
                ImportOne(input.Trim());
        }

        ImGui.TextWrapped(
            "Single import: open Strategy Board → New Strategy → Share Code, then click Import here. " +
            "Import raw input sends the full text uncut, even if the check cannot decode a name.");
    }

    private void DrawFolderImport()
    {
        var validCount = parsed.Count(c => c.IsValid);
        var job = plugin.FolderJob;

        ImGuiHelpers.ScaledDummy(6);
        ImGui.Separator();
        ImGui.Text("Folder import");
        ImGui.TextWrapped(
            "The game has no public folder API. Create or open the folder in Strategy Board " +
            "(the name is copied to the clipboard). Then open New Strategy → Share Code for each page. " +
            "The plugin fills the code and confirms it. " +
            $"Saved List: up to {FolderImportJob.MaxSavedBoards} boards. One folder: up to {FolderImportJob.MaxBoardsPerFolder}. " +
            "More than 10 pages: use a second folder or leave the rest in the Saved List.");

        ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(job.IsRunning))
        {
            ImGui.InputText("Folder name", ref folderName, 64);
        }

        if (job.IsRunning)
        {
            if (ImGui.Button("Cancel folder import"))
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
                ? $"Import all {validCount} boards into a folder"
                : "Import all boards into a folder";
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
                ImGui.TextDisabled("Need at least two [stgy:] codes.");
        }
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
        if (!ImGui.CollapsingHeader("Settings"))
            return;

        var autoConfirm = plugin.Configuration.AutoConfirm;
        if (ImGui.Checkbox("Auto-confirm after filling the field", ref autoConfirm))
        {
            plugin.Configuration.AutoConfirm = autoConfirm;
            plugin.Configuration.Save();
        }

        var callbackId = plugin.Configuration.ConfirmCallbackId;
        if (ImGui.InputInt("Callback ID (OK button)", ref callbackId))
        {
            plugin.Configuration.ConfirmCallbackId = Math.Max(0, callbackId);
            plugin.Configuration.Save();
        }

        if (ImGui.Checkbox("Show addon scan (debug)", ref showAddonScan) && showAddonScan)
            RefreshAddonScan();

        if (!showAddonScan)
            return;

        if (ImGui.Button("Refresh scan"))
            RefreshAddonScan();

        ImGui.TextWrapped(plugin.LastAddonScan.Count == 0
            ? "No visible addons with a text field found."
            : string.Join('\n', plugin.LastAddonScan));
    }

    private void ParseInput()
    {
        parsed = ShareCodeParser.Parse(input);
        selectedIndex = 0;
        if (parsed.Count == 0)
        {
            SetStatus("No [stgy:...] code found.", true);
            return;
        }

        var valid = parsed.Count(c => c.IsValid);
        if (string.IsNullOrWhiteSpace(folderName) && valid > 0)
            folderName = FolderImportJob.DefaultFolderName(parsed);

        var decodeFailed = parsed.Count(c => !string.IsNullOrEmpty(c.Error));
        SetStatus(decodeFailed == 0
            ? $"{parsed.Count} share code(s) found."
            : $"{parsed.Count} share code(s) found. {decodeFailed} name(s) could not be decoded.", decodeFailed == parsed.Count);
    }

    private void ImportOne(string code)
    {
        var compact = string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
        if (compact.Contains("stgy:", StringComparison.OrdinalIgnoreCase))
            code = compact;

        var result = plugin.Importer.Import(
            code,
            plugin.Configuration.AutoConfirm,
            plugin.Configuration.ConfirmCallbackId);
        SetStatus(result.Message, !result.Success);
        if (result.Success)
            Plugin.ChatGui.Print($"[SBI] {result.Message}");
        else
            Plugin.ChatGui.PrintError($"[SBI] {result.Message}");
    }

    private void CopyAllValid()
    {
        var codes = parsed.Where(c => c.IsValid).Select(c => c.Code);
        ImGui.SetClipboardText(string.Join("\n\n", codes));
        SetStatus(
            $"{parsed.Count(c => c.IsValid)} codes copied to the clipboard. Import them one by one in the share-code window.",
            false);
    }

    private void RefreshAddonScan()
    {
        plugin.LastAddonScan = plugin.Importer.ListCandidateAddons();
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
