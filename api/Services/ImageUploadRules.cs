namespace CampusFacilities.Api.Services;

/// <summary>
/// What counts as an acceptable photo upload. Deterministic rules, so C#, and in one place
/// so a report photo and a completion photo cannot end up with two different answers.
/// </summary>
public static class ImageUploadRules
{
    /// <summary>5 MB. A phone photo of a fault is well under this; anything larger is not one.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// The allowed content types, each with the bytes every file of that type starts with.
    ///
    /// THE CONTENT TYPE IS THE CLIENT'S CLAIM, NOT A FACT. It is a header on the multipart
    /// part, and anything can be sent labelled image/png. Checking the file's first bytes
    /// against the claimed type is what makes the claim worth anything — a text file or a
    /// script renamed to .png starts with the wrong bytes.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, byte[]> Signatures =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = new byte[] { 0xFF, 0xD8, 0xFF },
            ["image/png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
        };

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && Signatures.ContainsKey(contentType);

    /// <summary>
    /// Whether <paramref name="content"/> starts with the signature of
    /// <paramref name="contentType"/>. Leaves the stream where it found it, so the same
    /// stream can then be uploaded whole. An empty stream never matches.
    /// </summary>
    public static async Task<bool> HasMatchingSignatureAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek)
        {
            // An uploaded IFormFile is always buffered and seekable. Reaching here with
            // anything else is a caller bug, and a loud one beats a half-uploaded file.
            throw new ArgumentException("The stream must be seekable to check its signature.", nameof(content));
        }

        var expected = Signatures[contentType];
        var header = new byte[expected.Length];
        var start = content.Position;

        var read = await content.ReadAtLeastAsync(
            header, header.Length, throwOnEndOfStream: false, cancellationToken);

        content.Position = start;

        return read == expected.Length && header.AsSpan().SequenceEqual(expected);
    }
}
