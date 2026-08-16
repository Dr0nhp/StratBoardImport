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
            Fail("Keine gültigen Share-Codes.");
            return false;
        }

        if (valid.Count > MaxSavedBoards)
        {
            Plugin.ChatGui.PrintError(
                $"[SBI] Die Saved List darf nur {MaxSavedBoards} Boards enthalten. Es werden die ersten {MaxSavedBoards} importiert.");
            valid = valid.Take(MaxSavedBoards).ToList();
        }

        if (valid.Count > MaxBoardsPerFolder)
        {
            Plugin.ChatGui.Print(
                $"[SBI] {valid.Count} Boards: die Saved List schafft das (max. {MaxSavedBoards}). " +
                $"Ein Ordner fasst nur {MaxBoardsPerFolder} — nach {MaxBoardsPerFolder} Seiten einen zweiten Ordner öffnen " +
                "oder den Rest in der Saved List lassen.");
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
            ? $"Import von {totalCount} Boards. Ordner „{FolderName}“ anlegen (Name in der Zwischenablage). " +
              $"Ein Ordner fasst {MaxBoardsPerFolder} Seiten — danach zweiten Ordner öffnen oder in der Saved List lassen. " +
              $"Dann Neue Strategie → Share-Code. Seite {index + 1}/{totalCount} wird automatisch eingefügt."
            : $"Ordner „{FolderName}“ anlegen oder öffnen (Name liegt in der Zwischenablage). " +
              $"Dann Neue Strategie → Share-Code. Seite {index + 1}/{totalCount} wird automatisch eingefügt.";
        Plugin.ChatGui.Print($"[SBI] {Status}");
        return true;
    }

    public void Cancel()
    {
        phase = Phase.Cancelled;
        Status = "Ordner-Import abgebrochen.";
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
        var title = string.IsNullOrEmpty(code.Name) ? $"Seite {index + 1}" : code.Name;
        Status =
            $"Importiert {index + 1}/{totalCount}: {title}. " +
            "Wenn das Fenster offen bleibt, im Spiel auf OK klicken. Danach erneut Neue Strategie → Share-Code.";
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
                ? $"Fertig: {totalCount} Boards importiert. Maximal {MaxBoardsPerFolder} davon in den Ordner „{FolderName}“ ziehen, " +
                  "den Rest in einen zweiten Ordner oder in der Saved List lassen."
                : $"Fertig: {totalCount} Boards importiert. Falls sie nicht im Ordner „{FolderName}“ liegen, " +
                  "im Strategy Board in diesen Ordner verschieben.";
            Plugin.ChatGui.Print($"[SBI] {Status}");
            queue.Clear();
            return;
        }

        waitUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
        phase = Phase.WaitingForShareCodeWindow;
        Status = $"Warte auf Share-Code-Fenster für Seite {index + 1}/{totalCount}.";
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
