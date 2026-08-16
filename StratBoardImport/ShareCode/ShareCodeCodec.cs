using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace StratBoardImport;

/// <summary>
/// Encoder/decoder for official FFXIV Strategy Board share codes ([stgy:...]).
/// Ported from the community codec used by xiv-strat-board / ff14-strategyboard-decode.
/// </summary>
public static class ShareCodeCodec
{
    public static byte[] Decode(string shareCode)
    {
        var code = Unwrap(shareCode);
        if (code.Length < 2)
            throw new InvalidDataException("Share code is too short.");

        var substituted = new char[code.Length];
        for (var i = 0; i < code.Length; i++)
            substituted[i] = SubstituteDecode(code[i]);

        var seed = CharToValue(substituted[0]);
        var deobfuscated = new char[substituted.Length - 1];
        for (var i = 0; i < deobfuscated.Length; i++)
        {
            var value = (CharToValue(substituted[i + 1]) - i - seed) & 0x3F;
            deobfuscated[i] = ValueToChar(value);
        }

        var base64 = new string(deobfuscated).Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Share code is not valid Base64.", ex);
        }

        if (raw.Length < 8)
            throw new InvalidDataException("Share code does not contain enough data.");

        try
        {
            return Inflate(raw.AsSpan(6));
        }
        catch (InvalidDataException ex)
        {
            var storedCrc = BitConverter.ToUInt32(raw, 0);
            var calculatedCrc = Crc32(raw.AsSpan(4));
            if (storedCrc != calculatedCrc)
            {
                throw new InvalidDataException(
                    $"Share code could not be unpacked (CRC 0x{storedCrc:X8} != 0x{calculatedCrc:X8}).",
                    ex);
            }

            throw new InvalidDataException("Share code could not be unpacked.", ex);
        }
    }

    public static (string Name, int ObjectCount) ReadSummary(ReadOnlySpan<byte> binary)
    {
        var name = string.Empty;
        var objectCount = 0;
        if (binary.Length < 28)
            return (name, objectCount);

        var offset = 24;
        if (offset + 4 > binary.Length)
            return (name, objectCount);

        var sectionId = BitConverter.ToUInt16(binary[offset..]);
        var nameLength = BitConverter.ToUInt16(binary[(offset + 2)..]);
        offset += 4;
        if (sectionId == 1 && nameLength > 0 && offset + nameLength <= binary.Length)
        {
            name = Encoding.UTF8.GetString(binary.Slice(offset, nameLength)).TrimEnd('\0');
            offset += Align4(nameLength);
        }

        // Object entries are uint16 magic=2 followed by uint16 type id.
        var remaining = binary[Math.Min(offset, binary.Length)..];
        for (var i = 0; i + 4 <= remaining.Length; i += 4)
        {
            var magic = BitConverter.ToUInt16(remaining[i..]);
            if (magic != 2)
                break;
            objectCount++;
            var typeId = BitConverter.ToUInt16(remaining[(i + 2)..]);
            if (typeId == 100 && i + 8 <= remaining.Length)
            {
                var textLength = BitConverter.ToUInt16(remaining[(i + 6)..]);
                i += 4 + Align4(textLength);
                i -= 4;
            }
        }

        return (name, objectCount);
    }

    private static string Unwrap(string shareCode)
    {
        var value = shareCode.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
            value = value[1..^1];

        const string prefix = "stgy:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Not a Strategy Board share code ([stgy:...]).");

        value = value[prefix.Length..];
        if (value.Length == 0)
            throw new InvalidDataException("Share code has no payload.");

        // First character after "stgy:" is the version letter (normally 'a').
        if (char.IsLetter(value[0]))
            value = value[1..];

        return value;
    }

    /// <summary>
    /// Decode substitution used by the game / Ennea / stgy-tools.
    /// The 256-byte blob's DEC half is not indexed as ASCII+128; this map is.
    /// </summary>
    private static readonly Dictionary<char, char> DecodeSubstitution = new()
    {
        ['+'] = 'N', ['-'] = 'P', ['0'] = 'x', ['1'] = 'g', ['2'] = '0', ['3'] = 'K',
        ['4'] = '8', ['5'] = 'S', ['6'] = 'J', ['7'] = '2', ['8'] = 's', ['9'] = 'Z',
        ['A'] = 'D', ['B'] = 'F', ['C'] = 't', ['D'] = 'T', ['E'] = '6', ['F'] = 'E',
        ['G'] = 'a', ['H'] = 'V', ['I'] = 'c', ['J'] = 'p', ['K'] = 'L', ['L'] = 'M',
        ['M'] = 'm', ['N'] = 'e', ['O'] = 'j', ['P'] = '9', ['Q'] = 'X', ['R'] = 'B',
        ['S'] = '4', ['T'] = 'R', ['U'] = 'Y', ['V'] = '7', ['W'] = '_', ['X'] = 'n',
        ['Y'] = 'O', ['Z'] = 'b', ['a'] = 'i', ['b'] = '-', ['c'] = 'v', ['d'] = 'H',
        ['e'] = 'C', ['f'] = 'A', ['g'] = 'r', ['h'] = 'W', ['i'] = 'o', ['j'] = 'd',
        ['k'] = 'I', ['l'] = 'q', ['m'] = 'h', ['n'] = 'U', ['o'] = 'l', ['p'] = 'k',
        ['q'] = '3', ['r'] = 'f', ['s'] = 'y', ['t'] = '5', ['u'] = 'G', ['v'] = 'w',
        ['w'] = '1', ['x'] = 'u', ['y'] = 'z', ['z'] = 'Q',
    };

    private static char SubstituteDecode(char character)
        => DecodeSubstitution.TryGetValue(character, out var mapped) ? mapped : character;

    private static int CharToValue(char character)
    {
        if (character is >= 'A' and <= 'Z')
            return character - 'A';
        if (character is >= 'a' and <= 'z')
            return character - 71;
        if (character is >= '0' and <= '9')
            return character + 4;
        return character switch
        {
            '-' => 62,
            '_' => 63,
            _ => 0,
        };
    }

    private static char ValueToChar(int value)
    {
        value &= 0x3F;
        if (value < 26)
            return (char)('A' + value);
        if (value < 52)
            return (char)('a' + value - 26);
        if (value < 62)
            return (char)('0' + value - 52);
        return value == 62 ? '-' : '_';
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var mask = (crc & 1) != 0 ? 0xEDB88320u : 0;
                crc = (crc >> 1) ^ mask;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static int Align4(int length) => (length + 3) & ~3;
}
