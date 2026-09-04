using System.Buffers.Binary;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellMotionDurationTests
{
    [Fact]
    public void TryGetBillableSeconds_ReadsVideoTrackMdhdAndRoundsUp()
    {
        var mp4 = BuildMp4Seconds(4.2);

        var seconds = DanceSellMotionDuration.TryGetBillableSeconds(mp4);

        Assert.Equal(5, seconds);
    }

    [Fact]
    public void TryGetBillableSeconds_IgnoresNonVideoTracks()
    {
        var mp4 = BuildMp4Seconds(7.8, videoTrack: false);

        var seconds = DanceSellMotionDuration.TryGetBillableSeconds(mp4);

        Assert.Null(seconds);
    }

    private static byte[] BuildMp4Seconds(double seconds, bool videoTrack = true)
    {
        var timescale = 1000u;
        var duration = (uint)Math.Round(seconds * timescale);
        var mdhdPayload = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(12, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(16, 4), duration);
        BinaryPrimitives.WriteUInt32BigEndian(mdhdPayload.AsSpan(20, 4), 0);

        var handlerType = videoTrack ? "vide" : "soun";
        var hdlrPayload = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(hdlrPayload.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(hdlrPayload.AsSpan(4, 4), 0);
        System.Text.Encoding.ASCII.GetBytes(handlerType).AsSpan().CopyTo(hdlrPayload.AsSpan(8, 4));

        var mdia = Box("mdia", Box("hdlr", hdlrPayload), Box("mdhd", mdhdPayload));
        var trak = Box("trak", mdia);
        var moov = Box("moov", trak);
        var ftyp = Box("ftyp", System.Text.Encoding.ASCII.GetBytes("isom"), new byte[8]);
        return ftyp.Concat(moov).ToArray();
    }

    private static byte[] Box(string type, params byte[][] children)
    {
        var payload = children.SelectMany(x => x).ToArray();
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type).AsSpan().CopyTo(box.AsSpan(4, 4));
        payload.CopyTo(box, 8);
        return box;
    }
}
