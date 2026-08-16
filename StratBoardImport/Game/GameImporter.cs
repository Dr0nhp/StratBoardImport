using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using StratBoardImport.Localization;

namespace StratBoardImport;

public sealed unsafe class GameImporter
{
    private static readonly string[] PreferredAddonNames =
    [
        "InputString",
        "StrategyBoardShare",
        "StrategyBoardShareCode",
        "StrategyBoardCode",
        "StrategyBoardInput",
        "StrategyBoardNew",
        "TofuShare",
        "TofuInput",
        "TofuNew",
        "TofuEdit",
        "Tofu",
        "StrategyBoardSelect",
        "StrategyBoardEdit",
        "Whiteboard",
        "WhiteBoard",
    ];

    public ImportResult Import(string shareCode, bool autoConfirm, int confirmCallbackId)
        => Import(shareCode, autoConfirm, confirmCallbackId, requireShareCodeDialog: false);

    public ImportResult Import(string shareCode, bool autoConfirm, int confirmCallbackId, bool requireShareCodeDialog)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
            return ImportResult.Fail(Loc.Get(L.ImportNoCode));

        var found = FindShareCodeDialog(out var addon, out var textInput, out var addonName);
        if (!found && !requireShareCodeDialog)
            found = FindShareCodeTarget(out addon, out textInput, out addonName);

        if (!found)
            return ImportResult.Fail(Loc.Get(L.ImportNoField));

        SetInputText(textInput, shareCode);

        if (autoConfirm)
            ConfirmShareCodeDialog(addon, shareCode, confirmCallbackId);

        return ImportResult.Ok(autoConfirm
            ? Loc.Format(L.ImportWroteConfirmed, shareCode.Length, addonName)
            : Loc.Format(L.ImportWroteManual, shareCode.Length, addonName));
    }

    private static void ConfirmShareCodeDialog(AtkUnitBase* addon, string shareCode, int confirmCallbackId)
    {
        var values = stackalloc AtkValue[2];
        values[0] = default;
        values[1] = default;
        values[0].Type = AtkValueType.Int;
        values[0].Int = confirmCallbackId;
        values[1].SetManagedString(shareCode);
        addon->FireCallback(2, values, true);

        if (addon->IsVisible)
            addon->FireCallbackInt(confirmCallbackId);
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
                name.Contains("WhiteBoard", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Tofu", StringComparison.OrdinalIgnoreCase))
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

    public List<string> ListVisibleTextInputNames()
    {
        var names = new List<string>();
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return names;

        var count = Math.Min((int)manager->AllLoadedUnitsList.Count, 256);
        for (var i = 0; i < count; i++)
        {
            var addon = manager->AllLoadedUnitsList.Entries[i].Value;
            if (addon == null || !addon->IsReady || !addon->IsVisible || FindTextInput(addon) == null)
                continue;

            var name = addon->NameString;
            if (name.Contains("Chat", StringComparison.OrdinalIgnoreCase))
                continue;

            names.Add(name);
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
            if (TryUseShareCodeAddon(candidate, out addon, out textInput, out addonName))
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

        if (!LooksLikeShareCodeDialog(candidate))
            return false;

        var input = FindTextInput(candidate);
        if (input == null)
            return false;

        addon = candidate;
        textInput = input;
        addonName = candidate->NameString;
        return true;
    }

    private static bool LooksLikeShareCodeDialog(AtkUnitBase* addon)
    {
        var name = addon->NameString;
        if (string.IsNullOrEmpty(name) || IsExcludedAddonName(name))
            return false;

        if (NameLooksLikeShareCode(name))
            return true;

        return AddonTextSuggestsShareCode(addon);
    }

    private static bool IsExcludedAddonName(string name)
        => name.Contains("Chat", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Inventory", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Macro", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("StrategyBoardList", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TofuList", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Folder", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Rename", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Filter", StringComparison.OrdinalIgnoreCase);

    private static bool NameLooksLikeShareCode(string name)
        => name.Contains("Share", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Stgy", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("TofuShare", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("TofuInput", StringComparison.OrdinalIgnoreCase) ||
           (name.Contains("Input", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("Strategy", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("White", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Tofu", StringComparison.OrdinalIgnoreCase)));

    private static bool AddonTextSuggestsShareCode(AtkUnitBase* addon)
    {
        var text = CollectAddonText(addon);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.Contains("stgy", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("share-code", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("share code", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sharecode", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Contains("freigabecode", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("freigabe-code", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Contains("code de partage", StringComparison.OrdinalIgnoreCase))
            return true;

        return text.Contains("共有コード", StringComparison.Ordinal) ||
               text.Contains("シェアコード", StringComparison.Ordinal);
    }

    private static string CollectAddonText(AtkUnitBase* addon)
    {
        var builder = new StringBuilder();
        var count = addon->UldManager.NodeListCount;
        for (var i = 0; i < count; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || node->Type != NodeType.Text)
                continue;

            var text = ((AtkTextNode*)node)->NodeText.ToString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            builder.Append(text);
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static int ScoreAddon(AtkUnitBase* addon)
    {
        var name = addon->NameString;
        if (name.Contains("Share", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Stgy", StringComparison.OrdinalIgnoreCase))
        {
            return 120;
        }

        if (name.Contains("Tofu", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Strategy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Whiteboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WhiteBoard", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (name.Equals("InputString", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Input", StringComparison.OrdinalIgnoreCase))
        {
            return AddonTextSuggestsShareCode(addon) ? 110 : 20;
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
