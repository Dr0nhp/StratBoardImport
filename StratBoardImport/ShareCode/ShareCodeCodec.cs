using System;
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
    private static readonly byte[] SubstitutionTable =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x62, 0x00, 0x00,
        0x32, 0x77, 0x37, 0x71, 0x53, 0x74, 0x45, 0x56, 0x34, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x66, 0x52, 0x65, 0x41, 0x46, 0x42, 0x75, 0x64, 0x6b, 0x36, 0x33, 0x4b, 0x4c, 0x2b, 0x59,
        0x2d, 0x7a, 0x54, 0x35, 0x44, 0x6e, 0x48, 0x68, 0x51, 0x55, 0x39, 0x00, 0x00, 0x00, 0x00, 0x57,
        0x00, 0x47, 0x5a, 0x49, 0x6a, 0x4e, 0x72, 0x31, 0x6d, 0x61, 0x4f, 0x70, 0x6f, 0x4d, 0x58, 0x69,
        0x4a, 0x6c, 0x67, 0x38, 0x43, 0x78, 0x63, 0x76, 0x30, 0x73, 0x79, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4e, 0x00, 0x50,
        0x00, 0x00, 0x78, 0x67, 0x30, 0x4b, 0x38, 0x53, 0x4a, 0x32, 0x73, 0x5a, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x44, 0x46, 0x74, 0x54, 0x36, 0x45, 0x61, 0x56, 0x63, 0x70, 0x4c, 0x4d, 0x6d,
        0x65, 0x6a, 0x39, 0x58, 0x42, 0x34, 0x52, 0x59, 0x37, 0x5f, 0x6e, 0x4f, 0x62, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x69, 0x2d, 0x76, 0x48, 0x43, 0x41, 0x72, 0x57, 0x6f, 0x64, 0x49, 0x71, 0x68,
        0x55, 0x6c, 0x6b, 0x33, 0x66, 0x79, 0x35, 0x47, 0x77, 0x31, 0x75, 0x7a, 0x51, 0x00, 0x00, 0x00,
        0x00, 0x00,
    ];

    public static byte[] Decode(string shareCode)
    {
        var code = Unwrap(shareCode);
        if (code.Length < 2)
            throw new InvalidDataException("Share-Code ist zu kurz.");

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
            throw new InvalidDataException("Share-Code ist kein gültiges Base64.", ex);
        }

        if (raw.Length < 8)
            throw new InvalidDataException("Share-Code enthält zu wenig Daten.");

        var storedCrc = BitConverter.ToUInt32(raw, 0);
        var calculatedCrc = Crc32(raw.AsSpan(4));
        if (storedCrc != calculatedCrc)
            throw new InvalidDataException($"CRC stimmt nicht (0x{storedCrc:X8} != 0x{calculatedCrc:X8}).");

        try
        {
            return Inflate(raw.AsSpan(6));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("Share-Code konnte nicht entpackt werden.", ex);
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
            throw new InvalidDataException("Kein Strategy-Board-Share-Code ([stgy:...]).");

        value = value[prefix.Length..];
        if (value.Length == 0)
            throw new InvalidDataException("Share-Code hat keine Nutzlast.");

        // First character after "stgy:" is the version letter (normally 'a').
        if (char.IsLetter(value[0]))
            value = value[1..];

        return value;
    }

    private static char SubstituteDecode(char character)
    {
        var index = (int)character;
        if (index is >= 0 and < 128)
        {
            var mapped = SubstitutionTable[128 + index];
            if (mapped != 0)
                return (char)mapped;
        }

        return character;
    }

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
