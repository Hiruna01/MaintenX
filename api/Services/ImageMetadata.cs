namespace CampusFacilities.Api.Services;

/// <summary>
/// Removes the metadata a phone writes into a photo — EXIF (GPS position, device, time), XMP,
/// IPTC, comments, PNG text — before the photo goes to a PUBLIC bucket. A photo of a broken
/// projector is evidence of a fault; where the reporter was standing, on what phone, is not
/// something the URL should hand anyone who has it.
///
/// BYTE-LEVEL, NO DEPENDENCY. The file is walked by its own structure — JPEG segments, PNG
/// chunks — and the metadata ones are left out; the pixel data is copied untouched, so nothing
/// is decoded or re-encoded and no quality is lost. An image library would be a large
/// dependency for what is a loop over length-prefixed records.
///
/// FAILS CLOSED. A file whose structure cannot be walked to its end returns null and is
/// refused as not a valid image: if it cannot be read, nothing can promise its metadata is
/// gone. The caller checks the magic bytes FIRST (ImageUploadRules), so this only ever sees a
/// file that at least claims to be the type it is.
///
/// One known cost: EXIF carries the orientation flag too, so a photo that relied on it rather
/// than on its pixels may display rotated. The phone's image_picker re-encodes on resize.
/// </summary>
public static class ImageMetadata
{
    /// <summary>
    /// JPEG markers that are KEPT among the APPn segments: APP0 (JFIF — how to read the
    /// pixels), APP2 (the ICC colour profile) and APP14 (Adobe — the colour transform a CMYK
    /// decoder needs). Every other APPn is vendor metadata — APP1 is EXIF and XMP, APP13 IPTC —
    /// and is removed, as is COM (a free-text comment).
    /// </summary>
    private static readonly HashSet<byte> KeptAppMarkers = new() { 0xE0, 0xE2, 0xEE };

    /// <summary>
    /// PNG chunks that are REMOVED: the three text chunks (tEXt, zTXt, iTXt — XMP travels in
    /// iTXt) and eXIf. Everything else is image data or how to read it, and stays.
    /// </summary>
    private static readonly HashSet<string> RemovedPngChunks = new(StringComparer.Ordinal)
    {
        "tEXt", "zTXt", "iTXt", "eXIf"
    };

    /// <summary>
    /// <paramref name="bytes"/> without their metadata, or null when the file's structure could
    /// not be walked. Anything after the image's end marker (JPEG EOI, PNG IEND) is dropped as
    /// well — some phones append extra data there.
    /// </summary>
    public static byte[]? Strip(byte[] bytes, string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => StripJpeg(bytes),
            "image/png" => StripPng(bytes),
            _ => null
        };

    private static byte[]? StripJpeg(byte[] src)
    {
        if (src.Length < 4 || src[0] != 0xFF || src[1] != 0xD8)
        {
            return null;
        }

        using var output = new MemoryStream(src.Length);
        output.Write(src, 0, 2);
        var i = 2;

        while (true)
        {
            // Every segment starts with 0xFF; any number of 0xFF fill bytes may precede it.
            if (i >= src.Length || src[i] != 0xFF)
            {
                return null;
            }

            while (i < src.Length && src[i] == 0xFF)
            {
                i++;
            }

            if (i >= src.Length)
            {
                return null;
            }

            var marker = src[i++];

            if (marker == 0xD9)
            {
                // EOI: the image ends here, and so does what is uploaded.
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                return output.ToArray();
            }

            if (marker == 0xD8 || marker == 0x00)
            {
                return null;
            }

            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                // A marker with no length (RSTn, TEM).
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                continue;
            }

            if (i + 2 > src.Length)
            {
                return null;
            }

            // The length counts itself, so it is at least 2.
            var length = (src[i] << 8) | src[i + 1];

            if (length < 2 || i + length > src.Length)
            {
                return null;
            }

            var isMetadata = marker == 0xFE
                             || (marker is >= 0xE0 and <= 0xEF && !KeptAppMarkers.Contains(marker));

            if (!isMetadata)
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                output.Write(src, i, length);
            }

            i += length;

            if (marker != 0xDA)
            {
                continue;
            }

            // SOS: entropy-coded data follows, with no length. Inside it 0xFF is always
            // followed by 0x00 (a stuffed byte) or a restart marker; anything else is the next
            // segment, which the loop above reads — so metadata after the first scan (a
            // progressive JPEG has several) is removed as well.
            var start = i;

            while (i < src.Length)
            {
                if (src[i] == 0xFF)
                {
                    if (i + 1 >= src.Length)
                    {
                        return null;
                    }

                    var next = src[i + 1];

                    if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                    {
                        i += 2;
                        continue;
                    }

                    break;
                }

                i++;
            }

            output.Write(src, start, i - start);
        }
    }

    private static byte[]? StripPng(byte[] src)
    {
        const int signatureLength = 8;

        if (src.Length < signatureLength)
        {
            return null;
        }

        using var output = new MemoryStream(src.Length);
        output.Write(src, 0, signatureLength);
        var i = signatureLength;
        var first = true;

        // Each chunk: a 4-byte big-endian data length, a 4-byte type, the data, a 4-byte CRC.
        while (i + 12 <= src.Length)
        {
            var length = ((long)src[i] << 24) | ((long)src[i + 1] << 16) | ((long)src[i + 2] << 8) | src[i + 3];
            var type = System.Text.Encoding.ASCII.GetString(src, i + 4, 4);
            var total = 12 + length;

            if (length > int.MaxValue || i + total > src.Length)
            {
                return null;
            }

            // The PNG specification requires IHDR first; a file that does not start with it
            // is not one this can vouch for.
            if (first && type != "IHDR")
            {
                return null;
            }

            first = false;

            if (!RemovedPngChunks.Contains(type))
            {
                output.Write(src, i, (int)total);
            }

            i += (int)total;

            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        // Ran out of bytes before IEND.
        return null;
    }
}
