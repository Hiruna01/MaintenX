using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One service visit as `get_asset_service_history` hands it to an agent: when, what the
/// technician wrote, the outcome, and which work order it closed — the evidence, and nothing
/// about the PERSON.
///
/// Not <see cref="ServiceRecordDto"/>: that carries the technician's name, which no agent reads
/// and which every tool call would otherwise copy into AgentStep.PayloadJson and send on to the
/// LLM provider. A tool returns the facts an agent needs, not the row it came from.
/// </summary>
public record ToolServiceVisitDto(
    int Id,
    int AssetId,
    DateOnly ServicedOn,
    string? TechnicianNote,
    ServiceOutcome Outcome,

    // Which repair appended this visit — the verification agent tells "this repair" from the
    // earlier visits by it. Null on seeded and imported history.
    int? WorkOrderId);
