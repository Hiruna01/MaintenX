using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>Input DTO. No Id — the server assigns it.</summary>
public record CreateBuildingDto(
    [Required][MaxLength(200)] string Name,
    [Required][MaxLength(20)] string Code);
