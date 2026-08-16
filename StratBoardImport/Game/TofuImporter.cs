using System;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using StratBoardImport.Localization;

namespace StratBoardImport;

/// <summary>
/// Writes decoded share codes into the game's Strategy Board (Tofu) saved list.
/// </summary>
public static unsafe class TofuImporter
{
    public static bool IsAvailable
    {
        get
        {
            var tofu = TofuModule.Instance();
            return tofu != null && tofu->SavedBoardData != null;
        }
    }

    public static ImportResult ImportOne(string shareCode, string? folderName = null)
        => ImportMany([new ParsedShareCode { Code = shareCode, IsValid = true }], folderName);

    public static ImportResult ImportMany(IReadOnlyList<ParsedShareCode> codes, string? folderName)
    {
        var tofu = TofuModule.Instance();
        if (tofu == null || tofu->SavedBoardData == null)
            return ImportResult.Fail(Loc.Get(L.ImportNativeUnavailable));

        if (tofu->IsFull(TofuType.Saved, TofuItem.Board))
            return ImportResult.Fail(Loc.Format(L.ImportNativeFull, FolderImportJob.MaxSavedBoards));

        uint? folderIndex = null;
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            folderIndex = GetOrCreateFolder(tofu, folderName.Trim());
            if (folderIndex == null)
                return ImportResult.Fail(Loc.Format(L.ImportNativeFolderFailed, folderName));
        }

        var imported = 0;
        string? lastName = null;
        foreach (var code in codes)
        {
            if (!code.IsValid)
                continue;

            if (tofu->IsFull(TofuType.Saved, TofuItem.Board))
                break;

            DecodedBoard board;
            try
            {
                board = ShareCodeBoard.Parse(code.Code);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[SBI] Tofu parse failed for a share code.");
                continue;
            }

            var created = CreateBoard(tofu, board, code.Name);
            if (created == null)
                continue;

            if (folderIndex != null)
                tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);

            imported++;
            lastName = string.IsNullOrEmpty(board.Name) ? code.Name : board.Name;
        }

        if (imported == 0)
            return ImportResult.Fail(Loc.Get(L.ImportNativeFailed));

        tofu->HasChanges = true;
        tofu->SaveFile(true);
        RefreshListUi();

        if (!string.IsNullOrWhiteSpace(folderName) && imported > 1)
            return ImportResult.Ok(Loc.Format(L.ImportNativeFolderOk, imported, folderName));

        return ImportResult.Ok(Loc.Format(L.ImportNativeOk, lastName ?? "Board"));
    }

    private static TofuBoardEntry* CreateBoard(TofuModule* tofu, DecodedBoard board, string? fallbackName)
    {
        var entry = new TofuBoardEntry
        {
            IsValid = true,
            Background = board.Background,
        };

        var name = string.IsNullOrWhiteSpace(board.Name) ? fallbackName : board.Name;
        if (!string.IsNullOrWhiteSpace(name))
            entry.NameString = name.Length > 63 ? name[..63] : name;

        var count = Math.Min(board.Objects.Count, entry.Objects.Length);
        entry.NumberOfObjects = (byte)count;
        for (var i = 0; i < count; i++)
        {
            var src = board.Objects[i];
            var obj = new TofuShortObject
            {
                ObjectType = (TofuObjectType)src.Type,
                Flags = (TofuObjectFlags)src.Flags,
                PosX = src.X,
                PosY = src.Y,
                Angle = src.Angle,
                ArgsA = src.ArgsA,
                ArgsB = src.ArgsB,
                ArgsC = src.ArgsC,
                Scale = src.Scale,
                RGBA = new ByteColor { R = src.R, G = src.G, B = src.B, A = src.A },
            };
            if (!string.IsNullOrEmpty(src.Text))
                obj.TextString = src.Text.Length > 31 ? src.Text[..31] : src.Text;
            entry.Objects[i] = obj;
        }

        return tofu->CreateBoard(TofuType.Saved, &entry, true);
    }

    private static uint? GetOrCreateFolder(TofuModule* tofu, string name)
    {
        var existing = FindFolderIndex(tofu, name);
        if (existing != null)
            return existing;

        if (tofu->IsFull(TofuType.Saved, TofuItem.Folder))
            return null;

        var created = tofu->CreateFolder(TofuType.Saved, name);
        if (created == null)
            return null;

        return FindFolderIndex(tofu, name) ?? created->Index;
    }

    private static uint? FindFolderIndex(TofuModule* tofu, string name)
    {
        var count = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        for (uint i = 0; i < count; i++)
        {
            var folder = tofu->GetFolderAtUIIndex(TofuType.Saved, i);
            if (folder == null || !folder->IsValid)
                continue;
            if (string.Equals(folder->NameString, name, StringComparison.Ordinal))
                return i;
        }

        return null;
    }

    private static void RefreshListUi()
    {
        var agent = AgentTofuList.Instance();
        if (agent == null)
            return;

        if (agent->IsAddonShown())
        {
            agent->HideAddon();
            agent->ShowAddon();
        }
    }
}
