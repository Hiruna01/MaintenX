using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Services;

namespace api.Tests;

/// <summary>
/// PlanRules — the C# check on the planner's plan, as pure functions, no database. The agent
/// service checks the same rules in PlannerOutput before it routes on a plan
/// (agent/tests/test_planner.py); this is the check on THIS side of the network, before the
/// plan is stored as the workflow's. What the runner does with the answer is WorkflowRunnerTests'.
/// </summary>
public class PlanRulesTests
{
    private static JsonElement Plan(string rationale, params string[] agents) =>
        JsonSerializer.SerializeToElement(new
        {
            steps = agents.Select(a => new { agent = a, purpose = $"What the {a} is to establish." }),
            rationale
        });

    [Fact]
    public void AFullPlan_IsAccepted_InOrder_EveryStepPendingAndThePlanners()
    {
        var (plan, reason) = PlanRules.Validate(Plan("Vague report.", "clarifier", "diagnostic", "strategist"));

        Assert.Null(reason);
        Assert.Equal(PlanRules.SourcePlanner, plan!.Source);
        Assert.Equal("Vague report.", plan.Rationale);
        Assert.Equal(new[] { 1, 2, 3 }, plan.Steps.Select(s => s.Order));
        Assert.Equal(new[] { "clarifier", "diagnostic", "strategist" }, plan.Steps.Select(s => s.Agent));
        Assert.All(plan.Steps, s => Assert.Equal((PlanStepStatus.Pending, PlanRules.AddedByPlanner), (s.Status, s.AddedBy)));
        Assert.True(PlanRules.IncludesClarifier(plan));
    }

    [Fact]
    public void APlanWithoutTheClarifier_IsAccepted()
    {
        var (plan, reason) = PlanRules.Validate(Plan("Detailed report.", "diagnostic", "strategist"));

        Assert.Null(reason);
        Assert.False(PlanRules.IncludesClarifier(plan!));
    }

    [Theory]
    [InlineData("out of order", "diagnostic", "clarifier", "strategist")]
    [InlineData("out of order", "clarifier", "diagnostic", "diagnostic")]
    [InlineData("leaves out the diagnostic", "clarifier", "strategist")]
    [InlineData("leaves out the strategist", "clarifier", "diagnostic")]
    [InlineData("not an agent a report run can use", "diagnostic", "strategist", "verification")]
    [InlineData("not an agent a report run can use", "Diagnostic", "strategist")]
    [InlineData("must have 2 or 3", "strategist")]
    [InlineData("must have 2 or 3", "clarifier", "diagnostic", "strategist", "strategist")]
    public void AnIllegalPlan_IsRefused_WithTheReason(string because, params string[] agents)
    {
        var (plan, reason) = PlanRules.Validate(Plan("r", agents));

        Assert.Null(plan);
        Assert.Contains(because, reason);
    }

    [Theory]
    [InlineData("""{"steps": [], "rationale": ""}""")]
    [InlineData("""{"steps": "clarifier, diagnostic"}""")]
    [InlineData("""{"rationale": "no steps at all"}""")]
    [InlineData("""["clarifier", "diagnostic", "strategist"]""")]
    [InlineData("""{"steps": [{"agent": "diagnostic"}, {"agent": "strategist", "purpose": "x"}], "rationale": "r"}""")]
    public void AMalformedPlan_IsRefused_NeverThrown(string json)
    {
        var (plan, reason) = PlanRules.Validate(JsonDocument.Parse(json).RootElement);

        Assert.Null(plan);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void APurposeOrRationaleOverTheLimit_IsRefused()
    {
        var longPurpose = JsonSerializer.SerializeToElement(new
        {
            steps = new[]
            {
                new { agent = "diagnostic", purpose = new string('x', PlanRules.MaxPurposeLength + 1) },
                new { agent = "strategist", purpose = "ok" }
            },
            rationale = "r"
        });
        var longRationale = Plan(new string('x', PlanRules.MaxRationaleLength + 1), "diagnostic", "strategist");

        Assert.Null(PlanRules.Validate(longPurpose).Plan);
        Assert.Null(PlanRules.Validate(longRationale).Plan);
    }

    [Fact]
    public void TheFallback_IsEveryAgentInOrder_ClarifierIncluded_WithTheReason()
    {
        var plan = PlanRules.Fallback("The planner was down.");

        Assert.Equal(PlanRules.SourceFallback, plan.Source);
        Assert.Equal("The planner was down.", plan.Note);
        Assert.Equal(PlanRules.PipelineOrder, plan.Steps.Select(s => s.Agent));
        Assert.All(plan.Steps, s => Assert.Equal(PlanRules.AddedByApi, s.AddedBy));
    }

    [Fact]
    public void MarkStep_SettlesTheFirstPendingStepForThatAgent_AndNothingElse()
    {
        var plan = PlanRules.AppendRediagnosis(
            PlanRules.MarkStep(PlanRules.MarkStep(PlanRules.Fallback("n"), "diagnostic", PlanStepStatus.Completed),
                "strategist", PlanStepStatus.Failed),
            reopenedWorkOrderId: 12);

        var marked = PlanRules.MarkStep(plan, "diagnostic", PlanStepStatus.Completed);

        Assert.Equal(
            new[] { "pending", "completed", "failed", "completed", "pending" },
            marked.Steps.Select(s => s.Status));
        // A plan with nothing pending for that agent comes back as it was.
        Assert.Equal(marked.Steps, PlanRules.MarkStep(marked, "diagnostic", PlanStepStatus.Failed).Steps);
    }

    [Fact]
    public void AppendRediagnosis_AddsTwoApiSteps_OnceOnly()
    {
        var settled = PlanRules.MarkStep(PlanRules.MarkStep(PlanRules.Fallback("n"),
            "diagnostic", PlanStepStatus.Completed), "strategist", PlanStepStatus.Completed);

        var once = PlanRules.AppendRediagnosis(settled, reopenedWorkOrderId: 31);
        var twice = PlanRules.AppendRediagnosis(once, reopenedWorkOrderId: 31);

        Assert.Equal(5, once.Steps.Count);
        Assert.Equal(new[] { "diagnostic", "strategist" }, once.Steps.Skip(3).Select(s => s.Agent));
        Assert.Contains("#31", once.Steps[3].Purpose);
        // A restart re-running the same reopened workflow does not append it again.
        Assert.Equal(once.Steps, twice.Steps);
    }

    [Fact]
    public void AStoredPlan_RoundTrips_AndAnUnreadableOneIsNull_NeverThrown()
    {
        var plan = PlanRules.Validate(Plan("r", "diagnostic", "strategist")).Plan!;

        var read = PlanRules.Read(PlanRules.Serialize(plan));

        Assert.Equal(plan.Source, read!.Source);
        Assert.Equal(plan.Steps, read.Steps);
        Assert.Null(PlanRules.Read(null));
        Assert.Null(PlanRules.Read("not json"));
        Assert.Null(PlanRules.Read("""{"source": "planner"}"""));
    }
}
