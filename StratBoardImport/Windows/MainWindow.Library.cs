using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using StratBoardImport.Localization;

namespace StratBoardImport.Windows;

public sealed partial class MainWindow
{
    private readonly HashSet<string> libraryChecked = new(StringComparer.Ordinal);
    private string libraryPackName = string.Empty;
    private bool confirmLibraryDeleteGame;
    private bool confirmLibraryRemove;

    private void DrawLibraryTab()
    {
        ImGui.TextDisabled(Loc.Get(L.UiLibraryHeader));
        ImGuiHelpers.ScaledDummy(4);

        var snapshot = TofuImporter.IsAvailable ? TofuImporter.ReadSavedList() : new SavedListSnapshot();
        PruneLibraryChecks(snapshot);

        var gameCount = CountGameChecked(snapshot);
        var pluginCount = plugin.Library.CollectChecked(libraryChecked).Count;
        ImGui.Text(Loc.Format(L.UiSavedListCount, snapshot.BoardCount, FolderImportJob.MaxSavedBoards));
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.Format(L.UiLibraryChecked, gameCount, pluginCount));

        var treeHeight = Math.Max(180 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().Y - 150 * ImGuiHelpers.GlobalScale);
        using (var child = ImRaii.Child("library-tree", new Vector2(-1, treeHeight), true))
        {
            if (child.Success)
            {
                if (!TofuImporter.IsAvailable)
                    ImGui.TextWrapped(Loc.Get(L.UiLibraryUnavailable));
                else
                    DrawGameTree(snapshot);

                ImGui.Separator();
                DrawPluginTree();
            }
        }

        ImGuiHelpers.ScaledDummy(6);
        ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
        ImGui.InputText(Loc.Get(L.UiLibraryPackName), ref libraryPackName, 64);

        DrawLibraryActions(snapshot, gameCount, pluginCount);
        ImGuiHelpers.ScaledDummy(6);
        DrawStatus();
    }

    private void DrawGameTree(SavedListSnapshot snapshot)
    {
        var ids = new List<string> { BoardLibrary.GameRootId() };
        ids.AddRange(snapshot.Folders.Select(f => BoardLibrary.GameFolderId(f.UiIndex)));
        ids.AddRange(snapshot.Folders.SelectMany(f => f.Boards.Select(b => BoardLibrary.GameBoardId(b.Index))));
        ids.AddRange(snapshot.RootBoards.Select(b => BoardLibrary.GameBoardId(b.Index)));

        var empty = snapshot.Folders.Count == 0 && snapshot.RootBoards.Count == 0;
        if (!DrawTreeCheckbox(
            BoardLibrary.GameRootId(),
            Loc.Get(L.UiLibrarySaved),
            ids,
            ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (empty)
        {
            ImGui.Indent();
            ImGui.TextDisabled(Loc.Get(L.UiLibraryEmptyGame));
            ImGui.Unindent();
            ImGui.TreePop();
            return;
        }

        foreach (var folder in snapshot.Folders)
        {
            var folderIds = new List<string> { BoardLibrary.GameFolderId(folder.UiIndex) };
            folderIds.AddRange(folder.Boards.Select(b => BoardLibrary.GameBoardId(b.Index)));
            var label = $"{folder.Name}  ({folder.Boards.Count}/{FolderImportJob.MaxBoardsPerFolder})";
            if (!DrawTreeCheckbox(BoardLibrary.GameFolderId(folder.UiIndex), label, folderIds, ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            foreach (var board in folder.Boards)
                DrawLeafCheckbox(BoardLibrary.GameBoardId(board.Index), board.DisplayName);
            ImGui.TreePop();
        }

        foreach (var board in snapshot.RootBoards)
            DrawLeafCheckbox(BoardLibrary.GameBoardId(board.Index), board.DisplayName);

        ImGui.TreePop();
    }

    private void DrawPluginTree()
    {
        var packIds = plugin.Library.Packs.Select(p => BoardLibrary.PackId(p.Id)).ToList();
        foreach (var pack in plugin.Library.Packs)
        {
            packIds.Add(BoardLibrary.PackId(pack.Id));
            foreach (var folder in pack.Folders)
            {
                packIds.Add(BoardLibrary.PackFolderId(pack.Id, folder.Name));
                packIds.AddRange(folder.Boards.Select(b => BoardLibrary.PackBoardId(pack.Id, b.Id)));
            }

            packIds.AddRange(pack.Boards.Select(b => BoardLibrary.PackBoardId(pack.Id, b.Id)));
        }

        var rootIds = new List<string> { "lib" };
        rootIds.AddRange(packIds);

        if (!DrawTreeCheckbox("lib", Loc.Get(L.UiLibraryPlugin), rootIds, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (plugin.Library.Packs.Count == 0)
        {
            ImGui.Indent();
            ImGui.TextDisabled(Loc.Get(L.UiLibraryEmptyPlugin));
            ImGui.Unindent();
            ImGui.TreePop();
            return;
        }

        foreach (var pack in plugin.Library.Packs)
        {
            var ids = new List<string> { BoardLibrary.PackId(pack.Id) };
            foreach (var folder in pack.Folders)
            {
                ids.Add(BoardLibrary.PackFolderId(pack.Id, folder.Name));
                ids.AddRange(folder.Boards.Select(b => BoardLibrary.PackBoardId(pack.Id, b.Id)));
            }

            ids.AddRange(pack.Boards.Select(b => BoardLibrary.PackBoardId(pack.Id, b.Id)));
            var packLabel = $"{pack.Name}  ({pack.BoardCount})";
            if (!DrawTreeCheckbox(BoardLibrary.PackId(pack.Id), packLabel, ids, ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            foreach (var folder in pack.Folders)
            {
                var folderIds = new List<string> { BoardLibrary.PackFolderId(pack.Id, folder.Name) };
                folderIds.AddRange(folder.Boards.Select(b => BoardLibrary.PackBoardId(pack.Id, b.Id)));
                var folderLabel = $"{folder.Name}  ({folder.Boards.Count})";
                if (!DrawTreeCheckbox(BoardLibrary.PackFolderId(pack.Id, folder.Name), folderLabel, folderIds, ImGuiTreeNodeFlags.None))
                    continue;

                foreach (var board in folder.Boards)
                {
                    var name = string.IsNullOrWhiteSpace(board.Name) ? Loc.Get(L.UiUnnamed) : board.Name;
                    DrawLeafCheckbox(BoardLibrary.PackBoardId(pack.Id, board.Id), name);
                }

                ImGui.TreePop();
            }

            foreach (var board in pack.Boards)
            {
                var name = string.IsNullOrWhiteSpace(board.Name) ? Loc.Get(L.UiUnnamed) : board.Name;
                DrawLeafCheckbox(BoardLibrary.PackBoardId(pack.Id, board.Id), name);
            }

            ImGui.TreePop();
        }

        ImGui.TreePop();
    }

    private bool DrawTreeCheckbox(string id, string label, List<string> subtreeIds, ImGuiTreeNodeFlags extra)
    {
        ImGui.PushID(id);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.FramePadding | extra;
        var open = ImGui.TreeNodeEx("##node", flags);
        ImGui.SameLine();
        DrawNodeCheckbox(id, label, subtreeIds);
        ImGui.PopID();
        return open;
    }

    private void DrawLeafCheckbox(string id, string label)
    {
        ImGui.PushID(id);
        ImGui.AlignTextToFramePadding();
        ImGui.Dummy(new Vector2(ImGui.GetTreeNodeToLabelSpacing(), 1));
        ImGui.SameLine();
        DrawNodeCheckbox(id, label, [id]);
        ImGui.PopID();
    }

    private void DrawNodeCheckbox(string id, string label, List<string> subtreeIds)
    {
        var total = subtreeIds.Count;
        var selected = subtreeIds.Count(libraryChecked.Contains);
        var allOn = selected == total && total > 0;
        var isChecked = selected > 0;
        var clicked = ImGui.Checkbox(label, ref isChecked);
        if (!clicked)
            return;

        if (allOn)
        {
            foreach (var node in subtreeIds)
                libraryChecked.Remove(node);
        }
        else
        {
            foreach (var node in subtreeIds)
                libraryChecked.Add(node);
        }
    }

    private void DrawLibraryActions(SavedListSnapshot snapshot, int gameCount, int pluginCount)
    {
        var busy = plugin.FolderJob.IsRunning;

        if (confirmLibraryDeleteGame)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.72f, 0.18f, 0.18f, 1f));
            using (ImRaii.Disabled(busy || gameCount == 0))
            {
                if (ImGui.Button(Loc.Format(L.UiLibraryDeleteGameConfirm, gameCount)))
                {
                    confirmLibraryDeleteGame = false;
                    DeleteCheckedInGame(snapshot);
                }
            }

            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button(Loc.Get(L.UiDeleteAllSavedCancel)))
                confirmLibraryDeleteGame = false;
        }
        else
        {
            using (ImRaii.Disabled(busy || gameCount == 0 || !TofuImporter.IsAvailable))
            {
                if (ImGui.Button(Loc.Get(L.UiLibraryDeleteGame)))
                    confirmLibraryDeleteGame = true;
            }

            Tooltip(L.UiLibraryDeleteGameHelp);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(busy || gameCount == 0 || !TofuImporter.IsAvailable))
        {
            if (ImGui.Button(Loc.Get(L.UiLibrarySavePlugin)))
                SaveCheckedToPlugin(snapshot);
        }

        Tooltip(L.UiLibrarySavePluginHelp);

        ImGui.SameLine();
        using (ImRaii.Disabled(busy || pluginCount == 0 || !TofuImporter.IsAvailable))
        {
            if (ImGui.Button(Loc.Get(L.UiLibrarySendGame)))
                SendCheckedToGame();
        }

        Tooltip(L.UiLibrarySendGameHelp);

        if (confirmLibraryRemove)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.72f, 0.18f, 0.18f, 1f));
            using (ImRaii.Disabled(pluginCount == 0))
            {
                if (ImGui.Button(Loc.Format(L.UiLibraryRemoveConfirm, pluginCount)))
                {
                    confirmLibraryRemove = false;
                    var before = plugin.Library.Packs.Sum(p => p.BoardCount);
                    plugin.Library.RemoveChecked(libraryChecked);
                    var removed = Math.Max(0, before - plugin.Library.Packs.Sum(p => p.BoardCount));
                    libraryChecked.RemoveWhere(id => id.StartsWith("lib:", StringComparison.Ordinal));
                    SetStatus(L.LibraryRemoved, false, removed);
                }
            }

            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button(Loc.Get(L.UiDeleteAllSavedCancel)))
                confirmLibraryRemove = false;
        }
        else
        {
            using (ImRaii.Disabled(pluginCount == 0))
            {
                if (ImGui.Button(Loc.Get(L.UiLibraryRemove)))
                    confirmLibraryRemove = true;
            }

            Tooltip(L.UiLibraryRemoveHelp);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(pluginCount == 0))
        {
            if (ImGui.Button(Loc.Get(L.UiLibraryExportJson)))
            {
                try
                {
                    ImGui.SetClipboardText(plugin.Library.ExportJson(libraryChecked));
                    SetStatus(L.LibraryExported, false, pluginCount);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "[SBI] JSON export failed.");
                    SetStatus(L.LibraryNoneChecked, true);
                }
            }
        }

        Tooltip(L.UiLibraryExportJsonHelp);

        ImGui.SameLine();
        if (ImGui.Button(Loc.Get(L.UiLibraryImportJson)))
        {
            try
            {
                var added = plugin.Library.ImportJson(ImGui.GetClipboardText() ?? string.Empty);
                if (added == 0)
                    SetStatus(L.LibraryImportFailed, true);
                else
                    SetStatus(L.LibraryImported, false, added);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[SBI] JSON import failed.");
                SetStatus(L.LibraryImportFailed, true);
            }
        }

        Tooltip(L.UiLibraryImportJsonHelp);
    }

    private void SaveCheckedToPlugin(SavedListSnapshot snapshot)
    {
        try
        {
            var pack = plugin.Library.AddPackFromGame(libraryPackName, snapshot, libraryChecked);
            if (string.IsNullOrWhiteSpace(libraryPackName))
                libraryPackName = pack.Name;
            SetStatus(L.LibrarySaved, false, pack.BoardCount, pack.Name);
            Plugin.ChatPrint(Loc.Format(L.LibrarySaved, pack.BoardCount, pack.Name));
        }
        catch (InvalidOperationException)
        {
            SetStatus(L.LibraryNoneChecked, true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[SBI] Save to plugin failed.");
            SetStatus(L.LibraryEncodeFailed, true, ex.Message);
        }
    }

    private void DeleteCheckedInGame(SavedListSnapshot snapshot)
    {
        var request = new SavedListDeleteRequest();
        foreach (var folder in snapshot.Folders)
        {
            var checkedBoards = folder.Boards.Where(b => libraryChecked.Contains(BoardLibrary.GameBoardId(b.Index))).ToList();
            var folderChecked = libraryChecked.Contains(BoardLibrary.GameFolderId(folder.UiIndex));
            if (folderChecked || (checkedBoards.Count > 0 && checkedBoards.Count == folder.Boards.Count))
                request.WipeFolders.Add(folder.Name);
            else if (checkedBoards.Count > 0)
            {
                request.KeepInFolder[folder.Name] = folder.Boards
                    .Where(b => !libraryChecked.Contains(BoardLibrary.GameBoardId(b.Index)))
                    .Select(b => b.Board)
                    .ToList();
            }
        }

        foreach (var board in snapshot.RootBoards)
        {
            if (libraryChecked.Contains(BoardLibrary.GameBoardId(board.Index)))
                request.WipeRootBoards.Add(board.Board);
        }

        var result = TofuImporter.DeleteSelection(request);
        SetStatus(result);
        if (result.Success)
        {
            Plugin.ChatPrint(result.Message);
            libraryChecked.RemoveWhere(id => id.StartsWith("game", StringComparison.Ordinal));
        }
        else
            Plugin.ChatPrintError(result.Message);
    }

    private void SendCheckedToGame()
    {
        var items = plugin.Library.CollectChecked(libraryChecked);
        if (items.Count == 0)
        {
            SetStatus(L.LibraryNoneChecked, true);
            return;
        }

        var sent = 0;
        ImportResult? last = null;
        foreach (var group in items.GroupBy(i => i.Folder, StringComparer.Ordinal))
        {
            var codes = new List<ParsedShareCode>();
            foreach (var (_, board) in group)
            {
                var parsed = ShareCodeParser.Parse(board.Code).FirstOrDefault(c => c.IsValid);
                if (parsed == null)
                {
                    SetStatus(L.LibraryEncodeFailed, true, board.Name);
                    return;
                }

                codes.Add(parsed);
            }

            var folder = string.IsNullOrWhiteSpace(group.Key) ? null : group.Key;
            last = TofuImporter.ImportMany(codes, folder);
            if (!last.Value.Success)
            {
                SetStatus(last.Value);
                Plugin.ChatPrintError(last.Value.Message);
                return;
            }

            sent += codes.Count;
        }

        SetStatus(L.LibrarySent, false, sent);
        Plugin.ChatPrint(Loc.Format(L.LibrarySent, sent));
        if (last is { Success: true } && sent == 1)
            SetStatus(last.Value);
    }

    private int CountGameChecked(SavedListSnapshot snapshot)
    {
        var count = snapshot.RootBoards.Count(b => libraryChecked.Contains(BoardLibrary.GameBoardId(b.Index)));
        foreach (var folder in snapshot.Folders)
        {
            if (libraryChecked.Contains(BoardLibrary.GameFolderId(folder.UiIndex)))
                count += folder.Boards.Count;
            else
                count += folder.Boards.Count(b => libraryChecked.Contains(BoardLibrary.GameBoardId(b.Index)));
        }

        return count;
    }

    private void PruneLibraryChecks(SavedListSnapshot snapshot)
    {
        var valid = new HashSet<string>(StringComparer.Ordinal)
        {
            BoardLibrary.GameRootId(),
            "lib",
        };
        foreach (var folder in snapshot.Folders)
        {
            valid.Add(BoardLibrary.GameFolderId(folder.UiIndex));
            foreach (var board in folder.Boards)
                valid.Add(BoardLibrary.GameBoardId(board.Index));
        }

        foreach (var board in snapshot.RootBoards)
            valid.Add(BoardLibrary.GameBoardId(board.Index));

        foreach (var pack in plugin.Library.Packs)
        {
            valid.Add(BoardLibrary.PackId(pack.Id));
            foreach (var folder in pack.Folders)
            {
                valid.Add(BoardLibrary.PackFolderId(pack.Id, folder.Name));
                foreach (var board in folder.Boards)
                    valid.Add(BoardLibrary.PackBoardId(pack.Id, board.Id));
            }

            foreach (var board in pack.Boards)
                valid.Add(BoardLibrary.PackBoardId(pack.Id, board.Id));
        }

        libraryChecked.RemoveWhere(id => !valid.Contains(id));
    }
}
