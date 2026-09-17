namespace CampusFacilities.Api.Services;

/// <summary>
/// The spending control on work orders: above this figure, a work order needs a manager's
/// decision before any work is scheduled; at or below it, it does not.
///
/// A CLASS READ FROM CONFIGURATION, never a literal sitting in a service. The threshold is
/// the kind of number that changes — with a budget, with a currency, with a faculty — and
/// a service that hardcoded it would need a code change, a review and a deploy to follow
/// a decision that was made in a meeting. It also could not be varied between a demo
/// database and a real one, which is precisely when a threshold most wants varying.
///
/// The comparison itself stays in C#. Approval routing is a deterministic business rule —
/// one decimal against another — and it is never delegated to a prompt: an agent may
/// recommend a strategy and estimate its cost, but whether that estimate needs a human is
/// arithmetic, and arithmetic a model is asked to perform is arithmetic that can come back
/// wrong and unauditable.
///
/// Built once in Program.cs and registered as a singleton: it holds no DbContext and never
/// changes after startup, exactly like JwtSettings and AgentSettings.
/// </summary>
public class ApprovalSettings
{
    /// <summary>
    /// The default threshold in LKR, used when nothing is configured. One literal, in one
    /// place — Program.cs falls back to this constant rather than repeating the number,
    /// so the default cannot drift between the two.
    /// </summary>
    public const decimal DefaultCostThreshold = 15_000m;

    /// <summary>
    /// Work orders estimated above this need approval. LKR.
    ///
    /// DECIMAL, NEVER double. This value is compared against WorkOrder.EstimatedCost, and
    /// binary floating point cannot represent ordinary decimal amounts exactly: an
    /// estimate that should sit precisely on the threshold can land a hair below it and be
    /// auto-approved. Spending that escapes a manager because of a representation error is
    /// not a rounding bug, it is money spent without authorisation. Both sides of the
    /// comparison are decimal, all the way from the JSON to the column.
    /// </summary>
    public decimal CostThreshold { get; init; } = DefaultCostThreshold;
}
