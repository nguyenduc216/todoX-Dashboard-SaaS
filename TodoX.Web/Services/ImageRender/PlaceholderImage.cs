using System.IO.Compression;
using System.Text;

namespace TodoX.Web.Services.ImageRender;

/// <summary>
/// Generates a small valid PNG (solid TodoX-gold tinted square with a deterministic pattern)
/// used as a fallback when the real Vertex render is unavailable in dev. No external image libs.
/// </summary>
public static class PlaceholderImage
{
    public static byte[] Generate(string seedText, int index, int size = 512)
    {
        // Deterministic tint per index so the 3 placeholders look distinct.
        var seed = (Math.Abs(seedText.GetHashCode()) + index * 7919) % 360;
        var (r, g, b) = HsvToRgb(seed, 0.35, 0.22);
        var (r2, g2, b2) = HsvToRgb(seed, 0.6, 0.9);

        var raw = BuildRawImage(size, size, r, g, b, r2, g2, b2, index);
        return EncodePng(size, size, raw);
    }

    private static byte[] BuildRawImage(int w, int h, byte r, byte g, byte b, byte r2, byte g2, byte b2, int idx)
    {
        // Each row: 1 filter byte (0) + w*3 RGB bytes.
        var stride = 1 + w * 3;
        var data = new byte[stride * h];
        var cx = w / 2.0;
        var cy = h / 2.0;
        var maxd = Math.Sqrt(cx * cx + cy * cy);

        for (var y = 0; y < h; y++)
        {
            var rowStart = y * stride;
            data[rowStart] = 0; // filter type none
            for (var x = 0; x < w; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxd;
                var t = 1.0 - d; // brighter toward center
                var p = rowStart + 1 + x * 3;
                data[p] = (byte)(r + (r2 - r) * t);
                data[p + 1] = (byte)(g + (g2 - g) * t);
                data[p + 2] = (byte)(b + (b2 - b) * t);
            }
        }
        return data;
    }

    private static byte[] EncodePng(int w, int h, byte[] rawImage)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); // PNG signature

        // IHDR
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, w);
        WriteBe(ihdr, 4, h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type RGB
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT (zlib-compressed)
        byte[] compressed;
        using (var comp = new MemoryStream())
        {
            using (var z = new ZLibStream(comp, CompressionLevel.Fastest, true))
            {
                z.Write(rawImage, 0, rawImage.Length);
            }
            compressed = comp.ToArray();
        }
        WriteChunk(ms, "IDAT", compressed);
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteBe(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBe(crcBytes, 0, (int)crc);
        s.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xffffffff;
        void Feed(byte[] arr)
        {
            foreach (var by in arr)
            {
                crc ^= by;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
                }
            }
        }
        Feed(type);
        Feed(data);
        return crc ^ 0xffffffff;
    }

    private static (byte, byte, byte) HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
