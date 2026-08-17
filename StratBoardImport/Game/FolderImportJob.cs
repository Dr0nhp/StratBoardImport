using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using StratBoardImport.Localization;

namespace StratBoardImport;

public sealed class FolderImportJob
{
    public const int MaxSavedBoards = 50;
    public const int MaxBoardsPerFolder = 10;

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
    private DateTime lastWaitHintUtc = DateTime.MinValue;

    private string statusKey = string.Empty;
    private object?[] statusArgs = [];
    private bool includeWaitingHint;
    private string? visibleAddons;

    public string FolderName { get; private set; } = string.Empty;
    public string Status
    {
        get
        {
            if (string.IsNullOrEmpty(statusKey))
                return string.Empty;

            var text = statusArgs is { Length: > 0 } ? Loc.Format(statusKey, statusArgs) : Loc.Get(statusKey);
            if (includeWaitingHint)
                text += " " + Loc.Get(L.FolderWaitingHint);
            if (!string.IsNullOrEmpty(visibleAddons))
                text += "\n" + Loc.Format(L.FolderVisibleAddons, visibleAddons);
            return text;
        }
    }
    public string StatusFingerprint => $"{statusKey}|{HasError}|{phase}|{index}|{includeWaitingHint}|{visibleAddons}";
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
            Fail(L.FolderNone);
            return false;
        }

        if (valid.Count > MaxSavedBoards)
        {
            Plugin.ChatPrintError(Loc.Format(L.FolderSavedListCap, MaxSavedBoards));
            valid = valid.Take(MaxSavedBoards).ToList();
        }

        queue.Clear();
        queue.AddRange(valid);
        index = 0;
        totalCount = valid.Count;
        FolderName = string.IsNullOrWhiteSpace(folderName) ? "Import" : folderName.Trim();
        lastWindowName = string.Empty;
        lastWaitHintUtc = DateTime.MinValue;
        HasError = false;
        includeWaitingHint = false;
        visibleAddons = null;

        var native = TofuImporter.ImportMany(valid, FolderName);
        if (native.Success)
        {
            phase = Phase.Done;
            SetStatus(native);
            Plugin.ChatPrint(Status);
            queue.Clear();
            return true;
        }

        Plugin.Log.Warning($"[SBI] Direct Tofu import did not run ({native.Message}). Falling back to the share-code window.");

        waitUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
        phase = Phase.WaitingForShareCodeWindow;

        if (!Plugin.Instance.Importer.IsAnyStrategyBoardUiOpen())
            Plugin.Instance.Importer.OpenStrategyBoard();

        ImGui.SetClipboardText(FolderName);
        SetStatus(L.FolderStart, FolderName, index + 1, totalCount);
        Plugin.ChatPrint(Status);
        return true;
    }

    public void Cancel()
    {
        phase = Phase.Cancelled;
        SetStatus(L.FolderCancelled);
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
        {
            UpdateWaitingHint();
            return;
        }

        if (addonName == lastWindowName)
            return;

        var code = queue[index];
        var result = Plugin.Instance.Importer.Import(code.Code, requireShareCodeDialog: true);
        if (!result.Success)
        {
            Fail(result);
            return;
        }

        lastWindowName = addonName;
        var title = string.IsNullOrEmpty(code.Name) ? Loc.Format(L.FolderPage, index + 1) : code.Name;
        SetStatus(L.FolderImported, index + 1, totalCount, title);
        Plugin.ChatPrint(Status);
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
            SetStatus(L.FolderDone, totalCount, FolderName);
            Plugin.ChatPrint(Status);
            queue.Clear();
            return;
        }

        waitUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
        lastWaitHintUtc = DateTime.MinValue;
        phase = Phase.WaitingForShareCodeWindow;
        SetStatus(L.FolderWaiting, index + 1, totalCount);
    }

    private void UpdateWaitingHint()
    {
        if (DateTime.UtcNow - lastWaitHintUtc < TimeSpan.FromSeconds(1))
            return;

        lastWaitHintUtc = DateTime.UtcNow;
        SetStatus(L.FolderWaiting, index + 1, totalCount);
        includeWaitingHint = true;
        var addons = Plugin.Instance.Importer.ListVisibleTextInputNames();
        visibleAddons = addons.Count > 0 ? string.Join(", ", addons) : null;
    }

    private void Fail(ImportResult result) => Fail(result.Key, result.Args);

    private void Fail(string key, params object?[] args)
    {
        phase = Phase.Failed;
        HasError = true;
        SetStatus(key, args);
        Plugin.ChatPrintError(Status);
        queue.Clear();
    }

    private void SetStatus(ImportResult result) => SetStatus(result.Key, result.Args);

    private void SetStatus(string key, params object?[] args)
    {
        statusKey = key;
        statusArgs = args;
        includeWaitingHint = false;
        visibleAddons = null;
    }
}
