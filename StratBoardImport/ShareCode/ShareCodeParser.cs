using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StratBoardImport;

public static partial class ShareCodeParser
{
    [GeneratedRegex(@"\[stgy:[^\]]+\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracketedCodeRegex();

    [GeneratedRegex(@"stgy:[A-Za-z0-9\-_+/]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareCodeRegex();

    public static IReadOnlyList<ParsedShareCode> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var decoded = DecodeUriFragments(input.Trim());
        var codes = ExtractCodes(decoded);
        if (codes.Count == 0)
            return [];

        var results = new List<ParsedShareCode>(codes.Count);
        foreach (var code in codes)
        {
            try
            {
                var binary = ShareCodeCodec.Decode(code);
                var (name, objectCount) = ShareCodeCodec.ReadSummary(binary);
                results.Add(new ParsedShareCode
                {
                    Code = code,
                    IsValid = true,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name,
                    ObjectCount = objectCount,
                });
            }
            catch (Exception ex)
            {
                // The game is the source of truth. A well-formed [stgy:] string is still importable
                // even if our decoder cannot read the name.
                results.Add(new ParsedShareCode
                {
                    Code = code,
                    IsValid = true,
                    Error = ex.Message,
                });
            }
        }

        return results;
    }

    private static string DecodeUriFragments(string input)
    {
        var current = input.Replace("\r", string.Empty);
        for (var i = 0; i < 3; i++)
        {
            try
            {
                var next = Uri.UnescapeDataString(current);
                if (next == current)
                    break;
                current = next;
            }
            catch (UriFormatException)
            {
                break;
            }
        }

        return current;
    }

    private static List<string> ExtractCodes(string input)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var codes = new List<string>();

        foreach (Match match in BracketedCodeRegex().Matches(input))
            Add(Normalize(match.Value));

        if (codes.Count == 0)
        {
            foreach (Match match in BareCodeRegex().Matches(input))
                Add(Normalize(match.Value));
        }

        if (codes.Count == 0)
        {
            var compact = Compact(input);
            if (compact.Contains("stgy:", StringComparison.OrdinalIgnoreCase))
                Add(Normalize(compact));
        }

        return codes;

        void Add(string code)
        {
            if (code.Length < 10)
                return;
            if (seen.Add(code))
                codes.Add(code);
        }
    }

    private static string Normalize(string value)
    {
        var compact = Compact(value);
        if (!compact.StartsWith("[", StringComparison.Ordinal))
            compact = $"[{compact}";
        if (!compact.EndsWith("]", StringComparison.Ordinal))
            compact += "]";
        return compact;
    }

    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }
}
