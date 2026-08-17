using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using StratBoardImport.Localization;

namespace StratBoardImport;

/// <summary>
/// Writes decoded share codes into the game's Strategy Board (Tofu) saved list.
/// </summary>
public static unsafe partial class TofuImporter
{
    private static bool reopenListWhenClosed;
    private static int reopenWaitTicks;

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
            return ImportResult.Fail(L.ImportNativeUnavailable);

        if (tofu->IsFull(TofuType.Saved, TofuItem.Board))
            return ImportResult.Fail(L.ImportNativeFull, FolderImportJob.MaxSavedBoards);

        var useFolders = !string.IsNullOrWhiteSpace(folderName);
        var baseFolderName = useFolders ? folderName!.Trim() : string.Empty;
        var folderSeries = 1;
        uint? folderIndex = null;
        var foldersUsed = new List<string>();
        var validCodes = 0;
        foreach (var c in codes)
        {
            if (c.IsValid)
                validCodes++;
        }

        Plugin.Debug($"ImportMany start codes={codes.Count} valid={validCodes} folder=\"{baseFolderName}\" tofu={tofu != null}");
        DumpSavedList(tofu, "before");

        if (useFolders)
        {
            folderIndex = NextFolderWithSpace(tofu, baseFolderName, ref folderSeries);
            Plugin.Debug($"First folder series={folderSeries} uiIndex={folderIndex} name=\"{FolderSeriesName(baseFolderName, folderSeries)}\"");
            if (folderIndex == null)
                return ImportResult.Fail(L.ImportNativeFolderFailed, folderName);
            foldersUsed.Add(FolderSeriesName(baseFolderName, folderSeries));
            LogFolder(tofu, folderIndex.Value, "first");
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
                    Plugin.Debug("Folder cap reached; remaining boards stay at root.");
                    useFolders = false;
                }
                else
                    Plugin.Debug($"EnsureFolder uiIndex={folderIndex} series={folderSeries} inFolder={tofu->GetNumberOfBoardsInFolder(TofuType.Saved, folderIndex.Value)}");
            }

            var created = CreateBoard(tofu, board, code.Name);
            if (created == null)
            {
                Plugin.Debug($"CreateBoard returned null for \"{code.Name}\"");
                continue;
            }

            Plugin.Debug($"Created \"{code.Name}\" {DescribeBoard(created)}");

            if (folderIndex != null)
            {
                var rootIndex = created->Index;
                var copy = tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);
                if (copy == null)
                {
                    folderIndex = EnsureFolderHasSpace(tofu, baseFolderName, ref folderSeries, null, foldersUsed);
                    Plugin.Debug($"Copy failed; retry folder uiIndex={folderIndex}");
                    if (folderIndex != null)
                        copy = tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);
                }

                if (copy != null)
                {
                    inFolders++;
                    Plugin.Debug($"Copied {DescribeBoard(copy)}");
                    if (!TryDeleteRootBoard(tofu, rootIndex))
                    {
                        Plugin.Log.Warning($"[SBI] Copied a board into a folder but could not remove the root duplicate (index {rootIndex}).");
                        Plugin.Debug("TryDeleteRootBoard failed.");
                    }
                    else
                        Plugin.Debug("TryDeleteRootBoard succeeded.");
                }
                else
                {
                    Plugin.Log.Warning("[SBI] CopyBoardToFolder failed; board was left in the Saved List root.");
                    Plugin.Debug("CopyBoardToFolder failed.");
                }
            }

            imported++;
            lastName = string.IsNullOrEmpty(board.Name) ? code.Name : board.Name;
        }

        if (imported == 0)
            return ImportResult.Fail(L.ImportNativeFailed);

        tofu->HasChanges = true;
        tofu->SaveFile(true);
        Plugin.Debug($"ImportMany done imported={imported} inFolders={inFolders} foldersUsed={foldersUsed.Count}");
        DumpSavedList(tofu, "after save");
        RefreshListUi();

        if (!string.IsNullOrWhiteSpace(folderName) && imported > 1)
        {
            if (foldersUsed.Count > 1)
            {
                return ImportResult.Ok(
                    L.ImportNativeFolderSplitOk,
                    imported,
                    foldersUsed.Count,
                    FolderImportJob.MaxBoardsPerFolder,
                    foldersUsed[0]);
            }

            if (inFolders < imported)
            {
                return ImportResult.Ok(
                    L.ImportNativeFolderPartialOk,
                    imported,
                    inFolders,
                    FolderImportJob.MaxBoardsPerFolder,
                    foldersUsed.Count > 0 ? foldersUsed[0] : folderName);
            }

            return ImportResult.Ok(L.ImportNativeFolderOk, imported, folderName);
        }

        return ImportResult.Ok(L.ImportNativeOk, lastName ?? "Board");
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
        {
            Plugin.Debug($"Reusing folder \"{name}\" uiIndex={existing}");
            return existing;
        }

        if (tofu->IsFull(TofuType.Saved, TofuItem.Folder))
            return null;

        var created = tofu->CreateFolder(TofuType.Saved, name);
        if (created == null)
        {
            Plugin.Debug($"CreateFolder \"{name}\" returned null.");
            return null;
        }

        Plugin.Debug($"CreateFolder \"{name}\" -> {DescribeFolder(created)}");
        return FindFolderIndex(tofu, name) ?? created->Index;
    }

    private static uint? FindFolderIndex(TofuModule* tofu, string name)
    {
        var count = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        for (uint i = 0; i < count; i++)
        {
            var folder = tofu->GetFolderAtUIIndex(TofuType.Saved, i);
            if (folder == null || !folder->IsValid || folder->IsBoard)
                continue;
            if (string.Equals(folder->NameString, name, StringComparison.Ordinal))
                return i;
        }

        return null;
    }

    private static bool TryGetBoard(TofuModule* tofu, byte boardIndex, out byte folderId, out byte positionInList)
    {
        folderId = 0;
        positionInList = 0;
        var data = tofu->SavedBoardData;
        if (data == null)
            return false;

        var span = data->Boards;
        var max = Math.Min(span.Length, data->MaxCount);
        for (var i = 0; i < max; i++)
        {
            if (!span[i].IsValid || span[i].Index != boardIndex)
                continue;
            folderId = span[i].Folder;
            positionInList = span[i].PositionInList;
            return true;
        }

        return false;
    }

    private static uint CountRealFolders(TofuModule* tofu)
    {
        var count = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        uint real = 0;
        for (uint i = 0; i < count; i++)
        {
            var folder = tofu->GetFolderAtUIIndex(TofuType.Saved, i);
            if (folder != null && folder->IsValid && !folder->IsBoard)
                real++;
        }

        return real;
    }

    private static bool TryDeleteRootBoard(TofuModule* tofu, byte boardIndex)
    {
        if (!TryGetBoard(tofu, boardIndex, out var folderId, out var positionInList))
        {
            Plugin.Debug($"Skip delete: no board idx={boardIndex}");
            return false;
        }

        var slot = tofu->GetFolderAtUIIndex(TofuType.Saved, folderId);
        Plugin.Debug($"Delete probe idx={boardIndex} folder={folderId} pos={positionInList} slot={(slot == null ? "null" : DescribeFolder(slot))}");

        if (slot == null || !slot->IsValid)
            return false;

        // folder=0 here means "inside M9S" (real folder idx 0). Root leftovers point at
        // a folder-array row with IsBoard=true (untitled mixed-list item).
        if (!slot->IsBoard)
        {
            Plugin.Debug("Skip delete: Folder points at a named folder; that board is already inside it.");
            return false;
        }

        var realFoldersBefore = CountRealFolders(tofu);
        var boardsBefore = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        var mixed = (uint)slot->PositionInList;
        Plugin.Debug($"DeleteItemAndContents mixedPos={mixed} folderUi={folderId} boards={boardsBefore} realFolders={realFoldersBefore}");

        var deleted = tofu->DeleteItemAndContents(TofuType.Saved, mixed);
        if (!deleted && mixed != folderId)
        {
            Plugin.Debug("Delete at PositionInList failed; trying folder UI index.");
            deleted = tofu->DeleteItemAndContents(TofuType.Saved, folderId);
        }

        if (!deleted)
        {
            Plugin.Debug("DeleteItemAndContents returned false.");
            return false;
        }

        var realFoldersAfter = CountRealFolders(tofu);
        var boardsAfter = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        if (realFoldersAfter != realFoldersBefore)
        {
            Plugin.Log.Warning("[SBI] A named folder was removed while deleting a root duplicate.");
            Plugin.Debug($"Delete removed a named folder. boards {boardsBefore}->{boardsAfter} realFolders {realFoldersBefore}->{realFoldersAfter}");
            return false;
        }

        Plugin.Debug($"Delete result boards {boardsBefore}->{boardsAfter} realFolders={realFoldersAfter}");
        return boardsAfter == boardsBefore - 1;
    }

    private static void LogFolder(TofuModule* tofu, uint uiIndex, string tag)
    {
        var folder = tofu->GetFolderAtUIIndex(TofuType.Saved, uiIndex);
        Plugin.Debug($"Folder[{tag}] uiIndex={uiIndex} {(folder == null ? "null" : DescribeFolder(folder))} boardsInFolder={tofu->GetNumberOfBoardsInFolder(TofuType.Saved, uiIndex)}");
    }

    private static string DescribeBoard(TofuBoardEntry* board)
    {
        if (board == null)
            return "board=null";
        return $"board idx={board->Index} folder={board->Folder} pos={board->PositionInList} valid={board->IsValid} name=\"{board->NameString}\"";
    }

    private static string DescribeFolder(TofuFolderEntry* folder)
    {
        if (folder == null)
            return "folder=null";
        return $"folder idx={folder->Index} pos={folder->PositionInList} valid={folder->IsValid} isBoard={folder->IsBoard} name=\"{folder->NameString}\"";
    }

    private static void DumpSavedList(TofuModule* tofu, string tag)
    {
        if (!Plugin.DebugEnabled)
            return;

        var boards = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        var folders = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        Plugin.Debug($"SavedList[{tag}] boards={boards} folderRows={folders} realFolders={CountRealFolders(tofu)}");

        var boardData = tofu->SavedBoardData;
        if (boardData != null)
        {
            var span = boardData->Boards;
            var max = Math.Min(span.Length, boardData->MaxCount);
            for (var i = 0; i < max; i++)
            {
                if (!span[i].IsValid)
                    continue;
                Plugin.Debug($"  B[{i}] idx={span[i].Index} folder={span[i].Folder} pos={span[i].PositionInList} name=\"{span[i].NameString}\"");
            }
        }

        var folderData = tofu->SavedFolderData;
        if (folderData != null)
        {
            var span = folderData->Folders;
            var max = Math.Min(span.Length, folderData->MaxCount);
            for (var i = 0; i < max; i++)
            {
                if (!span[i].IsValid)
                    continue;
                Plugin.Debug($"  F[{i}] idx={span[i].Index} pos={span[i].PositionInList} isBoard={span[i].IsBoard} name=\"{span[i].NameString}\"");
            }
        }
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
            return ImportResult.Fail(L.ImportNativeUnavailable);

        var boards = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        var folders = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        if (boards == 0 && folders == 0)
            return ImportResult.Ok(L.DeleteAllEmpty);

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
            return ImportResult.Fail(L.DeleteAllPartial, remainBoards, remainFolders);

        return ImportResult.Ok(L.DeleteAllOk, boards, folders);
    }

    public static void TickUiRefresh()
    {
        if (!reopenListWhenClosed)
            return;

        var agent = AgentTofuList.Instance();
        if (agent == null)
        {
            reopenListWhenClosed = false;
            return;
        }

        reopenWaitTicks--;
        var closed = !agent->IsAddonShown() && !agent->IsAgentActive();
        if (!closed && reopenWaitTicks > 0)
            return;

        reopenListWhenClosed = false;
        try
        {
            agent->Show();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[SBI] Could not re-open Strategy Board after a list change.");
        }
    }

    private static void RefreshListUi()
    {
        var listWasOpen = IsTofuUiOpen(AgentId.TofuList) || IsTofuUiOpen(AgentId.TofuPreview);
        HideTofuUi(AgentId.TofuPreview);
        HideTofuUi(AgentId.TofuEdit);
        HideTofuUi(AgentId.TofuImport);

        var list = AgentTofuList.Instance();
        if (list != null && (list->IsAddonShown() || list->IsAgentActive()))
        {
            try
            {
                list->Hide();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[SBI] Could not close Strategy Board before refresh.");
                return;
            }
        }

        if (!listWasOpen)
            return;

        reopenListWhenClosed = true;
        reopenWaitTicks = 8;
    }

    private static bool IsTofuUiOpen(AgentId id)
    {
        var agent = GetAgent(id);
        return agent != null && (agent->IsAddonShown() || agent->IsAgentActive());
    }

    private static void HideTofuUi(AgentId id)
    {
        var agent = GetAgent(id);
        if (agent == null || (!agent->IsAddonShown() && !agent->IsAgentActive()))
            return;

        try
        {
            agent->Hide();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[SBI] Could not close {id}.");
        }
    }

    private static AgentInterface* GetAgent(AgentId id)
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return null;

        var module = ui->GetAgentModule();
        return module == null ? null : module->GetAgentByInternalId(id);
    }
}
