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
using Microsoft.Extensions.Logging;
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
    // Submitting the answers — POST/GET /api/reports/{id}/clarifications.
    //
    // Over HTTP, unlike the persistence tests above, because the ORDER of the checks and
    // the STATUS CODE each one produces are the contract this endpoint owes its clients.
    // Every one of them is a C# rule; none is delegated to the agent.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetClarifications_WithoutAToken_Is401()
    {
        var anonymous = _factory.CreateClient();
        var (_, reportId, _, _, _) = await CreateAwaitingClarificationReportAsync();

        var response = await anonymous.GetAsync($"/api/reports/{reportId}/clarifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitAnswers_WithoutAToken_Is401NotAnyOfTheOtherRefusals()
    {
        // 401 and 403 stay distinct: no token is "who are you?", and it is answered before
        // anything about the report is looked at.
        var anonymous = _factory.CreateClient();
        var (_, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications", AnswerAll(questions), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetClarifications_ForAReportWithNoQuestions_Is200AndEmptyNotA404()
    {
        // An empty list and a missing report are different answers. A report nobody has
        // clarified yet has nothing to show, and that is not a failure.
        var (client, _) = await CreateAuthenticatedClientAsync();
        var (reportId, _, _) = await CreateReportWithWorkflowAsync();

        var response = await client.GetAsync($"/api/reports/{reportId}/clarifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var questions = await response.Content
            .ReadFromJsonAsync<List<ClarificationQuestionDto>>(JsonOptions);

        Assert.Empty(questions!);
    }

    [Fact]
    public async Task GetClarifications_ForAReportThatDoesNotExist_Is404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/999999/clarifications");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitAnswers_WritesTheAnswers_ClarifiesTheReportAndResumesTheWorkflow()
    {
        var (client, reportId, workflowId, userId, questions) =
            await CreateAwaitingClarificationReportAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications", AnswerAll(questions), JsonOptions);

        // 204: the answers are recorded and the exchange is over. Nothing is returned
        // because there is no next turn to describe.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The same questions read back, now carrying their answers — the form the client
        // rendered, not a transcript.
        var answered = await client.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{reportId}/clarifications", JsonOptions);

        Assert.Equal(2, answered!.Count);
        Assert.Equal("Yes", answered[0].AnswerText);
        Assert.Equal("Today", answered[1].AnswerText);
        Assert.All(answered, q => Assert.NotNull(q.AnsweredAt));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The fault is no longer waiting on its reporter. A C# transition, written in the
        // same SaveChanges as the answers, so the two can never disagree.
        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(ReportStatus.Clarified, report.Status);

        // And the run that asked is free to carry on. Which state follows which is a
        // business rule and lives in C#, never in a prompt.
        var workflow = await db.AgentWorkflows.AsNoTracking().FirstAsync(w => w.Id == workflowId);
        Assert.Equal(WorkflowState.Diagnosing, workflow.CurrentState);

        // Who answered comes from the JWT sub claim, never from the body —
        // SubmitAnswersRequest has no field for it.
        var answers = await db.ClarificationAnswers.AsNoTracking()
            .Where(a => answered.Select(q => q.Id).Contains(a.ClarificationQuestionId))
            .ToListAsync();

        Assert.Equal(2, answers.Count);
        Assert.All(answers, a => Assert.Equal(userId, a.AnsweredByUserId));
    }

    [Fact]
    public async Task SubmitAnswers_ReQueuesTheWorkflowForTheBackgroundRunner()
    {
        // Through the service with a recording queue rather than over HTTP: the real
        // IWorkflowQueue is a shared singleton channel that nothing drains in tests, so
        // asserting on it would depend on what every other test in this class enqueued.
        var (_, reportId, workflowId, userId, questions) =
            await CreateAwaitingClarificationReportAsync();

        var queue = new RecordingWorkflowQueue();

        using var scope = _factory.Services.CreateScope();

        var clarifications = new ClarificationService(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            queue,
            scope.ServiceProvider.GetRequiredService<ILogger<ClarificationService>>());

        var result = await clarifications.SubmitAnswersAsync(
            reportId, userId, AnswerAll(questions));

        Assert.Equal(SubmitAnswersOutcome.Success, result.Outcome);

        // The runner picks the workflow up again from here. Handed over AFTER the save, so
        // it cannot open its scope and find no answers.
        Assert.Equal(new[] { workflowId }, queue.Enqueued);
    }

    [Fact]
    public async Task SubmitAnswers_ForAReportThatDoesNotExist_Is404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports/999999/clarifications",
            new SubmitAnswersRequest(new[] { new SubmittedAnswer(1, "Yes") }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitAnswers_ByAnyoneButTheReporter_Is403AndLeaksNothingAboutTheReport()
    {
        var (_, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        // A different signed-in user. Valid token, wrong person — 403, not 401 and not 404.
        var (stranger, _) = await CreateAuthenticatedClientAsync();

        var response = await stranger.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications", AnswerAll(questions), JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Identity is checked BEFORE state and content, so a stranger sending a body that
        // is also wrong still gets 403 and learns nothing about what the report is carrying.
        var nonsense = await stranger.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[] { new SubmittedAnswer(999999, "Nope") }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, nonsense.StatusCode);

        // And nothing was written.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(ReportStatus.AwaitingClarification, report.Status);
    }

    [Fact]
    public async Task SubmitAnswers_ToAReportThatIsNotAwaitingClarification_Is409()
    {
        // Submitted, not AwaitingClarification: nothing has been asked, so there is nothing
        // an answer could mean. The body may be perfectly well formed, which is why this is
        // a conflict rather than a 400.
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var created = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The ceiling fan is making a grinding noise.", roomId),
            JsonOptions);

        var report = await created.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);
        Assert.Equal(ReportStatus.Submitted, report!.Status);

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{report.Id}/clarifications",
            new SubmitAnswersRequest(new[] { new SubmittedAnswer(1, "Yes") }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // State is checked before content, so the bad question id above never gets looked at.
        Assert.Equal(userId, report.ReporterId);
    }

    [Fact]
    public async Task SubmitAnswers_NamingAQuestionFromAnotherReport_Is400NotA404()
    {
        var (client, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        // A real question id, belonging to a different report. The report itself was found,
        // so the body is what is wrong.
        var (_, _, _, _, otherQuestions) = await CreateAwaitingClarificationReportAsync();

        var answers = new SubmitAnswersRequest(new[]
        {
            new SubmittedAnswer(questions[0].Id, "Yes"),
            new SubmittedAnswer(otherQuestions[1].Id, "Today")
        });

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications", answers, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Nothing was written, not even the answer that named a real question of this
        // report — the form is accepted whole or not at all.
        Assert.Equal(2, await UnansweredCountAsync(reportId));
    }

    [Fact]
    public async Task SubmitAnswers_LeavingAQuestionOut_Is400BecauseTheFormGoesBackWhole()
    {
        // No partial submissions: finishing one later would need a second round, and a
        // second round is the conversation this system does not have.
        var (client, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[] { new SubmittedAnswer(questions[0].Id, "Yes") }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nothing partial was written — the second question is still open, and so is the
        // first one it would have been paired with.
        Assert.Equal(2, await UnansweredCountAsync(reportId));
    }

    [Fact]
    public async Task SubmitAnswers_WithASingleSelectValueThatWasNeverOffered_Is400()
    {
        // THIS is what makes AnswerType a constraint rather than a suggestion. A picker
        // whose value is never checked against its own options is a text box wearing a
        // picker's name, and unbounded text is the thing AnswerType exists to prevent.
        var (client, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        Assert.Equal(AnswerType.SingleSelect, questions[1].AnswerType);
        Assert.DoesNotContain("Since the rains started", questions[1].Options!);

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[]
            {
                new SubmittedAnswer(questions[0].Id, "Yes"),
                new SubmittedAnswer(questions[1].Id, "Since the rains started")
            }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, await UnansweredCountAsync(reportId));
    }

    [Theory]
    [InlineData("Maybe")]
    [InlineData("yes")]
    [InlineData("Yes ")]
    [InlineData("Yes, but only after it has been on for about an hour")]
    public async Task SubmitAnswers_WithAYesNoAnswerThatIsNotExactlyYesOrNo_Is400(string answer)
    {
        // A yes/no toggle is a picker with two options, and it is held to the same rule as
        // any other picker. Without this a client could put 100 characters of free text
        // where "Yes" belongs — a message box behind a toggle's name, which is exactly the
        // chat interface AnswerType exists to rule out.
        //
        // Matched ordinally, so "yes" and "Yes " are refused too: both clients send the
        // exact string, and loosening it here would be guessing at what the reporter meant.
        var (client, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        Assert.Equal(AnswerType.YesNo, questions[0].AnswerType);

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[]
            {
                new SubmittedAnswer(questions[0].Id, answer),
                new SubmittedAnswer(questions[1].Id, questions[1].Options![0])
            }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nothing written, not even the valid single-select answer beside it, and the
        // report is still waiting on its reporter.
        Assert.Equal(2, await UnansweredCountAsync(reportId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(ReportStatus.AwaitingClarification, report.Status);
    }

    [Theory]
    [InlineData("Yes")]
    [InlineData("No")]
    public async Task SubmitAnswers_WithYesOrNo_IsAccepted(string answer)
    {
        var (client, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[]
            {
                new SubmittedAnswer(questions[0].Id, answer),
                new SubmittedAnswer(questions[1].Id, questions[1].Options![0])
            }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SubmitAnswers_ShortTextIsCappedButNotCheckedAgainstAnyOptions()
    {
        // The other side of the option check: typed text is bounded by its 100-character
        // cap, not by a list. A short-text answer that happens not to be "Yes" is fine.
        const string output = """
            {
              "questions": [
                { "question_text": "Is it leaking?", "answer_type": "yes_no" },
                { "question_text": "What does the display show?",
                  "answer_type": "short_text" }
              ]
            }
            """;

        var (client, reportId, _, _, questions) =
            await CreateAwaitingClarificationReportAsync(output);

        Assert.Equal(AnswerType.ShortText, questions[1].AnswerType);

        var response = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[]
            {
                new SubmittedAnswer(questions[0].Id, "No"),
                new SubmittedAnswer(questions[1].Id, "A blue 'no signal' box, then black")
            }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SubmitAnswers_Twice_Is409AndTheFirstAnswersStand()
    {
        // ONE ANSWER, NOT A THREAD. The first answer is evidence, not a draft, so a
        // re-submission is refused rather than allowed to overwrite it.
        //
        // The unique index does NOT produce this: the service has loaded each question's
        // answer to check it, and inside one DbContext EF resolves the required one-to-one
        // conflict itself and would succeed by REPLACING. This 409 is an explicit query.
        var (client, reportId, _, _, questions) = await CreateAwaitingClarificationReportAsync();

        var first = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications", AnswerAll(questions), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Put the report back where it was, so this reaches the already-answered check
        // rather than stopping at the status check in front of it.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var report = await db.Reports.FirstAsync(r => r.Id == reportId);
            report.Status = ReportStatus.AwaitingClarification;
            await db.SaveChangesAsync();
        }

        var second = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[]
            {
                new SubmittedAnswer(questions[0].Id, "No"),
                new SubmittedAnswer(questions[1].Id, "This week")
            }),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var answered = await client.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{reportId}/clarifications", JsonOptions);

        Assert.Equal("Yes", answered![0].AnswerText);
        Assert.Equal("Today", answered[1].AnswerText);
    }

    /// <summary>Records what was handed over instead of queueing it. See the test above.</summary>
    private sealed class RecordingWorkflowQueue : IWorkflowQueue
    {
        public List<int> Enqueued { get; } = new();

        public ValueTask EnqueueAsync(int workflowId, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(workflowId);
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Nothing drains the queue in tests.");
    }

    /// <summary>Answers every question with something valid for its own answer type.</summary>
    private static SubmitAnswersRequest AnswerAll(IReadOnlyList<ClarificationQuestionDto> questions) =>
        new(questions
            .Select(q => new SubmittedAnswer(
                q.Id,
                q.AnswerType == AnswerType.SingleSelect ? q.Options![0] : "Yes"))
            .ToList());

    private async Task<int> UnansweredCountAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();

        return (await clarifications.GetForReportAsync(reportId))
            .Count(q => q.AnswerText is null);
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

    /// <summary>
    /// A report sitting exactly where the submit endpoint expects to find one: clarified
    /// by a run, AwaitingClarification, with the stub's two questions — or those in
    /// <paramref name="agentOutput"/> — unanswered against it. The client returned is the
    /// reporter's.
    /// </summary>
    private async Task<(HttpClient Client, int ReportId, int WorkflowId, int UserId,
        IReadOnlyList<ClarificationQuestionDto> Questions)> CreateAwaitingClarificationReportAsync(
            string agentOutput = StubOutput)
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var created = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The projector in the lab keeps cutting out mid-lecture.", roomId),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = await created.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        var workflows = await client.GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
            "/api/workflows?page=1&pageSize=50", JsonOptions);

        var workflow = Assert.Single(workflows!.Items.Where(w => w.ReportId == report!.Id));

        var response = AgentResponse(agentOutput, out var document);
        using var _ = document;
        var parsed = response.ParseQuestions();

        using (var scope = _factory.Services.CreateScope())
        {
            var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();
            await clarifications.RecordQuestionsAsync(report!.Id, workflow.Id, parsed);

            // Then exactly what the runner does next: the workflow pauses for the answers.
            // Only from AwaitingClarification may "the reporter answered" move it on.
            var workflowService = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            await workflowService.CompleteClarificationAsync(workflow.Id, parsed.Count);
        }

        var questions = await client.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{report!.Id}/clarifications", JsonOptions);

        Assert.Equal(parsed.Count, questions!.Count);

        return (client, report.Id, workflow.Id, userId, questions);
    }
}
