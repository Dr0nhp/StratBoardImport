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
    private string status = "Share-Code einfügen und prüfen.";
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
            "Lange Strategy-Board-Codes (einzelne Boards oder Ordner mit mehreren Seiten) " +
            "hier einfügen. Das Plugin schreibt den vollständigen String in das Spiel-Importfeld " +
            "und umgeht damit das Längenlimit.");

        ImGuiHelpers.ScaledDummy(6);

        if (ImGui.Button("Aus Zwischenablage"))
        {
            input = ImGui.GetClipboardText() ?? string.Empty;
            ParseInput();
        }

        ImGui.SameLine();
        if (ImGui.Button("Prüfen"))
            ParseInput();

        ImGui.SameLine();
        if (ImGui.Button("Leeren"))
        {
            input = string.Empty;
            parsed = [];
            selectedIndex = 0;
            folderName = string.Empty;
            SetStatus("Eingabe geleert.", false);
        }

        ImGui.SameLine();
        if (ImGui.Button("Strategy Board öffnen"))
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
        ImGui.Text($"Gefunden: {parsed.Count} Code(s), {input.Length} Zeichen");
        using var child = ImRaii.Child("parsed-codes", new Vector2(-1, 150 * ImGuiHelpers.GlobalScale), true);
        if (!child.Success)
            return;

        for (var i = 0; i < parsed.Count; i++)
        {
            var code = parsed[i];
            var name = string.IsNullOrEmpty(code.Name) ? "Unbenannt" : code.Name;
            var label = string.IsNullOrEmpty(code.Error)
                ? $"{i + 1}. {name}  ({code.Length} Zeichen, {code.ObjectCount} Objekte)"
                : $"{i + 1}. {name}  ({code.Length} Zeichen)  — {code.Error}";

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
            if (ImGui.Button("Ausgewählten Code importieren"))
                ImportOne(parsed[selectedIndex].Code);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(validCount == 0))
        {
            if (ImGui.Button(validCount <= 1 ? "Importieren" : $"Alle {validCount} Codes nacheinander kopieren"))
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
            if (ImGui.Button("Ausgewählten Code kopieren"))
            {
                ImGui.SetClipboardText(parsed[selectedIndex].Code);
                SetStatus("Code in die Zwischenablage kopiert.", false);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(input)))
        {
            if (ImGui.Button("Roheingabe importieren"))
                ImportOne(input.Trim());
        }

        ImGui.TextWrapped(
            "Einzelimport: Strategy Board öffnen → Neue Strategie → Share-Code, dann hier Importieren. " +
            "„Roheingabe importieren“ sendet den kompletten Text ungekürzt, auch wenn die Prüfung fehlschlägt.");
    }

    private void DrawFolderImport()
    {
        var validCount = parsed.Count(c => c.IsValid);
        var job = plugin.FolderJob;

        ImGuiHelpers.ScaledDummy(6);
        ImGui.Separator();
        ImGui.Text("Ordner-Import");
        ImGui.TextWrapped(
            "Das Spiel hat keine öffentliche Ordner-API. Lege den Ordner im Strategy Board an oder öffne ihn " +
            "(Name wird in die Zwischenablage kopiert). Danach für jede Seite Neue Strategie → Share-Code öffnen. " +
            "Das Plugin füllt den Code und bestätigt automatisch. " +
            $"Saved List: bis {FolderImportJob.MaxSavedBoards} Boards. Ein Ordner: bis {FolderImportJob.MaxBoardsPerFolder}. " +
            "Mehr als 10 Seiten: zweiten Ordner nutzen oder den Rest in der Saved List lassen.");

        ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(job.IsRunning))
        {
            ImGui.InputText("Ordnername", ref folderName, 64);
        }

        if (job.IsRunning)
        {
            if (ImGui.Button("Ordner-Import abbrechen"))
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
                ? $"Alle {validCount} Boards in Ordner importieren"
                : "Alle Boards in Ordner importieren";
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
                ImGui.TextDisabled("Mindestens zwei gültige [stgy:]-Codes nötig.");
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
        if (!ImGui.CollapsingHeader("Einstellungen"))
            return;

        var autoConfirm = plugin.Configuration.AutoConfirm;
        if (ImGui.Checkbox("Nach dem Einfügen automatisch bestätigen", ref autoConfirm))
        {
            plugin.Configuration.AutoConfirm = autoConfirm;
            plugin.Configuration.Save();
        }

        var callbackId = plugin.Configuration.ConfirmCallbackId;
        if (ImGui.InputInt("Callback-ID (OK-Button)", ref callbackId))
        {
            plugin.Configuration.ConfirmCallbackId = Math.Max(0, callbackId);
            plugin.Configuration.Save();
        }

        if (ImGui.Checkbox("Addon-Scan anzeigen (Debug)", ref showAddonScan) && showAddonScan)
            RefreshAddonScan();

        if (!showAddonScan)
            return;

        if (ImGui.Button("Scan aktualisieren"))
            RefreshAddonScan();

        ImGui.TextWrapped(plugin.LastAddonScan.Count == 0
            ? "Keine sichtbaren Addons mit Textfeld gefunden."
            : string.Join('\n', plugin.LastAddonScan));
    }

    private void ParseInput()
    {
        parsed = ShareCodeParser.Parse(input);
        selectedIndex = 0;
        if (parsed.Count == 0)
        {
            SetStatus("Kein [stgy:...]-Code erkannt.", true);
            return;
        }

        var valid = parsed.Count(c => c.IsValid);
        if (string.IsNullOrWhiteSpace(folderName) && valid > 0)
            folderName = FolderImportJob.DefaultFolderName(parsed);

        SetStatus(valid == parsed.Count
            ? $"{parsed.Count} gültige(r) Share-Code(s) erkannt."
            : $"{valid}/{parsed.Count} Codes gültig. Ungültige Codes werden übersprungen.", valid == 0);
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
            $"{parsed.Count(c => c.IsValid)} Codes in die Zwischenablage kopiert. Importiere sie nacheinander über das Share-Code-Fenster.",
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
