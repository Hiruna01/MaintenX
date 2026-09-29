using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One open work order as `get_open_work_orders` hands it to the strategist: the order, its
/// asset, where it stands, how it is being handled and what it is estimated to cost — the
/// facts consolidation is decided on, and not who is assigned to it.
///
/// Not <see cref="WorkOrderDto"/>: that carries the assigned technician's id and name, which
/// the strategist does not read, and which every tool call would otherwise copy into
/// AgentStep.PayloadJson and send on to the LLM provider.
/// </summary>
public record ToolWorkOrderDto(
    int Id,
    int ReportId,
    int AssetId,
    string AssetTag,
    WorkOrderStatus Status,
    WorkOrderStrategy Strategy,
    decimal EstimatedCost,
    DateTime CreatedAt);
