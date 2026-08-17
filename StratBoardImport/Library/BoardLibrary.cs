using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using StratBoardImport.Localization;

namespace StratBoardImport;

public sealed class LibraryBoard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public sealed class LibraryFolder
{
    public string Name { get; set; } = string.Empty;
    public List<LibraryBoard> Boards { get; set; } = [];
}

public sealed class LibraryPack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<LibraryFolder> Folders { get; set; } = [];
    public List<LibraryBoard> Boards { get; set; } = [];

    [JsonIgnore]
    public int BoardCount
        => Folders.Sum(f => f.Boards.Count) + Boards.Count;
}

public sealed class BoardLibrary
{
    public const string PackFormat = "strategy-board-plus-pack";
    public const string LibraryFormat = "strategy-board-plus-library";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string filePath;

    public List<LibraryPack> Packs { get; } = [];

    public BoardLibrary(string directory)
    {
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "library.json");
        Load();
    }

    public void Save()
    {
        var file = new LibraryFile
        {
            Format = LibraryFormat,
            Version = 1,
            Packs = Packs,
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    public LibraryPack AddPackFromGame(string name, SavedListSnapshot snapshot, HashSet<string> checkedIds)
    {
        var pack = new LibraryPack
        {
            Name = string.IsNullOrWhiteSpace(name) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : name.Trim(),
        };

        foreach (var folder in snapshot.Folders)
        {
            var boards = folder.Boards
                .Where(b => checkedIds.Contains(GameBoardId(b.Index)))
                .Select(ToLibraryBoard)
                .ToList();
            if (boards.Count == 0)
                continue;
            pack.Folders.Add(new LibraryFolder { Name = folder.Name, Boards = boards });
        }

        foreach (var board in snapshot.RootBoards)
        {
            if (!checkedIds.Contains(GameBoardId(board.Index)))
                continue;
            pack.Boards.Add(ToLibraryBoard(board));
        }

        if (pack.BoardCount == 0)
            throw new InvalidOperationException(Loc.Get(L.LibraryNoneChecked));

        Packs.Add(pack);
        Save();
        return pack;
    }

    public void RemoveChecked(HashSet<string> checkedIds)
    {
        foreach (var pack in Packs.ToList())
        {
            if (checkedIds.Contains(PackId(pack.Id)))
            {
                Packs.Remove(pack);
                continue;
            }

            pack.Folders.RemoveAll(folder =>
            {
                if (checkedIds.Contains(PackFolderId(pack.Id, folder.Name)))
                    return true;
                folder.Boards.RemoveAll(b => checkedIds.Contains(PackBoardId(pack.Id, b.Id)));
                return folder.Boards.Count == 0;
            });
            pack.Boards.RemoveAll(b => checkedIds.Contains(PackBoardId(pack.Id, b.Id)));
            if (pack.BoardCount == 0)
                Packs.Remove(pack);
        }

        Save();
    }

    public IReadOnlyList<(string Folder, LibraryBoard Board)> CollectChecked(HashSet<string> checkedIds)
    {
        var result = new List<(string Folder, LibraryBoard Board)>();
        foreach (var pack in Packs)
        {
            var packChecked = checkedIds.Contains(PackId(pack.Id));
            foreach (var folder in pack.Folders)
            {
                var folderChecked = packChecked || checkedIds.Contains(PackFolderId(pack.Id, folder.Name));
                foreach (var board in folder.Boards)
                {
                    if (folderChecked || checkedIds.Contains(PackBoardId(pack.Id, board.Id)))
                        result.Add((folder.Name, board));
                }
            }

            foreach (var board in pack.Boards)
            {
                if (packChecked || checkedIds.Contains(PackBoardId(pack.Id, board.Id)))
                    result.Add((string.Empty, board));
            }
        }

        return result;
    }

    public string ExportJson(HashSet<string> checkedIds)
    {
        var selected = Packs
            .Select(pack => FilterPack(pack, checkedIds))
            .Where(pack => pack != null)
            .Cast<LibraryPack>()
            .ToList();

        if (selected.Count == 0)
            throw new InvalidOperationException(Loc.Get(L.LibraryNoneChecked));

        if (selected.Count == 1)
            return JsonSerializer.Serialize(ToPackFile(selected[0]), JsonOptions);

        return JsonSerializer.Serialize(
            new LibraryFile { Format = LibraryFormat, Version = 1, Packs = selected },
            JsonOptions);
    }

    public int ImportJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var format = root.TryGetProperty("format", out var formatEl) ? formatEl.GetString() : null;
        var added = 0;

        if (string.Equals(format, LibraryFormat, StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("packs", out var packsEl))
        {
            var file = JsonSerializer.Deserialize<LibraryFile>(json, JsonOptions);
            if (file?.Packs == null)
                return 0;
            foreach (var pack in file.Packs)
                added += AddImportedPack(pack);
        }
        else
        {
            var pack = JsonSerializer.Deserialize<PackFile>(json, JsonOptions);
            if (pack == null)
                return 0;
            added += AddImportedPack(FromPackFile(pack));
        }

        if (added > 0)
            Save();
        return added;
    }

    private int AddImportedPack(LibraryPack pack)
    {
        NormalizePack(pack);
        if (pack.BoardCount == 0)
            return 0;
        pack.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(pack.Name))
            pack.Name = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        Packs.Add(pack);
        return pack.BoardCount;
    }

    private static void NormalizePack(LibraryPack pack)
    {
        foreach (var folder in pack.Folders)
        {
            foreach (var board in folder.Boards)
                NormalizeBoard(board);
        }

        foreach (var board in pack.Boards)
            NormalizeBoard(board);
    }

    private static void NormalizeBoard(LibraryBoard board)
    {
        if (string.IsNullOrWhiteSpace(board.Id))
            board.Id = Guid.NewGuid().ToString("N");
        board.Code = board.Code?.Trim() ?? string.Empty;
        if (!board.Code.StartsWith('[') && board.Code.Contains("stgy:", StringComparison.OrdinalIgnoreCase))
            board.Code = $"[{board.Code.TrimEnd(']')}]";
    }

    private LibraryPack? FilterPack(LibraryPack pack, HashSet<string> checkedIds)
    {
        var packChecked = checkedIds.Contains(PackId(pack.Id));
        var copy = new LibraryPack
        {
            Id = pack.Id,
            Name = pack.Name,
            Description = pack.Description,
        };

        foreach (var folder in pack.Folders)
        {
            var folderChecked = packChecked || checkedIds.Contains(PackFolderId(pack.Id, folder.Name));
            var boards = folder.Boards
                .Where(b => folderChecked || checkedIds.Contains(PackBoardId(pack.Id, b.Id)))
                .ToList();
            if (boards.Count == 0)
                continue;
            copy.Folders.Add(new LibraryFolder { Name = folder.Name, Boards = boards });
        }

        copy.Boards.AddRange(pack.Boards.Where(b => packChecked || checkedIds.Contains(PackBoardId(pack.Id, b.Id))));
        return copy.BoardCount == 0 ? null : copy;
    }

    private void Load()
    {
        Packs.Clear();
        if (!File.Exists(filePath))
            return;

        try
        {
            var json = File.ReadAllText(filePath);
            var file = JsonSerializer.Deserialize<LibraryFile>(json, JsonOptions);
            if (file?.Packs == null)
                return;
            foreach (var pack in file.Packs)
            {
                NormalizePack(pack);
                Packs.Add(pack);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[SBI] Could not read the board library file.");
        }
    }

    private static LibraryBoard ToLibraryBoard(SavedBoardInfo info)
    {
        return new LibraryBoard
        {
            Name = info.Name,
            Code = TofuImporter.EncodeBoard(info.Board),
        };
    }

    private static PackFile ToPackFile(LibraryPack pack) => new()
    {
        Format = PackFormat,
        Version = 1,
        Name = pack.Name,
        Description = pack.Description,
        Folders = pack.Folders,
        Boards = pack.Boards.Count > 0 ? pack.Boards : null,
    };

    private static LibraryPack FromPackFile(PackFile file) => new()
    {
        Name = file.Name ?? string.Empty,
        Description = file.Description ?? string.Empty,
        Folders = file.Folders ?? [],
        Boards = file.Boards ?? [],
    };

    public static string GameFolderId(uint uiIndex) => $"game:folder:{uiIndex}";
    public static string GameBoardId(byte index) => $"game:board:{index}";
    public static string GameRootId() => "game";
    public static string PackId(string packId) => $"lib:pack:{packId}";
    public static string PackFolderId(string packId, string folder) => $"lib:pack:{packId}:folder:{folder}";
    public static string PackBoardId(string packId, string boardId) => $"lib:pack:{packId}:board:{boardId}";

    private sealed class LibraryFile
    {
        public string Format { get; set; } = LibraryFormat;
        public int Version { get; set; } = 1;
        public List<LibraryPack> Packs { get; set; } = [];
    }

    private sealed class PackFile
    {
        public string Format { get; set; } = PackFormat;
        public int Version { get; set; } = 1;
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<LibraryFolder>? Folders { get; set; }
        public List<LibraryBoard>? Boards { get; set; }
    }
}
