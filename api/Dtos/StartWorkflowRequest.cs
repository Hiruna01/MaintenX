using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — no Id, the server assigns it.
/// </summary>
public record StartWorkflowRequest(
    [Required]
    [MaxLength(1000)]
    string Objective,

    // Optional for now; reports are a later feature.
    int? ReportId = null);
