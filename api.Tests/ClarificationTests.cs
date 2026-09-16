using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace api.Tests;

/// <summary>
/// The clarification persistence path: the clarifier's questions become rows the
/// application can read and answer, beside the AgentStep that records what the agent said.
///
/// These reach IClarificationService through a DI scope rather than over HTTP, because
/// there is no endpoint yet — the only production caller is WorkflowRunner, which
/// ApiFactory removes from the container so a background writer cannot race a test's
/// assertions. Calling the service directly exercises the same code the runner does,
/// against the same real database, with the timing under the test's control.
/// </summary>
public class ClarificationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ClarificationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // ---------------------------------------------------------------------------
    // Parsing the agent's payload — no database, no HTTP.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The exact payload agent/schemas.py returns in STUB_MODE. Pinned here so the two
    /// contracts cannot drift apart unnoticed: if the agent's question shape changes, this
    /// is the test that goes red rather than a silently empty ClarificationQuestions table.
    /// </summary>
    private const string StubOutput = """
        {
          "questions": [
            { "question_text": "Is the equipment completely unresponsive?",
              "answer_type": "yes_no" },
            { "question_text": "How long has the problem been happening?",
              "answer_type": "single_select",
              "options": ["Today", "This week", "Longer than a week"] }
          ]
        }
        """;

    private static AgentRunResponse AgentResponse(string outputJson, out JsonDocument document)
    {
        document = JsonDocument.Parse(outputJson);

        using var empty = JsonDocument.Parse("[]");

        return new AgentRunResponse(
            WorkflowId: 1,
            Agent: "clarifier",
            Status: "ok",
            Output: document.RootElement,
            Error: null,
            ToolCalls: empty.RootElement.Clone());
    }

    [Fact]
    public void ParseQuestions_MapsTheAgentsSnakeCaseAnswerTypesOntoTheEnum()
    {
        var response = AgentResponse(StubOutput, out var document);
        using var _ = document;

        var questions = response.ParseQuestions();

        Assert.Equal(2, questions.Count);
        Assert.Equal(response.QuestionCount, questions.Count);

        Assert.Equal("Is the equipment completely unresponsive?", questions[0].QuestionText);
        Assert.Equal(AnswerType.YesNo, questions[0].AnswerType);
        // Options belong to SingleSelect alone — a yes/no question carrying them would
        // render as a control the agent never asked for.
        Assert.Null(questions[0].OptionsJson);
        Assert.Equal(0, questions[0].DisplayOrder);

        Assert.Equal(AnswerType.SingleSelect, questions[1].AnswerType);
        Assert.NotNull(questions[1].OptionsJson);
        Assert.Equal(
            new[] { "Today", "This week", "Longer than a week" },
            JsonSerializer.Deserialize<string[]>(questions[1].OptionsJson!));
        Assert.Equal(1, questions[1].DisplayOrder);
    }

    [Fact]
    public void ParseQuestions_SkipsMalformedQuestionsAndRenumbersTheRest()
    {
        // Three things that must never reach a reporter: an answer_type this API does not
        // know how to render, a picker with no choices, and a question with no text. None
        // of them can come out of the agent's own Pydantic validation — which is exactly
        // why the API must not assume they cannot arrive.
        const string output = """
            {
              "questions": [
                { "question_text": "Which floor is it on?", "answer_type": "free_text" },
                { "question_text": "Pick one", "answer_type": "single_select", "options": [] },
                { "question_text": "", "answer_type": "yes_no" },
                { "question_text": "Is it leaking?", "answer_type": "yes_no" }
              ]
            }
            """;

        var response = AgentResponse(output, out var document);
        using var _ = document;

        var questions = response.ParseQuestions();

        var kept = Assert.Single(questions);
        Assert.Equal("Is it leaking?", kept.QuestionText);

        // Renumbered from zero, with no gap where the three dropped ones were — a form
        // cannot render a DisplayOrder of 3 as the first field.
        Assert.Equal(0, kept.DisplayOrder);

        // And the shortfall is visible to the caller, which is how WorkflowRunner knows to
        // warn that the two contracts have drifted.
        Assert.Equal(4, response.QuestionCount);
    }

    [Fact]
    public void ParseQuestions_OnASafeFailurePayload_ReturnsNothingRatherThanThrowing()
    {
        // A safe failure is a normal 200 carrying empty output. The runner is a background
        // worker with no request to surface an exception on, so this must be a quiet empty
        // list rather than anything that throws.
        var response = AgentResponse("""{"questions": []}""", out var document);
        using var _ = document;

        Assert.Empty(response.ParseQuestions());
    }

    // ---------------------------------------------------------------------------
    // Persistence.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RecordQuestions_WritesTheRowsAndMovesTheReportToAwaitingClarification()
    {
        var (reportId, workflowId, _) = await CreateReportWithWorkflowAsync();

        var response = AgentResponse(StubOutput, out var document);
        using var _ = document;

        using var scope = _factory.Services.CreateScope();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();

        var written = await clarifications.RecordQuestionsAsync(
            reportId, workflowId, response.ParseQuestions());

        Assert.Equal(2, written);

        var questions = await clarifications.GetForReportAsync(reportId);

        Assert.Equal(2, questions.Count);
        Assert.Equal(workflowId, questions[0].WorkflowId);
        Assert.Equal(AnswerType.YesNo, questions[0].AnswerType);
        Assert.Null(questions[0].Options);

        // The stored jsonb comes back as a list, so a client never parses a string that
        // came out of another string.
        Assert.Equal(AnswerType.SingleSelect, questions[1].AnswerType);
        Assert.Equal(
            new[] { "Today", "This week", "Longer than a week" }, questions[1].Options);

        // Unanswered, which is how a client knows which fields are still open.
        Assert.Null(questions[0].AnswerText);
        Assert.Null(questions[0].AnsweredAt);

        // The report now says it is waiting on its reporter. This is a deterministic
        // business rule in C#, set in the same SaveChanges as the rows above so the two
        // can never disagree.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(ReportStatus.AwaitingClarification, report.Status);
    }

    [Fact]
    public async Task RecordQuestions_WithNothingToAsk_LeavesTheReportSubmitted()
    {
        var (reportId, workflowId, _) = await CreateReportWithWorkflowAsync();

        using var scope = _factory.Services.CreateScope();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();

        var written = await clarifications.RecordQuestionsAsync(
            reportId, workflowId, Array.Empty<ParsedClarifyingQuestion>());

        Assert.Equal(0, written);
        Assert.Empty(await clarifications.GetForReportAsync(reportId));

        // Nothing needed clarifying, so nobody is being waited on. The report must not be
        // parked in AwaitingClarification with no questions to answer.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(ReportStatus.Submitted, report.Status);
    }

    [Fact]
    public async Task RecordQuestions_ForAReportThatDoesNotExist_IsALoggedNoOpNotAnException()
    {
        // The only caller is a background worker: there is no request to turn a missing
        // parent into a 404, and an unhandled foreign key violation there would park the
        // workflow rather than fail it cleanly.
        var response = AgentResponse(StubOutput, out var document);
        using var _ = document;

        using var scope = _factory.Services.CreateScope();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();

        var written = await clarifications.RecordQuestionsAsync(
            999_999, 999_999, response.ParseQuestions());

        Assert.Equal(0, written);
    }

    [Fact]
    public async Task ASecondAnswerToTheSameQuestionIsRejectedByTheDatabase()
    {
        var (reportId, workflowId, userId) = await CreateReportWithWorkflowAsync();

        var response = AgentResponse(StubOutput, out var document);
        using var _ = document;

        int questionId;

        using (var scope = _factory.Services.CreateScope())
        {
            var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();
            await clarifications.RecordQuestionsAsync(reportId, workflowId, response.ParseQuestions());

            questionId = (await clarifications.GetForReportAsync(reportId))[0].Id;

            scope.ServiceProvider.GetRequiredService<AppDbContext>()
                 .ClarificationAnswers.Add(NewAnswer(questionId, "Yes", userId));

            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        }

        // A SEPARATE SCOPE, so nothing is tracked. That is not tidiness — it is the whole
        // point of the test. Inside one DbContext that already tracks the first answer, EF
        // resolves the conflict itself: the relationship is a required one-to-one, so the
        // existing dependent is marked Deleted and the save SUCCEEDS by replacing it. The
        // unique index is what stops a writer that has not loaded the first answer, which
        // is every writer that only knows a question id.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClarificationAnswers.Add(NewAnswer(questionId, "No", userId));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // The first answer is untouched and readable back through its question.
        using (var scope = _factory.Services.CreateScope())
        {
            var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();
            var answered = (await clarifications.GetForReportAsync(reportId))[0];

            Assert.Equal("Yes", answered.AnswerText);
            Assert.NotNull(answered.AnsweredAt);
        }
    }

    private static ClarificationAnswer NewAnswer(int questionId, string text, int userId) =>
        new()
        {
            ClarificationQuestionId = questionId,
            AnswerText = text,
            AnsweredByUserId = userId,
            AnsweredAt = DateTime.UtcNow
        };

    [Fact]
    public async Task CreateReport_StartsWithNoClarificationQuestions()
    {
        // An empty list is not a failure: a report that has not been clarified yet simply
        // has nothing to show, and that must be distinguishable from a report not existing.
        var (reportId, _, _) = await CreateReportWithWorkflowAsync();

        using var scope = _factory.Services.CreateScope();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();

        Assert.Empty(await clarifications.GetForReportAsync(reportId));
    }

    [Fact]
    public async Task CreateReport_ReturnsTheNewNullableFields()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The lecture hall projector will not power on.", roomId),
            JsonOptions);

        var created = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        // Both null, and legitimately so: a reporter is not expected to know the asset tag,
        // and photo attachment is still the disabled TODO(photo) button on the Flutter form.
        Assert.Null(created!.AssetId);
        Assert.Null(created.PhotoUrl);
        Assert.Equal(ReportStatus.Submitted, created.Status);
    }

    // ---------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------

    private async Task<(HttpClient Client, int UserId)> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "ClarifyPass1", "Test Reporter", Role.Reporter),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    private async Task<int> CreateRoomAsync()
    {
        var client = _factory.CreateClient();

        var buildingResponse = await client.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await client.PostAsJsonAsync(
            "/api/rooms",
            new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        return room!.Id;
    }

    /// <summary>
    /// Files a report and returns it with the workflow POST /api/reports raised for it —
    /// the same pair the runner holds when it comes to persist questions.
    /// </summary>
    private async Task<(int ReportId, int WorkflowId, int UserId)> CreateReportWithWorkflowAsync()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The air conditioning unit is dripping onto the desks.", roomId),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        var workflows = await client.GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
            "/api/workflows?page=1&pageSize=50", JsonOptions);

        var workflow = Assert.Single(workflows!.Items.Where(w => w.ReportId == report!.Id));

        return (report!.Id, workflow.Id, userId);
    }
}
