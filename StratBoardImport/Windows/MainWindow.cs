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

    public MainWindow(Plugin plugin)
        : base("Strategy Board Import##StratBoardImport")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(640, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
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
            var label = code.IsValid
                ? $"{i + 1}. {(string.IsNullOrEmpty(code.Name) ? "Unbenannt" : code.Name)}  ({code.Length} Zeichen, {code.ObjectCount} Objekte)"
                : $"{i + 1}. Ungültig: {code.Error}";

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
            "Ablauf: Strategy Board öffnen → Neue Strategie → Share-Code. " +
            "Danach hier Importieren klicken. Bei mehreren Seiten den Dialog für jede Seite erneut öffnen " +
            "oder den jeweiligen Code kopieren. „Roheingabe importieren“ sendet den kompletten Text ungekürzt, " +
            "auch wenn die Prüfung fehlschlägt (z. B. Ordner-Codes).");
    }

    private void DrawStatus()
    {
        var color = statusError ? new Vector4(1f, 0.45f, 0.45f, 1f) : new Vector4(0.55f, 0.9f, 0.55f, 1f);
        ImGui.TextColored(color, status);
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

    private void SetStatus(string message, bool error)
    {
        status = message;
        statusError = error;
    }
}
