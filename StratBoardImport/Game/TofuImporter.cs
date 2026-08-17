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
public static unsafe class TofuImporter
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

        if (useFolders)
        {
            folderIndex = NextFolderWithSpace(tofu, baseFolderName, ref folderSeries);
            if (folderIndex == null)
                return ImportResult.Fail(L.ImportNativeFolderFailed, folderName);
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
                var rootIndex = created->Index;
                var copy = tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);
                if (copy == null)
                {
                    folderIndex = EnsureFolderHasSpace(tofu, baseFolderName, ref folderSeries, null, foldersUsed);
                    if (folderIndex != null)
                        copy = tofu->CopyBoardToFolder(TofuType.Saved, created, folderIndex.Value);
                }

                if (copy != null)
                {
                    inFolders++;
                    if (!TryDeleteRootBoard(tofu, rootIndex))
                        Plugin.Log.Warning($"[SBI] Copied a board into a folder but could not remove the root duplicate (index {rootIndex}).");
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
            return ImportResult.Fail(L.ImportNativeFailed);

        tofu->HasChanges = true;
        tofu->SaveFile(true);
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

    private static bool TryDeleteRootBoard(TofuModule* tofu, byte boardIndex)
    {
        if (!tofu->IsItemValid(TofuType.Saved, TofuItem.Board, boardIndex))
            return false;

        // In-folder boards make this API return the folder's mixed-list row. Deleting that
        // wipes the folder. Root boards return their own row in the mixed list.
        var inFolder = tofu->GetBoardFolderIndexByBoardIndex(TofuType.Saved, boardIndex);
        if (inFolder < 0)
            return false;
        if (inFolder != boardIndex)
            return false;

        var mixedIndex = tofu->GetFolderIndexByBoardIndex(TofuType.Saved, boardIndex);
        if (mixedIndex < 0)
            return false;

        var foldersBefore = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        // Folders occupy the first mixed-list rows. Never delete those.
        if (mixedIndex < foldersBefore)
            return false;
        var boardsBefore = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
        if (!tofu->DeleteItemAndContents(TofuType.Saved, (uint)mixedIndex))
            return false;

        if (tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder) != foldersBefore)
        {
            Plugin.Log.Warning("[SBI] A folder was removed while deleting a root duplicate. Remaining boards may be missing.");
            return false;
        }

        return tofu->TotalItemCount(TofuType.Saved, TofuItem.Board) == boardsBefore - 1;
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
