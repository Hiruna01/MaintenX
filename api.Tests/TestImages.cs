using System.Text;

namespace api.Tests;

/// <summary>
/// The smallest JPEGs and PNGs whose STRUCTURE is real — segments and chunks with true
/// lengths, ending in EOI / IEND — so ImageMetadata can walk them. Nothing decodes the pixels
/// on the server, so the pixel bytes are arbitrary.
///
/// Each image comes clean and "as a phone wrote it": the same image with EXIF carrying a GPS
/// position, a comment, XMP/IPTC, text chunks, and bytes after the end marker. Stripping the
/// second must give EXACTLY the first — which is the whole assertion the photo tests make.
/// </summary>
internal static class TestImages
{
    /// <summary>What every metadata block below carries, so a test can say it never reached storage.</summary>
    public const string Secret = "GPS 6.9271N 79.8612E";

    private static readonly byte[] Soi = { 0xFF, 0xD8 };
    private static readonly byte[] Eoi = { 0xFF, 0xD9 };

    private static readonly byte[] App0Jfif =
        Segment(0xE0, new byte[] { 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });

    private static readonly byte[] Dqt = Segment(0xDB, Enumerable.Repeat((byte)0x10, 65).ToArray());

    private static readonly byte[] Sos = Segment(0xDA, new byte[] { 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 });

    /// <summary>Entropy-coded data with a stuffed 0xFF and a restart marker in it.</summary>
    private static readonly byte[] Scan = { 0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78, 0x9A };

    /// <summary>A clean baseline JPEG: SOI, JFIF, a table, one scan, EOI.</summary>
    public static byte[] Jpeg => Concat(Soi, App0Jfif, Dqt, Sos, Scan, Eoi);

    /// <summary>
    /// The same JPEG as a phone writes it: EXIF (APP1) and IPTC (APP13) before the scan, a
    /// comment, XMP (another APP1) between two scans the way a progressive JPEG has them — so
    /// metadata AFTER the first scan is covered — and a trailer after EOI.
    /// </summary>
    public static byte[] JpegWithMetadata => Concat(
        Soi, App0Jfif,
        Segment(0xE1, Ascii($"Exif\0\0{Secret}")),
        Segment(0xED, Ascii($"Photoshop 3.0\0{Secret}")),
        Dqt,
        Segment(0xFE, Ascii($"shot on a phone at {Secret}")),
        Sos, Scan,
        Segment(0xE1, Ascii($"http://ns.adobe.com/xap/1.0/\0{Secret}")),
        Sos, Scan,
        Eoi,
        Ascii($"MOTION_PHOTO {Secret}"));

    /// <summary><see cref="JpegWithMetadata"/> stripped — the second scan stays, the rest goes.</summary>
    public static byte[] JpegWithMetadataStripped => Concat(Soi, App0Jfif, Dqt, Sos, Scan, Sos, Scan, Eoi);

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly byte[] Ihdr =
        Chunk("IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0 });

    private static readonly byte[] Iend = Chunk("IEND", Array.Empty<byte>());

    /// <summary>
    /// A clean PNG. With <paramref name="totalLength"/>, the IDAT is padded so the file is
    /// exactly that many bytes — for the 5 MB boundary.
    /// </summary>
    public static byte[] Png(int totalLength = 0)
    {
        var fixedLength = PngSignature.Length + Ihdr.Length + Iend.Length + 12;
        var dataLength = totalLength == 0 ? 10 : totalLength - fixedLength;
        return Concat(PngSignature, Ihdr, Chunk("IDAT", new byte[dataLength]), Iend);
    }

    /// <summary>The same PNG with tEXt, iTXt (XMP), zTXt and eXIf chunks, and a trailer after IEND.</summary>
    public static byte[] PngWithMetadata => Concat(
        PngSignature, Ihdr,
        Chunk("tEXt", Ascii($"Location\0{Secret}")),
        Chunk("eXIf", Ascii($"MM\0*{Secret}")),
        Chunk("IDAT", new byte[10]),
        Chunk("iTXt", Ascii($"XML:com.adobe.xmp\0\0\0\0\0{Secret}")),
        Chunk("zTXt", Ascii($"Comment\0\0{Secret}")),
        Iend,
        Ascii(Secret));

    /// <summary>A PNG signature and the start of a chunk, and nothing else — cannot be walked.</summary>
    public static byte[] TruncatedPng => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

    private static byte[] Segment(byte marker, byte[] data)
    {
        var length = data.Length + 2;
        return Concat(new byte[] { 0xFF, marker, (byte)(length >> 8), (byte)length }, data);
    }

    /// <summary>A PNG chunk. The CRC is left zero: nothing on the server checks it.</summary>
    private static byte[] Chunk(string type, byte[] data) =>
        Concat(
            new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length },
            Ascii(type), data, new byte[4]);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
