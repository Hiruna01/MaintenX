using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// The agent service's reply to POST /run. snake_case for the same reason as
/// AgentRunRequest.
///
/// A reply with status "safe_failure" is still a 200 with a well-formed body — that is the
/// agent's contract, not an error condition on the wire — so the runner has to read this
/// field rather than relying on the status code.
/// </summary>
public record AgentRunResponse(
    [property: JsonPropertyName("workflow_id")] int WorkflowId,
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("status")] string Status,

    // Kept as raw JSON rather than mapped to C# types: it is written straight into the
    // AgentStep.PayloadJson jsonb column, and re-modelling the clarifier's question shape
    // here would be a second copy of a schema that already lives in agent/schemas.py.
    [property: JsonPropertyName("output")] JsonElement Output,

    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("tool_calls")] JsonElement ToolCalls,

    // The diagnostic's and the strategist's envelopes, which the graph attaches BESIDE the
    // clarifier's fields rather than in place of them. Raw JSON for the same reason as
    // Output; absent (Undefined) or null when the graph did not run that agent. Read only
    // through DownstreamResults().
    [property: JsonPropertyName("diagnosis")] JsonElement Diagnosis = default,
    [property: JsonPropertyName("strategy")] JsonElement Strategy = default,

    // The planner's envelope — the structured plan the rest of the run was delegated from.
    // Absent on a resumed run, which starts at the diagnostic under the plan already stored,
    // and on a reply from an agent service that predates the planner. Read only through
    // PlannerResult().
    [property: JsonPropertyName("plan")] JsonElement Plan = default,

    // What the agent in the TOP-LEVEL fields reports about itself: how many LLM attempts it
    // took (1, or 2 with the one retry) and how long it ran. Null when it did not say — an
    // older agent service — in which case the runner falls back to the whole call's time.
    [property: JsonPropertyName("attempts")] int? Attempts = null,
    [property: JsonPropertyName("duration_ms")] int? DurationMs = null,

    // The tokens the provider reported for that same agent's model calls. Raw JSON, read only
    // through ReportedUsage: a reply without it, or with an odd shape, is "not reported" —
    // never zero, and never an exception in a background worker.
    [property: JsonPropertyName("usage")] JsonElement Usage = default,

    // The verification agent's envelope — the only field a verification run fills. Absent on
    // every report run. Read only through VerificationResult().
    [property: JsonPropertyName("verification")] JsonElement Verification = default)
{
    /// <summary>The status string the agent sends when it could not produce real output.</summary>
    public const string SafeFailureStatus = "safe_failure";

    /// <summary>
    /// The AgentStep.AgentName the clarifier's run is recorded under — the agent service's
    /// own name for it, and the runner's fallback when the call never returned one.
    /// AnalyticsService reads clarifier runs back by this name.
    /// </summary>
    public const string ClarifierAgentName = "clarifier";

    /// <summary>
    /// The AgentStep.AgentName the diagnostic's result is recorded under. Taken from WHICH
    /// FIELD the result arrived in, never from the envelope's own `agent` value: the field is
    /// the contract, and the approval queue reads the step back by this exact name.
    /// </summary>
    public const string DiagnosticAgentName = "diagnostic";

    /// <summary>The AgentStep.AgentName the strategist's proposal is recorded under.</summary>
    public const string StrategistAgentName = "strategist";

    /// <summary>
    /// The AgentStep.AgentName a verification run is recorded under, and the name the agent
    /// sends on its tool calls — which is what the tool router's one exception for an ended
    /// workflow looks for (see VerificationAgentService.IsJudgingOnWorkflowAsync).
    /// </summary>
    public const string VerificationAgentName = "verification";

    /// <summary>
    /// True when the clarifier ran in this call — its fields are the top-level ones. False
    /// when the planner sent the run straight to the diagnostic, or on a resumed run: the
    /// agent service then puts the diagnostic's name at the top level instead.
    /// </summary>
    public bool ClarifierRan => string.Equals(Agent, ClarifierAgentName, StringComparison.Ordinal);

    public bool IsSafeFailure => string.Equals(Status, SafeFailureStatus, StringComparison.Ordinal);

    /// <summary>The top-level agent's reported token usage, or null when it reported none.</summary>
    public AgentTokenUsage? ReportedUsage => AgentTokenUsage.Read(Usage);

    /// <summary>
    /// How many questions the clarifier asked. Read defensively: this is the one place the
    /// API reaches into the agent's payload shape, and a missing or oddly-shaped `output`
    /// must not throw inside a background worker.
    /// </summary>
    public int QuestionCount => QuestionsElement(out var questions) ? questions.GetArrayLength() : 0;

    /// <summary>
    /// The clarifier's questions, parsed into the shape the ClarificationQuestion rows are
    /// written from.
    ///
    /// THIS IS THE ONLY PLACE THE API READS THE AGENT'S QUESTION SHAPE, on purpose. The
    /// payload itself is still stored verbatim on the AgentStep, so nothing is lost if
    /// this parse drops something, and keeping the translation in one method means the
    /// agent's snake_case answer_type values ("yes_no", "single_select", "short_text") are
    /// mapped to the C# enum in exactly one place rather than wherever a reader happens to
    /// need them.
    ///
    /// Defensive throughout, because the caller is a background worker with no request to
    /// surface an exception on: anything that is not a well-formed question is SKIPPED
    /// rather than thrown on or half-stored. The caller compares the returned count with
    /// <see cref="QuestionCount"/> to notice that it happened.
    /// </summary>
    public IReadOnlyList<ParsedClarifyingQuestion> ParseQuestions()
    {
        var parsed = new List<ParsedClarifyingQuestion>();

        if (!QuestionsElement(out var questions))
        {
            return parsed;
        }

        foreach (var question in questions.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Object
                || !question.TryGetProperty("question_text", out var text)
                || text.ValueKind != JsonValueKind.String
                || !question.TryGetProperty("answer_type", out var type)
                || type.ValueKind != JsonValueKind.String
                || ToAnswerType(type.GetString()) is not { } answerType)
            {
                continue;
            }

            var questionText = text.GetString();

            if (string.IsNullOrWhiteSpace(questionText))
            {
                continue;
            }

            // Options are kept ONLY for SingleSelect. The agent's own schema already
            // refuses them on the other two types, but a question that somehow arrived
            // carrying them would otherwise be stored in a shape no client can render —
            // so they are dropped here rather than trusted through.
            string? optionsJson = null;

            if (answerType == AnswerType.SingleSelect)
            {
                if (!question.TryGetProperty("options", out var options)
                    || options.ValueKind != JsonValueKind.Array
                    || options.GetArrayLength() == 0)
                {
                    // A picker with no choices is not a question anyone can answer.
                    continue;
                }

                optionsJson = options.GetRawText();
            }

            // DisplayOrder is the position in the agent's own array — the order it chose
            // to ask in — and is assigned after the skips above so the surviving questions
            // are numbered 0, 1, ... with no gap where a dropped one was.
            parsed.Add(new ParsedClarifyingQuestion(
                questionText, answerType, optionsJson, parsed.Count));
        }

        return parsed;
    }

    /// <summary>
    /// Maps the agent service's snake_case answer_type onto the C# enum. Returns null for
    /// anything unrecognised, which the caller treats as a question to skip — never as a
    /// default, because guessing a control type would put a question in front of a
    /// reporter in a shape the agent did not ask for.
    /// </summary>
    private static AnswerType? ToAnswerType(string? value) => value switch
    {
        "yes_no" => AnswerType.YesNo,
        "single_select" => AnswerType.SingleSelect,
        "short_text" => AnswerType.ShortText,
        _ => null
    };

    /// <summary>
    /// The diagnostic's and the strategist's results, in graph order, ready to be written as
    /// one agent-level AgentStep each. An agent the graph did not run is simply absent.
    ///
    /// Nothing here interprets the output — a diagnosis is stored verbatim, exactly as the
    /// clarifier's questions are, and read back into a typed shape only by AgentAnalysis when
    /// somebody asks for it. Defensive like ParseQuestions: an envelope that is not an object
    /// is skipped, and one whose output is missing is recorded as a safe failure rather than
    /// as a success with nothing in it, because there is no such thing as an empty diagnosis.
    /// </summary>
    public IReadOnlyList<DownstreamAgentResult> DownstreamResults()
    {
        var results = new List<DownstreamAgentResult>();

        AddDownstream(results, DiagnosticAgentName, Diagnosis);
        AddDownstream(results, StrategistAgentName, Strategy);

        return results;
    }

    private static void AddDownstream(List<DownstreamAgentResult> results, string agentName, JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var status = StringProperty(envelope, "status");
        var error = StringProperty(envelope, "error");

        string? outputJson = envelope.TryGetProperty("output", out var output)
                             && output.ValueKind == JsonValueKind.Object
            ? output.GetRawText()
            : null;

        var succeeded = outputJson is not null
                        && !string.Equals(status, SafeFailureStatus, StringComparison.Ordinal);

        results.Add(new DownstreamAgentResult(
            agentName, succeeded, outputJson, error,
            IntProperty(envelope, "attempts"), IntProperty(envelope, "duration_ms"), UsageProperty(envelope)));
    }

    /// <summary>
    /// The planner's result, or null when the reply carries none. Read like the downstream
    /// envelopes — defensively, and verbatim: whether the plan is USABLE is PlanRules'
    /// decision, not this parse's, so an output that is present is handed over whatever it
    /// contains.
    /// </summary>
    public PlannerAgentResult? PlannerResult()
    {
        if (Plan.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var status = StringProperty(Plan, "status");
        var hasOutput = Plan.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object;

        return new PlannerAgentResult(
            Succeeded: hasOutput && !string.Equals(status, SafeFailureStatus, StringComparison.Ordinal),
            Output: hasOutput ? output.Clone() : default,
            OutputJson: hasOutput ? output.GetRawText() : null,
            Error: StringProperty(Plan, "error"),
            Attempts: IntProperty(Plan, "attempts"),
            DurationMs: IntProperty(Plan, "duration_ms"),
            Usage: UsageProperty(Plan));
    }

    /// <summary>
    /// The verification agent's verdict, or null when the reply carries no verification
    /// envelope at all — which, on a verification run, is the graph breaking its promise and is
    /// treated by the caller like a call that failed.
    ///
    /// Read defensively and kept verbatim, like the other envelopes. It SUCCEEDED only when the
    /// status is not a safe failure AND the output is an object whose `outcome` is a string:
    /// the outcome is what the check's AgentOutcome is written from, and there is no such thing
    /// as a verdict without one. Reason and evidence are carried as they came; evidence as the
    /// raw JSON of its array, for AgentEvidenceJson.
    /// </summary>
    public VerificationAgentResult? VerificationResult()
    {
        if (Verification.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var status = StringProperty(Verification, "status");
        var hasOutput = Verification.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object;
        var outcome = hasOutput ? StringProperty(output, "outcome") : null;

        string? evidenceJson = hasOutput
                               && output.TryGetProperty("evidence", out var evidence)
                               && evidence.ValueKind == JsonValueKind.Array
            ? evidence.GetRawText()
            : null;

        var succeeded = !string.IsNullOrWhiteSpace(outcome)
                        && !string.Equals(status, SafeFailureStatus, StringComparison.Ordinal);

        return new VerificationAgentResult(
            Succeeded: succeeded,
            OutputJson: hasOutput ? output.GetRawText() : null,
            Outcome: succeeded ? outcome : null,
            Reason: succeeded ? StringProperty(output, "reason") : null,
            EvidenceJson: succeeded ? evidenceJson : null,
            Error: StringProperty(Verification, "error")
                   ?? (succeeded || !hasOutput ? null : "The verification agent's output carried no outcome."),
            Attempts: IntProperty(Verification, "attempts"),
            DurationMs: IntProperty(Verification, "duration_ms"),
            Usage: UsageProperty(Verification));
    }

    private static int? IntProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static AgentTokenUsage? UsageProperty(JsonElement envelope) =>
        envelope.TryGetProperty("usage", out var usage) ? AgentTokenUsage.Read(usage) : null;

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private bool QuestionsElement(out JsonElement questions)
    {
        if (Output.ValueKind == JsonValueKind.Object
            && Output.TryGetProperty("questions", out questions)
            && questions.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        questions = default;
        return false;
    }
}

/// <summary>
/// One clarifier question, translated out of the agent's JSON and ready to be written as
/// a ClarificationQuestion row. Not a response DTO — nothing serialises this; it exists so
/// the parse and the persist are separate, testable steps.
/// </summary>
public record ParsedClarifyingQuestion(
    string QuestionText,
    AnswerType AnswerType,
    string? OptionsJson,
    int DisplayOrder);

/// <summary>
/// One downstream agent's result out of a /run reply — the diagnostic's or the strategist's —
/// ready to be written as an agent-level AgentStep. <see cref="OutputJson"/> is the agent's
/// output verbatim, or null when it produced none.
/// </summary>
public record DownstreamAgentResult(
    string AgentName,
    bool Succeeded,
    string? OutputJson,
    string? Error,

    // What the agent reported about itself; null when it did not say.
    int? Attempts = null,
    int? DurationMs = null,
    AgentTokenUsage? Usage = null);

/// <summary>
/// The planner's result out of a /run reply. <see cref="Output"/> is the plan as the agent
/// sent it — PlanRules decides whether it is used — and <see cref="OutputJson"/> the same,
/// verbatim, for its AgentStep.
/// </summary>
public record PlannerAgentResult(
    bool Succeeded,
    JsonElement Output,
    string? OutputJson,
    string? Error,
    int? Attempts,
    int? DurationMs,
    AgentTokenUsage? Usage = null);

/// <summary>
/// The verification agent's result out of a /run reply. <see cref="OutputJson"/> is its output
/// verbatim, for the AgentStep; the other fields are what the check's columns are written from,
/// and are null unless <see cref="Succeeded"/>.
/// </summary>
public record VerificationAgentResult(
    bool Succeeded,
    string? OutputJson,
    string? Outcome,
    string? Reason,
    string? EvidenceJson,
    string? Error,
    int? Attempts,
    int? DurationMs,
    AgentTokenUsage? Usage = null);

/// <summary>
/// The tokens the PROVIDER reported for one agent's model calls — both attempts added
/// together by the agent service when it took the retry. Stored on AgentStep as
/// PromptTokens / CompletionTokens and costed in C# by AgentMetricsService; never estimated.
/// </summary>
public record AgentTokenUsage(int PromptTokens, int CompletionTokens)
{
    /// <summary>
    /// Reads an agent envelope's `usage` object. Null — "not reported", which is not zero —
    /// when it is absent, null, or not two whole non-negative numbers. Never throws: the
    /// caller is a background worker, and usage is observability, not something a run can
    /// fail over.
    /// </summary>
    public static AgentTokenUsage? Read(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Count(usage, "prompt_tokens") is { } prompt && Count(usage, "completion_tokens") is { } completion
            ? new AgentTokenUsage(prompt, completion)
            : null;
    }

    private static int? Count(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var count)
        && count >= 0
            ? count
            : null;
}
