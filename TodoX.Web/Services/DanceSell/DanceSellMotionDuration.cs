using System.Buffers.Binary;

namespace TodoX.Web.Services.DanceSell;

internal static class DanceSellMotionDuration
{
    public static int? TryGetBillableSeconds(ReadOnlySpan<byte> content)
    {
        var moov = FindAtom(content, "moov", 0, content.Length);
        if (moov is null)
        {
            return null;
        }

        var position = moov.Value.PayloadStart;
        while (position < moov.Value.End)
        {
            var child = ReadAtom(content, position, moov.Value.End);
            if (child is null)
            {
                break;
            }

            if (child.Value.Type == "trak")
            {
                var duration = TryGetVideoTrackDuration(content, child.Value);
                if (duration.HasValue)
                {
                    return duration;
                }
            }

            position = child.Value.End;
        }

        return null;
    }

    private static int? TryGetVideoTrackDuration(ReadOnlySpan<byte> content, Atom trak)
    {
        var mdia = FindAtom(content, "mdia", trak.PayloadStart, trak.End);
        if (mdia is null)
        {
            return null;
        }

        var handler = FindAtom(content, "hdlr", mdia.Value.PayloadStart, mdia.Value.End);
        if (handler is null || handler.Value.PayloadLength < 12
            || ReadFourCc(content, handler.Value.PayloadStart + 8) != "vide")
        {
            return null;
        }

        var mdhd = FindAtom(content, "mdhd", mdia.Value.PayloadStart, mdia.Value.End);
        if (mdhd is null)
        {
            return null;
        }

        var version = content[mdhd.Value.PayloadStart];
        var fieldsStart = mdhd.Value.PayloadStart + 4;
        ulong timescale;
        ulong duration;
        if (version == 1)
        {
            if (mdhd.Value.PayloadLength < 32)
            {
                return null;
            }

            timescale = BinaryPrimitives.ReadUInt32BigEndian(content[(fieldsStart + 16)..]);
            duration = BinaryPrimitives.ReadUInt64BigEndian(content[(fieldsStart + 20)..]);
        }
        else
        {
            if (mdhd.Value.PayloadLength < 20)
            {
                return null;
            }

            timescale = BinaryPrimitives.ReadUInt32BigEndian(content[(fieldsStart + 8)..]);
            duration = BinaryPrimitives.ReadUInt32BigEndian(content[(fieldsStart + 12)..]);
        }

        if (timescale == 0 || duration == 0)
        {
            return null;
        }

        var seconds = (double)duration / timescale;
        return seconds > 0 && seconds <= int.MaxValue
            ? (int)Math.Ceiling(seconds)
            : null;
    }

    private static Atom? FindAtom(ReadOnlySpan<byte> content, string type, int start, int end)
    {
        var position = start;
        while (position < end)
        {
            var atom = ReadAtom(content, position, end);
            if (atom is null)
            {
                return null;
            }

            if (atom.Value.Type == type)
            {
                return atom;
            }

            position = atom.Value.End;
        }

        return null;
    }

    private static Atom? ReadAtom(ReadOnlySpan<byte> content, int offset, int limit)
    {
        if (offset < 0 || offset > limit - 8)
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt32BigEndian(content[offset..]);
        var headerSize = 8;
        ulong atomSize = size;
        if (size == 1)
        {
            if (offset > limit - 16)
            {
                return null;
            }

            atomSize = BinaryPrimitives.ReadUInt64BigEndian(content[(offset + 8)..]);
            headerSize = 16;
        }
        else if (size == 0)
        {
            atomSize = (ulong)(limit - offset);
        }

        if (atomSize < (ulong)headerSize || atomSize > (ulong)(limit - offset)
            || atomSize > int.MaxValue)
        {
            return null;
        }

        return new Atom(
            ReadFourCc(content, offset + 4),
            offset + headerSize,
            offset + (int)atomSize);
    }

    private static string ReadFourCc(ReadOnlySpan<byte> content, int offset)
        => offset >= 0 && offset <= content.Length - 4
            ? new string(new[]
            {
                (char)content[offset],
                (char)content[offset + 1],
                (char)content[offset + 2],
                (char)content[offset + 3]
            })
            : string.Empty;

    private readonly record struct Atom(string Type, int PayloadStart, int End)
    {
        public int PayloadLength => End - PayloadStart;
    }
}
