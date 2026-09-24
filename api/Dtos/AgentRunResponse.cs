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
    [property: JsonPropertyName("strategy")] JsonElement Strategy = default)
{
    /// <summary>The status string the agent sends when it could not produce real output.</summary>
    public const string SafeFailureStatus = "safe_failure";

    /// <summary>
    /// The AgentStep.AgentName the diagnostic's result is recorded under. Taken from WHICH
    /// FIELD the result arrived in, never from the envelope's own `agent` value: the field is
    /// the contract, and the approval queue reads the step back by this exact name.
    /// </summary>
    public const string DiagnosticAgentName = "diagnostic";

    /// <summary>The AgentStep.AgentName the strategist's proposal is recorded under.</summary>
    public const string StrategistAgentName = "strategist";

    public bool IsSafeFailure => string.Equals(Status, SafeFailureStatus, StringComparison.Ordinal);

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

        results.Add(new DownstreamAgentResult(agentName, succeeded, outputJson, error));
    }

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
    string? Error);
