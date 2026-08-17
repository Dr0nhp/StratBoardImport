using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using StratBoardImport.Localization;

namespace StratBoardImport;

public sealed class SavedListSnapshot
{
    public List<SavedFolderInfo> Folders { get; } = [];
    public List<SavedBoardInfo> RootBoards { get; } = [];
    public uint BoardCount { get; set; }
    public uint FolderCount { get; set; }
}

public sealed class SavedFolderInfo
{
    public uint UiIndex { get; init; }
    public byte FolderIndex { get; init; }
    public byte PositionInList { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<SavedBoardInfo> Boards { get; } = [];
}

public sealed class SavedBoardInfo
{
    public byte Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public DecodedBoard Board { get; init; } = new();

    public string DisplayName
        => string.IsNullOrWhiteSpace(Name) ? Loc.Get(L.UiUnnamed) : Name;
}

public sealed class SavedListDeleteRequest
{
    public List<string> WipeFolders { get; } = [];
    public List<DecodedBoard> WipeRootBoards { get; } = [];
    public Dictionary<string, List<DecodedBoard>> KeepInFolder { get; } = new(StringComparer.Ordinal);
}

public static unsafe partial class TofuImporter
{
    public static SavedListSnapshot ReadSavedList()
    {
        var snapshot = new SavedListSnapshot();
        var tofu = TofuModule.Instance();
        if (tofu == null || tofu->SavedBoardData == null)
            return snapshot;

        var folderCount = tofu->TotalItemCount(TofuType.Saved, TofuItem.Folder);
        snapshot.FolderCount = CountRealFolders(tofu);
        snapshot.BoardCount = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);

        var foldersByIndex = new Dictionary<byte, SavedFolderInfo>();
        for (uint i = 0; i < folderCount; i++)
        {
            var folder = tofu->GetFolderAtUIIndex(TofuType.Saved, i);
            if (folder == null || !folder->IsValid || folder->IsBoard)
                continue;

            var info = new SavedFolderInfo
            {
                UiIndex = i,
                FolderIndex = folder->Index,
                PositionInList = folder->PositionInList,
                Name = folder->NameString,
            };
            snapshot.Folders.Add(info);
            foldersByIndex[folder->Index] = info;
        }

        var data = tofu->SavedBoardData;
        var span = data->Boards;
        var max = Math.Min(span.Length, data->MaxCount);
        for (var i = 0; i < max; i++)
        {
            if (!span[i].IsValid)
                continue;

            var info = new SavedBoardInfo
            {
                Index = span[i].Index,
                Name = span[i].NameString,
                Board = CopyBoard(span[i]),
            };

            if (foldersByIndex.TryGetValue(span[i].Folder, out var folder))
                folder.Boards.Add(info);
            else
                snapshot.RootBoards.Add(info);
        }

        return snapshot;
    }

    public static string EncodeBoard(DecodedBoard board)
        => ShareCodeCodec.Encode(board);

    public static ImportResult DeleteSelection(SavedListDeleteRequest request)
    {
        var tofu = TofuModule.Instance();
        if (tofu == null || tofu->SavedBoardData == null)
            return ImportResult.Fail(L.ImportNativeUnavailable);

        var deletedBoards = 0;
        var deletedFolders = 0;

        foreach (var (name, keep) in request.KeepInFolder)
        {
            if (request.WipeFolders.Contains(name))
                continue;

            var before = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
            if (!TryDeleteNamedFolder(tofu, name))
            {
                Plugin.Debug($"Library delete: could not rebuild folder \"{name}\".");
                continue;
            }

            deletedFolders++;
            deletedBoards += (int)(before - tofu->TotalItemCount(TofuType.Saved, TofuItem.Board));

            if (keep.Count == 0)
                continue;

            var restored = RestoreBoards(tofu, keep, name);
            deletedBoards -= restored;
        }

        foreach (var name in request.WipeFolders)
        {
            var boardsBefore = tofu->TotalItemCount(TofuType.Saved, TofuItem.Board);
            if (!TryDeleteNamedFolder(tofu, name))
            {
                Plugin.Debug($"Library delete: could not wipe folder \"{name}\".");
                continue;
            }

            deletedFolders++;
            deletedBoards += (int)(boardsBefore - tofu->TotalItemCount(TofuType.Saved, TofuItem.Board));
        }

        foreach (var board in request.WipeRootBoards)
        {
            if (!TryDeleteMatchingRootBoard(tofu, board))
            {
                Plugin.Debug($"Library delete: could not remove root board \"{board.Name}\".");
                continue;
            }

            deletedBoards++;
        }

        if (deletedBoards == 0 && deletedFolders == 0)
            return ImportResult.Fail(L.LibraryDeleteNone);

        tofu->HasChanges = true;
        tofu->SaveFile(true);
        RefreshListUi();
        return ImportResult.Ok(L.LibraryDeleteOk, deletedBoards, deletedFolders);
    }

    private static int RestoreBoards(TofuModule* tofu, List<DecodedBoard> boards, string folderName)
    {
        var restored = 0;
        if (FindNamedFolder(tofu, folderName) == null)
        {
            var created = tofu->CreateFolder(TofuType.Saved, folderName);
            if (created == null)
                return 0;
        }

        foreach (var board in boards)
        {
            if (tofu->IsFull(TofuType.Saved, TofuItem.Board))
                break;

            var created = CreateBoard(tofu, board, board.Name);
            if (created == null)
                continue;

            var rootIndex = created->Index;
            var slot = FindNamedFolder(tofu, folderName);
            var copy = slot == null
                ? null
                : TryCopyBoardToFolder(tofu, created, slot.Value);
            if (copy != null)
                TryDeleteRootBoard(tofu, rootIndex);
            restored++;
        }

        return restored;
    }

    private static bool TryDeleteNamedFolder(TofuModule* tofu, string name)
    {
        var slot = FindNamedFolder(tofu, name);
        if (slot == null)
            return false;

        var mixed = (uint)slot.Value.PositionInList;
        var realBefore = CountRealFolders(tofu);
        var deleted = tofu->DeleteItemAndContents(TofuType.Saved, mixed);
        if (!deleted && mixed != slot.Value.UiIndex)
            deleted = tofu->DeleteItemAndContents(TofuType.Saved, slot.Value.UiIndex);

        if (!deleted)
            return false;

        return CountRealFolders(tofu) == realBefore - 1;
    }

    private static bool TryDeleteMatchingRootBoard(TofuModule* tofu, DecodedBoard target)
    {
        var data = tofu->SavedBoardData;
        if (data == null)
            return false;

        var span = data->Boards;
        var max = Math.Min(span.Length, data->MaxCount);
        for (var i = 0; i < max; i++)
        {
            if (!span[i].IsValid || !BoardsMatch(span[i], target))
                continue;

            var entry = FindFolderEntryByIndex(tofu, span[i].Folder);
            if (entry != null && entry->IsValid && !entry->IsBoard)
                continue;

            return TryDeleteRootBoard(tofu, span[i].Index);
        }

        return false;
    }

    private static bool BoardsMatch(in TofuBoardEntry live, DecodedBoard target)
    {
        if (!string.Equals(live.NameString, target.Name, StringComparison.Ordinal))
            return false;
        if (live.Background != target.Background)
            return false;
        if (live.NumberOfObjects != target.Objects.Count)
            return false;
        if (target.Objects.Count == 0)
            return true;

        var first = live.Objects[0];
        var want = target.Objects[0];
        return (ushort)first.ObjectType == want.Type && first.PosX == want.X && first.PosY == want.Y;
    }

    private static DecodedBoard CopyBoard(in TofuBoardEntry entry)
    {
        var board = new DecodedBoard
        {
            Name = entry.NameString,
            Background = entry.Background,
        };

        var count = Math.Min((int)entry.NumberOfObjects, entry.Objects.Length);
        for (var i = 0; i < count; i++)
        {
            var src = entry.Objects[i];
            board.Objects.Add(new DecodedObject
            {
                Type = (ushort)src.ObjectType,
                X = src.PosX,
                Y = src.PosY,
                Angle = src.Angle,
                ArgsA = src.ArgsA,
                ArgsB = src.ArgsB,
                ArgsC = src.ArgsC,
                Scale = src.Scale,
                R = src.RGBA.R,
                G = src.RGBA.G,
                B = src.RGBA.B,
                A = src.RGBA.A,
                Flags = (ushort)src.Flags,
                Text = src.TextString,
            });
        }

        return board;
    }
}
