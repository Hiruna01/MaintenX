using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record ServiceRecordDto(
    int Id,
    int AssetId,
    DateOnly ServicedOn,
    string TechnicianName,
    string? TechnicianNote,
    ServiceOutcome Outcome,
    int? WorkOrderId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
