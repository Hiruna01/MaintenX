namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — one work order awaiting approval, with everything a manager needs to decide
/// it and nothing they would have to open another page for: the order (and, on it, the
/// approval basis), the machine with its full service history, the failure summary, and the
/// agent's diagnosis and proposal.
///
/// One row of GET /api/workorders/approvals. Assembled in WorkOrderService from the services
/// that own each part — the asset and its summary from IAssetService, never a second query
/// of the registry written here.
///
/// NULL IS NOT EMPTY. <see cref="Diagnosis"/> or <see cref="Proposal"/> null means no such
/// agent step has been recorded for the report — the agent never ran, or the order predates
/// it. A run that failed is NOT null: it comes back with its ValidationResult and reason, so
/// "the agent could not say" and "the agent was never asked" read as the different facts
/// they are.
/// </summary>
public record ApprovalCaseDto(
    WorkOrderDetailDto WorkOrder,

    // Category and room resolved, and the service history OLDEST-FIRST, exactly as
    // GET /api/assets/{id} serves it: read to follow a machine over time.
    AssetDetailDto Asset,

    AssetFailureSummaryDto FailureSummary,

    AgentDiagnosisDto? Diagnosis,
    AgentProposalDto? Proposal);
