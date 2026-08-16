using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;

namespace StratBoardImport;

public sealed class FolderImportJob
{
    public const int MaxBoardsPerFolder = 10;
    public const int MaxSavedBoards = 50;

    private enum Phase
    {
        Idle,
        WaitingForShareCodeWindow,
        WaitingForWindowClose,
        Done,
        Failed,
        Cancelled,
    }

    private readonly List<ParsedShareCode> queue = [];
    private Phase phase = Phase.Idle;
    private int index;
    private int totalCount;
    private string lastWindowName = string.Empty;
    private DateTime waitUntilUtc = DateTime.MinValue;

    public string FolderName { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public bool HasError { get; private set; }
    public bool IsRunning => phase is Phase.WaitingForShareCodeWindow or Phase.WaitingForWindowClose;
    public int CurrentIndex => index;
    public int TotalCount => totalCount;
    public float ProgressFraction
    {
        get
        {
            if (totalCount <= 0)
                return 0;
            var completed = phase == Phase.WaitingForWindowClose ? index + 1 : index;
            return completed / (float)totalCount;
        }
    }
    public string ProgressLabel
    {
        get
        {
            var completed = phase == Phase.WaitingForWindowClose ? index + 1 : index;
            return $"{completed}/{totalCount}";
        }
    }

    public static string DefaultFolderName(IEnumerable<ParsedShareCode> codes)
    {
        var named = codes.FirstOrDefault(c => c.IsValid && !string.IsNullOrWhiteSpace(c.Name));
        return string.IsNullOrWhiteSpace(named?.Name) ? "Import" : named.Name.Trim();
    }

    public bool Start(IEnumerable<ParsedShareCode> codes, string folderName)
    {
        if (IsRunning)
            Cancel();

        var valid = codes.Where(c => c.IsValid).ToList();
        if (valid.Count == 0)
        {
            Fail("No share codes to import.");
            return false;
        }

        if (valid.Count > MaxSavedBoards)
        {
            Plugin.ChatGui.PrintError(
                $"[SBI] The Saved List holds at most {MaxSavedBoards} boards. Importing the first {MaxSavedBoards}.");
            valid = valid.Take(MaxSavedBoards).ToList();
        }

        if (valid.Count > MaxBoardsPerFolder)
        {
            Plugin.ChatGui.Print(
                $"[SBI] {valid.Count} boards: the Saved List can hold them (max {MaxSavedBoards}). " +
                $"A folder holds only {MaxBoardsPerFolder} — after {MaxBoardsPerFolder} pages, open a second folder " +
                "or leave the rest in the Saved List.");
        }

        queue.Clear();
        queue.AddRange(valid);
        index = 0;
        totalCount = valid.Count;
        FolderName = string.IsNullOrWhiteSpace(folderName) ? "Import" : folderName.Trim();
        lastWindowName = string.Empty;
        waitUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
        phase = Phase.WaitingForShareCodeWindow;
        HasError = false;

        if (!Plugin.Instance.Importer.IsAnyStrategyBoardUiOpen())
            Plugin.Instance.Importer.OpenStrategyBoard();

        ImGui.SetClipboardText(FolderName);

        Status = totalCount > MaxBoardsPerFolder
            ? $"Importing {totalCount} boards. Create folder \"{FolderName}\" (name is on the clipboard). " +
              $"A folder holds {MaxBoardsPerFolder} pages — then open a second folder or leave the rest in the Saved List. " +
              $"Then New Strategy → Share Code. Page {index + 1}/{totalCount} will be filled automatically."
            : $"Create or open folder \"{FolderName}\" (name is on the clipboard). " +
              $"Then New Strategy → Share Code. Page {index + 1}/{totalCount} will be filled automatically.";
        Plugin.ChatGui.Print($"[SBI] {Status}");
        return true;
    }

    public void Cancel()
    {
        phase = Phase.Cancelled;
        Status = "Folder import cancelled.";
        HasError = false;
        queue.Clear();
    }

    public void Tick()
    {
        if (!IsRunning)
            return;

        if (DateTime.UtcNow < waitUntilUtc)
            return;

        switch (phase)
        {
            case Phase.WaitingForShareCodeWindow:
                WaitForWindow();
                break;
            case Phase.WaitingForWindowClose:
                WaitForClose();
                break;
        }
    }

    private void WaitForWindow()
    {
        if (!Plugin.Instance.Importer.IsShareCodeWindowOpen(out var addonName))
            return;

        if (addonName == lastWindowName)
            return;

        var code = queue[index];
        var result = Plugin.Instance.Importer.Import(
            code.Code,
            autoConfirm: true,
            Plugin.Instance.Configuration.ConfirmCallbackId,
            requireShareCodeDialog: true);
        if (!result.Success)
        {
            Fail(result.Message);
            return;
        }

        lastWindowName = addonName;
        var title = string.IsNullOrEmpty(code.Name) ? $"Page {index + 1}" : code.Name;
        Status =
            $"Imported {index + 1}/{totalCount}: {title}. " +
            "If the window stays open, click OK in game. Then open New Strategy → Share Code again.";
        Plugin.ChatGui.Print($"[SBI] {Status}");
        waitUntilUtc = DateTime.UtcNow.AddMilliseconds(400);
        phase = Phase.WaitingForWindowClose;
    }

    private void WaitForClose()
    {
        if (Plugin.Instance.Importer.IsShareCodeWindowOpen(out _))
            return;

        lastWindowName = string.Empty;
        index++;
        if (index >= queue.Count)
        {
            phase = Phase.Done;
            Status = totalCount > MaxBoardsPerFolder
                ? $"Done: {totalCount} boards imported. Drag up to {MaxBoardsPerFolder} into folder \"{FolderName}\", " +
                  "and put the rest in a second folder or the Saved List."
                : $"Done: {totalCount} boards imported. If they are not in folder \"{FolderName}\", " +
                  "drag them there in Strategy Board.";
            Plugin.ChatGui.Print($"[SBI] {Status}");
            queue.Clear();
            return;
        }

        waitUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
        phase = Phase.WaitingForShareCodeWindow;
        Status = $"Waiting for the share-code window for page {index + 1}/{totalCount}.";
    }

    private void Fail(string message)
    {
        phase = Phase.Failed;
        HasError = true;
        Status = message;
        Plugin.ChatGui.PrintError($"[SBI] {message}");
        queue.Clear();
    }
}
