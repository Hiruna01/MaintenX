namespace CampusFacilities.Api.Services;

/// <summary>
/// Configuration for Supabase Storage, where uploaded photos live.
///
/// ONLY THE OBJECT'S URL GOES IN POSTGRES, never the bytes — see Report.PhotoUrl. The
/// service key is the Supabase service role key: it bypasses every storage policy, so it
/// stays on this server and is never sent to the web or mobile client.
///
/// From configuration (Supabase:Url / Supabase:ServiceKey / Supabase:StorageBucket, or
/// SUPABASE_URL / SUPABASE_SERVICE_KEY / SUPABASE_STORAGE_BUCKET from the root .env) —
/// never a literal in code. Locally, through dotnet user-secrets.
///
/// Like AgentSettings, an empty value does not stop the API booting: a team member who is
/// not working on photos should not need a Supabase project to run it. An upload then
/// fails with a 503 instead, and startup says so once.
/// </summary>
public class StorageSettings
{
    /// <summary>
    /// The bucket used when none is configured. It must exist in the Supabase project and
    /// be PUBLIC, because what is stored is a public object URL the clients load directly.
    /// </summary>
    public const string DefaultBucket = "photos";

    /// <summary>The Supabase project URL, e.g. https://abcdefgh.supabase.co.</summary>
    public string Url { get; init; } = string.Empty;

    public string ServiceKey { get; init; } = string.Empty;

    public string Bucket { get; init; } = DefaultBucket;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ServiceKey);
}
