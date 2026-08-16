namespace StratBoardImport;

public sealed class ParsedShareCode
{
    public required string Code { get; init; }
    public bool IsValid { get; init; }
    public string? Name { get; init; }
    public int ObjectCount { get; init; }
    public string? Error { get; init; }
    public int Length => Code.Length;
}
