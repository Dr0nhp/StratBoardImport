using System;
using System.Collections.Generic;
using System.Text;

namespace StratBoardImport;

/// <summary>
/// Walks the decompressed Strategy Board TLV (stgy.hexpat) into objects Tofu can store.
/// </summary>
public sealed class DecodedBoard
{
    public string Name { get; set; } = string.Empty;
    public ushort Background { get; set; }
    public List<DecodedObject> Objects { get; } = [];
}

public sealed class DecodedObject
{
    public ushort Type { get; set; }
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public ushort Angle { get; set; }
    public ushort ArgsA { get; set; }
    public ushort ArgsB { get; set; }
    public ushort ArgsC { get; set; }
    public byte Scale { get; set; } = 100;
    public byte R { get; set; } = 255;
    public byte G { get; set; } = 255;
    public byte B { get; set; } = 255;
    public byte A { get; set; }
    public ushort Flags { get; set; } = 1;
    public string Text { get; set; } = string.Empty;
}

public static class ShareCodeBoard
{
    private const ushort FieldEmpty = 0;
    private const ushort FieldTitle = 1;
    private const ushort FieldAllocate = 2;
    private const ushort FieldLabel = 3;
    private const ushort FieldFlags = 4;
    private const ushort FieldPosition = 5;
    private const ushort FieldAngle = 6;
    private const ushort FieldScale = 7;
    private const ushort FieldColours = 8;
    private const ushort FieldArgsAB = 9;
    private const ushort FieldArgsA = 10;
    private const ushort FieldArgsB = 11;
    private const ushort FieldArgsC = 12;

    public static DecodedBoard Parse(string shareCode)
    {
        var binary = ShareCodeCodec.Decode(shareCode);
        return ParseBinary(binary);
    }

    public static DecodedBoard ParseBinary(ReadOnlySpan<byte> binary)
    {
        var board = new DecodedBoard();
        if (binary.Length < 20)
            return board;

        var offset = 16;
        while (offset + 4 <= binary.Length)
        {
            var sectionType = ReadU16(binary, ref offset);
            if (sectionType == 3)
            {
                var bg = ReadTypedU16(binary, ref offset);
                if (bg.Count > 0)
                    board.Background = bg[0];
                continue;
            }

            if (sectionType != 0)
            {
                offset -= 2;
                ParseFields(binary, ref offset, binary.Length, board);
                break;
            }

            var contentLength = ReadU16(binary, ref offset);
            var end = Math.Min(binary.Length, offset + contentLength);
            ParseFields(binary, ref offset, end, board);
            offset = Math.Max(offset, end);
        }

        return board;
    }

    private static void ParseFields(ReadOnlySpan<byte> binary, ref int offset, int end, DecodedBoard board)
    {
        while (offset + 2 <= end)
        {
            var field = ReadU16(binary, ref offset);
            switch (field)
            {
                case FieldEmpty:
                    if (offset + 2 <= end)
                        offset += 2;
                    break;
                case FieldTitle:
                    board.Name = ReadSizeString(binary, ref offset, end);
                    break;
                case FieldAllocate:
                    if (offset + 2 > end)
                        return;
                    board.Objects.Add(new DecodedObject { Type = ReadU16(binary, ref offset) });
                    break;
                case FieldLabel:
                    ApplyText(board, ReadSizeString(binary, ref offset, end));
                    break;
                case FieldFlags:
                    ApplyU16(board, ReadTypedU16(binary, ref offset), static (o, v) => o.Flags = v);
                    break;
                case FieldPosition:
                    ApplyPositions(board, binary, ref offset);
                    break;
                case FieldAngle:
                    ApplyU16(board, ReadTypedU16(binary, ref offset), static (o, v) => o.Angle = v);
                    break;
                case FieldScale:
                    ApplyU16(board, ReadTypedU16(binary, ref offset), static (o, v) => o.Scale = (byte)Math.Min((int)v, 255));
                    break;
                case FieldColours:
                    ApplyColours(board, binary, ref offset);
                    break;
                case FieldArgsAB:
                    ApplyPairs(board, binary, ref offset, static (o, a, b) =>
                    {
                        o.ArgsA = a;
                        o.ArgsB = b;
                    });
                    break;
                case FieldArgsA:
                    ApplyU16(board, ReadTypedU16(binary, ref offset), static (o, v) => o.ArgsA = v);
                    break;
                case FieldArgsB:
                    ApplyU16(board, ReadTypedU16(binary, ref offset), static (o, v) => o.ArgsB = v);
                    break;
                case FieldArgsC:
                    ApplyU16(board, ReadTypedU16(binary, ref offset), static (o, v) => o.ArgsC = v);
                    break;
                default:
                    offset -= 2;
                    return;
            }
        }
    }

    private static void ApplyText(DecodedBoard board, string text)
    {
        for (var i = board.Objects.Count - 1; i >= 0; i--)
        {
            if (board.Objects[i].Type == 100 && string.IsNullOrEmpty(board.Objects[i].Text))
            {
                board.Objects[i].Text = text;
                return;
            }
        }

        if (board.Objects.Count > 0)
            board.Objects[^1].Text = text;
    }

    private static void ApplyU16(DecodedBoard board, IReadOnlyList<ushort> values, Action<DecodedObject, ushort> apply)
    {
        var n = Math.Min(board.Objects.Count, values.Count);
        for (var i = 0; i < n; i++)
            apply(board.Objects[i], values[i]);
    }

    private static void ApplyPositions(DecodedBoard board, ReadOnlySpan<byte> binary, ref int offset)
    {
        var dataType = ReadU16(binary, ref offset);
        var count = ReadU16(binary, ref offset);
        for (var i = 0; i < count && offset + 4 <= binary.Length; i++)
        {
            var x = ReadU16(binary, ref offset);
            var y = ReadU16(binary, ref offset);
            if (i < board.Objects.Count)
            {
                board.Objects[i].X = x;
                board.Objects[i].Y = y;
            }
        }

        _ = dataType;
    }

    private static void ApplyPairs(DecodedBoard board, ReadOnlySpan<byte> binary, ref int offset, Action<DecodedObject, ushort, ushort> apply)
    {
        var dataType = ReadU16(binary, ref offset);
        var count = ReadU16(binary, ref offset);
        _ = dataType;
        for (var i = 0; i < count && offset + 4 <= binary.Length; i++)
        {
            var a = ReadU16(binary, ref offset);
            var b = ReadU16(binary, ref offset);
            if (i < board.Objects.Count)
                apply(board.Objects[i], a, b);
        }
    }

    private static void ApplyColours(DecodedBoard board, ReadOnlySpan<byte> binary, ref int offset)
    {
        var dataType = ReadU16(binary, ref offset);
        var count = ReadU16(binary, ref offset);
        var stride = dataType == 2 ? 4 : dataType == 1 ? 2 : 1;
        for (var i = 0; i < count && offset + stride <= binary.Length; i++)
        {
            byte r = 255, g = 255, b = 255, a = 0;
            if (stride == 4)
            {
                r = binary[offset];
                g = binary[offset + 1];
                b = binary[offset + 2];
                a = binary[offset + 3];
                offset += 4;
            }
            else
            {
                offset += stride;
            }

            if (i < board.Objects.Count)
            {
                board.Objects[i].R = r;
                board.Objects[i].G = g;
                board.Objects[i].B = b;
                board.Objects[i].A = a;
            }
        }
    }

    private static List<ushort> ReadTypedU16(ReadOnlySpan<byte> binary, ref int offset)
    {
        var values = new List<ushort>();
        if (offset + 4 > binary.Length)
            return values;

        var dataType = ReadU16(binary, ref offset);
        var count = ReadU16(binary, ref offset);
        for (var i = 0; i < count && offset < binary.Length; i++)
        {
            switch (dataType)
            {
                case 0:
                    values.Add(binary[offset]);
                    offset += 1;
                    break;
                case 1:
                    if (offset + 2 > binary.Length)
                        return values;
                    values.Add(ReadU16(binary, ref offset));
                    break;
                case 2:
                    if (offset + 4 > binary.Length)
                        return values;
                    values.Add((ushort)ReadU32(binary, ref offset));
                    break;
                default:
                    if (offset + 2 > binary.Length)
                        return values;
                    values.Add(ReadU16(binary, ref offset));
                    break;
            }
        }

        return values;
    }

    private static string ReadSizeString(ReadOnlySpan<byte> binary, ref int offset, int end)
    {
        if (offset + 2 > end)
            return string.Empty;

        var length = ReadU16(binary, ref offset);
        if (length == 0 || offset + length > binary.Length)
            return string.Empty;

        var text = Encoding.UTF8.GetString(binary.Slice(offset, length)).TrimEnd('\0');
        offset += (length + 3) & ~3;
        return text;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> binary, ref int offset)
    {
        var value = BitConverter.ToUInt16(binary[offset..]);
        offset += 2;
        return value;
    }

    private static uint ReadU32(ReadOnlySpan<byte> binary, ref int offset)
    {
        var value = BitConverter.ToUInt32(binary[offset..]);
        offset += 4;
        return value;
    }
}
