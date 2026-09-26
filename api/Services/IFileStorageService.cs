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
    /// NEVER THROWS FOR A STORAGE FAILURE — unreachable, timed out, not configured, refusing
    /// the upload or rate-limiting it all come back as a <see cref="StorageUploadResult"/>
    /// with no URL, so a caller can simply decline to record one: a URL pointing at nothing
    /// is worse than no photo.
    /// </summary>
    Task<StorageUploadResult> UploadAsync(
        Stream content,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);
}

/// <summary>How an upload ended. A plain enum, like every other outcome in this API.</summary>
public enum StorageUploadOutcome
{
    /// <summary>Stored; <see cref="StorageUploadResult.Url"/> is set.</summary>
    Stored,

    /// <summary>Unreachable, timed out, not configured or refused. A 503 to the client.</summary>
    Unavailable,

    /// <summary>
    /// The storage provider said too many requests (HTTP 429). Still a 503 to the client —
    /// storage is unavailable to it either way — but told apart in the log, and carrying the
    /// provider's Retry-After when it sent one, so the client is told when to try again
    /// rather than to hammer a service that asked it to wait.
    /// </summary>
    RateLimited
}

/// <summary>
/// <see cref="Url"/> is set only when <see cref="Outcome"/> is Stored. <see cref="RetryAfter"/>
/// is the provider's Retry-After header, re-serialised after parsing (delta-seconds or an
/// HTTP date), and only ever set when RateLimited.
/// </summary>
public record StorageUploadResult(StorageUploadOutcome Outcome, string? Url = null, string? RetryAfter = null)
{
    public static StorageUploadResult Unavailable { get; } = new(StorageUploadOutcome.Unavailable);
}
