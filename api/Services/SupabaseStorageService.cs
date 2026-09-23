using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace CampusFacilities.Api.Services;

/// <summary>
/// <see cref="IFileStorageService"/> over the Supabase Storage REST API.
///
/// One POST per upload, to /storage/v1/object/{bucket}/{path}, authenticated with the
/// service role key. The URL handed back is the object's PUBLIC URL, so the bucket must be
/// public — the clients load the image straight from Supabase and never through this API.
/// </summary>
public class SupabaseStorageService : IFileStorageService
{
    /// <summary>The named HttpClient registered in Program.cs.</summary>
    public const string HttpClientName = "SupabaseStorage";

    /// <summary>
    /// The extension each storable content type is saved with. The extension comes from
    /// the type the caller has already validated, never from the uploaded file name.
    /// A new type is one line here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExtensionsByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png"
        };

    /// <summary>
    /// Folders are written by our own code ("reports/42"), never by a client, but they still
    /// become part of a storage path — so nothing but plain lower-case segments is accepted.
    /// No "..", no leading slash, no empty segment.
    /// </summary>
    private static readonly Regex SafeFolder = new("^[a-z0-9-]+(/[a-z0-9-]+)*$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StorageSettings _settings;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(
        IHttpClientFactory httpClientFactory,
        StorageSettings settings,
        ILogger<SupabaseStorageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string?> UploadAsync(
        Stream content,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        // Both of these are caller bugs rather than storage failures, so they throw instead
        // of returning null: a 503 would send someone looking for an outage that isn't there.
        if (!ExtensionsByContentType.TryGetValue(contentType, out var extension))
        {
            throw new ArgumentException($"No storage extension is defined for '{contentType}'.", nameof(contentType));
        }

        if (!SafeFolder.IsMatch(folder))
        {
            throw new ArgumentException($"'{folder}' is not a safe storage folder.", nameof(folder));
        }

        if (!_settings.IsConfigured)
        {
            _logger.LogWarning(
                "Photo upload refused: Supabase Storage is not configured (Supabase:Url / Supabase:ServiceKey).");
            return null;
        }

        // Generated here, and the only name the object will ever have. See the interface.
        var objectPath = $"{folder}/{Guid.NewGuid():N}{extension}";
        var baseUrl = _settings.Url.TrimEnd('/');
        var bucket = Uri.EscapeDataString(_settings.Bucket);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{baseUrl}/storage/v1/object/{bucket}/{objectPath}");

        // Both headers: a legacy JWT service key is read from Authorization, the newer
        // sb_secret_ keys from apikey. Sending both works with either.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ServiceKey);
        request.Headers.Add("apikey", _settings.ServiceKey);

        // A GUID never collides, so an existing object here would mean something is wrong.
        // Refuse rather than silently replace it.
        request.Headers.Add("x-upsert", "false");

        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        try
        {
            var http = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Supabase's error text ("Bucket not found", "Invalid JWT") is what someone
                // debugging this will need. It is logged, never returned to the client.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase Storage refused an upload to {ObjectPath}: HTTP {StatusCode} {Body}",
                    objectPath, (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                return null;
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation. The `when` keeps a
            // genuinely cancelled request (the client hung up) a cancellation.
            _logger.LogWarning("Supabase Storage did not respond in time for {ObjectPath}.", objectPath);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach Supabase Storage for {ObjectPath}.", objectPath);
            return null;
        }

        return $"{baseUrl}/storage/v1/object/public/{bucket}/{objectPath}";
    }
}
