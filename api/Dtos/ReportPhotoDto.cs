namespace CampusFacilities.Api.Dtos;

/// <summary>
/// What POST /api/reports/{id}/photo returns: the URL the photo can be loaded from, which
/// is also what is now stored on the report. There is no input DTO — the upload is a
/// multipart file, and the only other thing that matters, who is uploading, comes from the
/// token.
/// </summary>
public record ReportPhotoDto(string PhotoUrl);
