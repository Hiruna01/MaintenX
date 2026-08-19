using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>Input DTO. No Id — the server assigns it.</summary>
public record CreateRoomDto(
    [Range(1, int.MaxValue)] int BuildingId,
    [Required][MaxLength(200)] string Name,
    [Required][MaxLength(20)] string Code,
    [Range(-5, 200)] int Floor);
