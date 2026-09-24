namespace CampusFacilities.Api.Dtos;

/// <summary>
/// What POST /api/workorders/{id}/photo returns: the URL the completion photo can be loaded
/// from, which is also what is now stored on the order. The counterpart of ReportPhotoDto —
/// no input DTO, because the upload is a multipart file and who is uploading comes from the
/// token.
/// </summary>
public record CompletionPhotoDto(string CompletionPhotoUrl);
