using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace StratBoardImport;

public sealed unsafe class GameImporter
{
    private static readonly string[] PreferredAddonNames =
    [
        "StrategyBoard",
        "StrategyBoardList",
        "StrategyBoardShare",
        "StrategyBoardInput",
        "StrategyBoardCode",
        "StrategyBoardNew",
        "StrategyBoardShareCode",
        "StrategyBoardSelect",
        "StrategyBoardEdit",
        "StrategyBoardMain",
        "Whiteboard",
        "WhiteBoard",
    ];

    public ImportResult Import(string shareCode, bool autoConfirm, int confirmCallbackId)
        => Import(shareCode, autoConfirm, confirmCallbackId, requireShareCodeDialog: false);

    public ImportResult Import(string shareCode, bool autoConfirm, int confirmCallbackId, bool requireShareCodeDialog)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
            return ImportResult.Fail("Kein Share-Code angegeben.");

        var found = requireShareCodeDialog
            ? FindShareCodeDialog(out var addon, out var textInput, out var addonName)
            : FindShareCodeTarget(out addon, out textInput, out addonName);

        if (!found)
        {
            return ImportResult.Fail(
                "Kein Share-Code-Eingabefeld gefunden. Öffne im Spiel: Strategy Board → Neue Strategie → Share-Code, und versuche es erneut.");
        }

        SetInputText(textInput, shareCode);

        if (autoConfirm)
            addon->FireCallbackInt(confirmCallbackId);

        return ImportResult.Ok(
            $"Code ({shareCode.Length} Zeichen) in '{addonName}' geschrieben" +
            (autoConfirm ? " und bestätigt." : ". Bitte im Spiel auf Übernehmen/OK klicken."));
    }

    public bool IsShareCodeWindowOpen(out string addonName)
        => FindShareCodeDialog(out _, out _, out addonName);

    public bool IsAnyStrategyBoardUiOpen()
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return false;

        var count = Math.Min((int)manager->AllLoadedUnitsList.Count, 256);
        for (var i = 0; i < count; i++)
        {
            var addon = manager->AllLoadedUnitsList.Entries[i].Value;
            if (addon == null || !addon->IsReady || !addon->IsVisible)
                continue;

            var name = addon->NameString;
            if (name.Contains("Strategy", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Whiteboard", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("WhiteBoard", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void OpenStrategyBoard()
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return;

        var command = stackalloc Utf8String[1];
        *command = default;
        command->Ctor();
        command->SetString("/strategyboard"u8);
        uiModule->ProcessChatBoxEntry(command);
        command->Dtor();
    }

    public List<string> ListCandidateAddons()
    {
        var names = new List<string>();
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return names;

        var count = Math.Min((int)manager->AllLoadedUnitsList.Count, 256);
        for (var i = 0; i < count; i++)
        {
            var addon = manager->AllLoadedUnitsList.Entries[i].Value;
            if (addon == null || FindTextInput(addon) == null)
                continue;

            names.Add($"{addon->NameString} (id {addon->Id}, visible={addon->IsVisible})");
        }

        return names;
    }

    private static bool FindShareCodeTarget(
        out AtkUnitBase* addon,
        out AtkComponentTextInput* textInput,
        out string addonName)
    {
        addon = null;
        textInput = null;
        addonName = string.Empty;

        foreach (var name in PreferredAddonNames)
        {
            var candidate = GetAddonByName(name);
            if (candidate == null || !candidate->IsReady || !candidate->IsVisible)
                continue;

            var input = FindTextInput(candidate);
            if (input == null)
                continue;

            addon = candidate;
            textInput = input;
            addonName = candidate->NameString;
            return true;
        }

        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return false;

        var bestScore = int.MinValue;
        var count = Math.Min((int)manager->AllLoadedUnitsList.Count, 256);
        for (var i = 0; i < count; i++)
        {
            var candidate = manager->AllLoadedUnitsList.Entries[i].Value;
            if (candidate == null || !candidate->IsReady || !candidate->IsVisible)
                continue;

            var input = FindTextInput(candidate);
            if (input == null)
                continue;

            var score = ScoreAddon(candidate);
            if (score <= 0 || score <= bestScore)
                continue;

            bestScore = score;
            addon = candidate;
            textInput = input;
            addonName = candidate->NameString;
        }

        return addon != null && textInput != null;
    }

    private static bool FindShareCodeDialog(
        out AtkUnitBase* addon,
        out AtkComponentTextInput* textInput,
        out string addonName)
    {
        addon = null;
        textInput = null;
        addonName = string.Empty;

        var manager = RaptureAtkUnitManager.Instance();
        if (manager != null)
        {
            var focused = manager->FocusedAddon;
            if (TryUseShareCodeAddon(focused, out addon, out textInput, out addonName))
                return true;
        }

        foreach (var name in PreferredAddonNames)
        {
            if (!LooksLikeShareCodeDialog(name))
                continue;

            var candidate = GetAddonByName(name);
            if (TryUseShareCodeAddon(candidate, out addon, out textInput, out addonName))
                return true;
        }

        if (manager == null)
            return false;

        var count = Math.Min((int)manager->AllLoadedUnitsList.Count, 256);
        for (var i = 0; i < count; i++)
        {
            var candidate = manager->AllLoadedUnitsList.Entries[i].Value;
            if (TryUseShareCodeAddon(candidate, out addon, out textInput, out addonName))
                return true;
        }

        return false;
    }

    private static bool TryUseShareCodeAddon(
        AtkUnitBase* candidate,
        out AtkUnitBase* addon,
        out AtkComponentTextInput* textInput,
        out string addonName)
    {
        addon = null;
        textInput = null;
        addonName = string.Empty;

        if (candidate == null || !candidate->IsReady || !candidate->IsVisible)
            return false;

        if (!LooksLikeShareCodeDialog(candidate->NameString))
            return false;

        var input = FindTextInput(candidate);
        if (input == null)
            return false;

        addon = candidate;
        textInput = input;
        addonName = candidate->NameString;
        return true;
    }

    private static bool LooksLikeShareCodeDialog(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.Equals("StrategyBoard", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("StrategyBoardList", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("StrategyBoardMain", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Folder", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("List", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Name", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rename", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Filter", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("Share", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Stgy", StringComparison.OrdinalIgnoreCase) ||
               (name.Contains("Input", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Strategy", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("White", StringComparison.OrdinalIgnoreCase)));
    }

    private static int ScoreAddon(AtkUnitBase* addon)
    {
        var name = addon->NameString;
        if (name.Contains("Strategy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Whiteboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WhiteBoard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Stgy", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (name.Contains("Input", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Share", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Code", StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return 0;
    }

    private static AtkUnitBase* GetAddonByName(string name)
        => Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);

    private static AtkComponentTextInput* FindTextInput(AtkUnitBase* addon)
    {
        var count = addon->UldManager.NodeListCount;
        AtkComponentTextInput* found = null;
        for (var i = 0; i < count; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (int)node->Type < 1000)
                continue;

            var componentNode = (AtkComponentNode*)node;
            var component = componentNode->Component;
            if (component == null)
                continue;

            if (component->GetComponentType() != ComponentType.TextInput)
                continue;

            found = (AtkComponentTextInput*)component;
        }

        return found;
    }

    private static void SetInputText(AtkComponentTextInput* input, string text)
    {
        input->SetText(text);
        input->AtkComponentInputBase.EvaluatedString.SetString(text);
        input->AtkComponentInputBase.RawString.SetString(text);
    }
}

public readonly record struct ImportResult(bool Success, string Message)
{
    public static ImportResult Ok(string message) => new(true, message);
    public static ImportResult Fail(string message) => new(false, message);
}
