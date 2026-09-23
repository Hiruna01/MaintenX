namespace CampusFacilities.Api.Services;

/// <summary>
/// Stores a file somewhere outside the database and hands back a URL to it.
///
/// GENERIC ON PURPOSE. It knows nothing about reports: a report photo and a work order's
/// completion photo are both "bytes, a content type and a folder", so a second caller uses
/// this as it is rather than growing a second method. What a caller is ALLOWED to upload —
/// which types, how large — is that caller's rule, checked before it gets here; see
/// ImageUploadRules for the photo one.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Uploads <paramref name="content"/>, read from its current position to the end, under
    /// <paramref name="folder"/> (e.g. "reports/42"), and returns the stored object's URL.
    ///
    /// THE OBJECT NAME IS GENERATED HERE — a new GUID plus an extension derived from
    /// <paramref name="contentType"/>. There is deliberately no file name parameter: a
    /// client-supplied name is the classic path-traversal vector ("../../other/thing.png"),
    /// and the only way to be sure it is never used is for there to be nowhere to pass it.
    ///
    /// RETURNS NULL WHEN THE STORAGE IS UNAVAILABLE — unreachable, timed out, not configured,
    /// or refusing the upload. It never throws for any of those, so a caller can simply
    /// decline to record a URL: a URL pointing at nothing is worse than no photo.
    /// </summary>
    Task<string?> UploadAsync(
        Stream content,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);
}
