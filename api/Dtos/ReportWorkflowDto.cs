using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — the latest workflow raised for a report, as its detail page needs it.
///
/// <see cref="CanRaiseWorkOrder"/> is whether POST /api/workorders would accept an order for
/// this report now: the workflow is somewhere <see cref="Services.WorkflowTransitions"/> lets
/// an order be raised from (Strategizing, or Failed), and the report is not Closed. It decides
/// which control a client OFFERS; CreateAsync still makes the move and refuses a stale one
/// with a 409.
/// </summary>
public record ReportWorkflowDto(
    int Id,
    WorkflowState State,
    bool CanRaiseWorkOrder);
