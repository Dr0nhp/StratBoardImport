using System;
using System.Collections.Generic;
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

        var useFolders = !string.IsNullOrWhiteSpace(folderName);
        var baseFolderName = useFolders ? folderName!.Trim() : string.Empty;
        var folderSeries = 1;
        uint? folderIndex = null;
        var foldersUsed = new List<string>();

        if (useFolders)
        {
            folderIndex = NextFolderWithSpace(tofu, baseFolderName, ref folderSeries);
            if (folderIndex == null)
                return ImportResult.Fail(Loc.Format(L.ImportNativeFolderFailed, folderName));
            foldersUsed.Add(FolderSeriesName(baseFolderName, folderSeries));
        }

        var imported = 0;
        var inFolders = 0;
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

            if (useFolders)
            {
                folderIndex = EnsureFolderHasSpace(tofu, baseFolderName, ref folderSeries, folderIndex, foldersUsed);
                if (folderIndex == null)
                {
                    Plugin.Log.Warning("[SBI] No folder with free slots left; remaining boards stay in the Saved List root.");
                    useFolders = false;
                }
            }

            var created = CreateBoard(tofu, board, code.Name);
            if (created == null)
                continue;

            if (folderIndex != null)
            {
                var copy = tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);
                if (copy == null)
                {
                    folderIndex = EnsureFolderHasSpace(tofu, baseFolderName, ref folderSeries, null, foldersUsed);
                    if (folderIndex != null)
                        copy = tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);
                }

                if (copy != null)
                {
                    if (!TryDeleteBoard(tofu, created))
                        Plugin.Log.Warning("[SBI] Copied a board into a folder but could not remove the root duplicate.");
                    inFolders++;
                }
                else
                {
                    Plugin.Log.Warning("[SBI] CopyBoardToFolder failed; board was left in the Saved List root.");
                }
            }

            imported++;
            lastName = string.IsNullOrEmpty(board.Name) ? code.Name : board.Name;
        }

        if (imported == 0)
            return ImportResult.Fail(Loc.Get(L.ImportNativeFailed));

        tofu->HasChanges = true;
        tofu->SaveFile(true);
        RefreshListUi();

        if (!string.IsNullOrWhiteSpace(folderName) && imported > 1)
        {
            if (foldersUsed.Count > 1)
            {
                return ImportResult.Ok(Loc.Format(
                    L.ImportNativeFolderSplitOk,
                    imported,
                    foldersUsed.Count,
                    FolderImportJob.MaxBoardsPerFolder,
                    foldersUsed[0]));
            }

            if (inFolders < imported)
            {
                return ImportResult.Ok(Loc.Format(
                    L.ImportNativeFolderPartialOk,
                    imported,
                    inFolders,
                    FolderImportJob.MaxBoardsPerFolder,
                    foldersUsed.Count > 0 ? foldersUsed[0] : folderName));
            }

            return ImportResult.Ok(Loc.Format(L.ImportNativeFolderOk, imported, folderName));
        }

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

    private static uint? EnsureFolderHasSpace(
        TofuModule* tofu,
        string baseName,
        ref int series,
        uint? currentIndex,
        List<string> foldersUsed)
    {
        if (currentIndex != null &&
            tofu->GetNumberOfBoardsInFolder(TofuType.Saved, currentIndex.Value) < FolderImportJob.MaxBoardsPerFolder)
        {
            return currentIndex;
        }

        if (currentIndex != null)
            series++;

        var next = NextFolderWithSpace(tofu, baseName, ref series);
        if (next == null)
            return null;

        var name = FolderSeriesName(baseName, series);
        if (!foldersUsed.Contains(name))
            foldersUsed.Add(name);
        return next;
    }

    private static uint? NextFolderWithSpace(TofuModule* tofu, string baseName, ref int series)
    {
        var maxFolders = (int)Math.Max(1, tofu->MaxItemAllowed(TofuType.Saved, TofuItem.Folder));
        for (; series <= maxFolders; series++)
        {
            var name = FolderSeriesName(baseName, series);
            var index = GetOrCreateFolder(tofu, name);
            if (index == null)
                continue;
            if (tofu->GetNumberOfBoardsInFolder(TofuType.Saved, index.Value) < FolderImportJob.MaxBoardsPerFolder)
                return index;
        }

        return null;
    }

    private static string FolderSeriesName(string baseName, int series)
        => series <= 1 ? baseName : $"{baseName} ({series})";

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

    private static bool TryDeleteBoard(TofuModule* tofu, TofuBoardEntry* board)
    {
        if (board == null || !board->IsValid)
            return false;

        var mixedIndex = tofu->GetFolderIndexByBoardIndex(TofuType.Saved, board->Index);
        if (mixedIndex >= 0 && tofu->GetBoardAtUIIndex(TofuType.Saved, (uint)mixedIndex) == board)
            return tofu->DeleteItemAndContents(TofuType.Saved, (uint)mixedIndex);

        var mixedCount = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board)
                         + tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        for (uint i = 0; i < mixedCount + 4; i++)
        {
            if (tofu->GetBoardAtUIIndex(TofuType.Saved, i) != board)
                continue;
            return tofu->DeleteItemAndContents(TofuType.Saved, i);
        }

        Plugin.Log.Warning("[SBI] Could not find the root board to delete after copying it into a folder.");
        return false;
    }

    public static (uint Boards, uint Folders) GetSavedCounts()
    {
        var tofu = TofuModule.Instance();
        if (tofu == null || tofu->SavedBoardData == null)
            return (0, 0);

        return (
            tofu->TotalItemCount(TofuType.Saved, TofuItem.Board),
            tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder));
    }

    public static ImportResult DeleteAllSaved()
    {
        var tofu = TofuModule.Instance();
        if (tofu == null || tofu->SavedBoardData == null)
            return ImportResult.Fail(Loc.Get(L.ImportNativeUnavailable));

        var boards = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        var folders = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        if (boards == 0 && folders == 0)
            return ImportResult.Ok(Loc.Get(L.DeleteAllEmpty));

        if (tofu->SavedFolderData != null)
            tofu->SavedFolderData->DeleteAllItems();
        tofu->SavedBoardData->DeleteAllItems();

        for (var n = 0; n < 120; n++)
        {
            var leftBoards = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
            var leftFolders = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
            if (leftBoards == 0 && leftFolders == 0)
                break;

            var mixed = leftBoards + leftFolders;
            var removed = false;
            for (var i = (int)mixed; i >= 0; i--)
            {
                if (!tofu->DeleteItemAndContents(TofuType.Saved, (uint)i))
                    continue;
                removed = true;
                break;
            }

            if (!removed)
                break;
        }

        tofu->HasChanges = true;
        tofu->SaveFile(true);
        RefreshListUi();

        var remainBoards = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        var remainFolders = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        if (remainBoards > 0 || remainFolders > 0)
            return ImportResult.Fail(Loc.Format(L.DeleteAllPartial, remainBoards, remainFolders));

        return ImportResult.Ok(Loc.Format(L.DeleteAllOk, boards, folders));
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
