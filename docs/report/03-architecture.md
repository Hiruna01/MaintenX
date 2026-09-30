# 3. System Architecture

This chapter describes the architecture of MaintenX as it is implemented in the repository at
the time of writing. Every rule stated here is attributed to the source file that enforces it,
and where a capability is only partly implemented the text says so. The four figures referred
to are Mermaid sources under `docs/report/diagrams/`.

| Figure | Source file | Shows |
| --- | --- | --- |
| 3.1 | `diagrams/system-architecture.mmd` | Runtime components, every connection, its protocol and its authentication |
| 3.2 | `diagrams/workflow-state-machine.mmd` | The workflow state machine, transcribed from `api/Services/WorkflowTransitions.cs` |
| 3.3 | `diagrams/agent-graph.mmd` | The LangGraph in `agent/graph.py` and its two routing functions |
| 3.4 | `diagrams/e2e-sequence.mmd` | The full loop from a report to a reopened repair being diagnosed again |

---

## 3.1 System context

MaintenX consists of three deployable services and two client applications, together with
four external dependencies. The components and their responsibilities are summarised below.

| Component | Technology | Location | Responsibility |
| --- | --- | --- | --- |
| Mobile client | Flutter, Riverpod, go_router | `mobile/` | Filing reports (with QR scan and photo), answering clarification questions, a technician's jobs and completion, a reporter's repair confirmation |
| Web client | React 18, Vite | `web/` | Report intake, the approval queue, the dispatch board, the asset registry, verification and metrics |
| API | ASP.NET Core Web API (.NET 8), EF Core | `api/` | Every business rule, all persistence, authentication and authorisation, and three background workers (`WorkflowRunner`, `VerificationSweepService`, `TimetableSyncWorker`) |
| Agent service | Python, FastAPI, LangGraph | `agent/` | Four LLM-backed agents that return advice (questions, a diagnosis, a proposal, a verification opinion) |
| Database | PostgreSQL 16 | configured in `api/Program.cs` | The system of record |
| Object storage | Supabase Storage | `api/Services/SupabaseStorageService.cs` | Report and completion photos |
| Timetable source | Google Calendar API | `api/Services/GoogleCalendarClient.cs` | The campus timetable, mirrored into `ClassScheduleSlot` |
| LLM provider | Any OpenAI-compatible chat-completions endpoint | `agent/llm_client.py` | Model inference for the agents |

### 3.1.1 Connections, protocols and authentication

Figure 3.1 labels every edge with its protocol and its authentication mechanism. The same
information is given in tabular form below, with the file that establishes each connection.

| From → To | Protocol | Authentication | Established in |
| --- | --- | --- | --- |
| Flutter → API | HTTPS REST (JSON; multipart for photos) | JWT bearer token in the `Authorization` header | `mobile/lib/core/api_client.dart` |
| React → API | HTTPS REST (JSON) | JWT bearer token in the `Authorization` header | `web/src/services/apiClient.js` |
| API → PostgreSQL | TCP, Npgsql via EF Core | Connection-string credentials (`ConnectionStrings:DefaultConnection` or `DATABASE_URL`) | `api/Program.cs` |
| API → Supabase Storage | HTTPS REST (object upload) | Service-role key, sent as both `Authorization: Bearer` and `apikey` | `api/Services/SupabaseStorageService.cs` |
| API → Google Calendar | HTTPS, Calendar API v3 | Google service account, scope `calendar.readonly` | `api/Services/GoogleCalendarClient.cs` |
| API → Agent service | HTTP `POST /run` (JSON) | Shared secret in the `X-Agent-Secret` header, compared in constant time; closed when no secret is configured | `api/Program.cs` (the `IAgentClient` registration), `agent/main.py` (`require_agent_secret`) |
| Agent service → API | HTTP `POST /api/internal/tools/{toolName}` | Shared secret in the `X-Agent-Secret` header, compared in fixed time | `agent/tools.py`, `api/Middleware/AgentSecretFilter.cs` |
| Agent service → LLM | HTTPS `POST /chat/completions` | `Authorization: Bearer LLM_API_KEY` | `agent/llm_client.py` |
| Clients → Supabase Storage | HTTPS `GET` of a public object URL | None (public bucket) | `web/src/features/reports/components/ReportPhoto.jsx`, `mobile/lib/features/workorders/job_detail_screen.dart` |

The JWT is validated in `api/Program.cs` with issuer, audience, signing key and lifetime all
checked and `ClockSkew` set to zero, so expiry is exact. Authorisation policies are generated
one per member of the `Role` enum in the same file, so a new role acquires a policy
automatically.

### 3.1.2 The trust boundary

The design intent, stated in the project rules, is that the two clients communicate only with
the ASP.NET Core API and never with the agent service. The clients honour this: neither
contains a URL or configuration key for the agent service (`web/src/services/apiClient.js`
reads only `VITE_API_BASE_URL`, and `mobile/lib/core/env.dart` reads only the API base URL),
and every photo upload is routed through the API rather than written to storage directly.
The Supabase service-role key therefore exists only in the API's configuration.

One qualification applies, and it is drawn in Figure 3.1: a stored photo is displayed by
loading its public URL directly from Supabase Storage. This is a read of an object the API has
already validated, stripped of its metadata and stored; it carries no credential and writes
nothing, but it is a connection from a client to a service other than the API.

Both directions between the API and the agent service are authenticated with the same shared
secret. The agent service's `/run` endpoint requires the `X-Agent-Secret` header, checked in
constant time by the `require_agent_secret` dependency in `agent/main.py` before the body is
validated, and closed when no secret is configured; the API's typed `IAgentClient` sends it on
every call (`api/Program.cs`). Only the API can therefore start an agent run. In the reverse
direction, the agent service can reach campus data only through the tool router, which rejects
any request lacking the shared secret with a 401, and which fails closed when no secret is
configured (`api/Middleware/AgentSecretFilter.cs`). The agent service itself holds no database
credentials; `agent/config.py` declares no connection-string field, and its `extra="ignore"`
setting prevents a `DATABASE_URL` in the shared `.env` file from being loaded into the process.

---

## 3.2 Layer responsibilities: who decides what

The architecture separates *advice* from *decisions*. The agents interpret unstructured text —
a reporter's description, a technician's notes — and produce advice. Every decision that
changes the state of the system is taken by deterministic C# code in the API. No agent output
is used as a number in a calculation, and no agent output moves a workflow; what moves a
workflow is the fact that an agent *ran*, or an action taken by a person.

| Decision | Decided by | File |
| --- | --- | --- |
| Which questions to ask a reporter | Clarifier agent (advice), bounded by `ClarifierOutput` (at most two questions) | `agent/agents/clarifier.py`, `agent/schemas.py` |
| Whether a reporter's answer is acceptable (option membership, `"Yes"`/`"No"`, 100-character cap) | C# | `api/Services/ClarificationService.cs` |
| Likely cause of a fault | Diagnostic agent (advice) | `agent/agents/diagnostic.py` |
| Proposed strategy and estimated cost | Strategist agent (advice) | `agent/agents/strategist.py` |
| Whether a work order needs a manager's approval | C#: estimate strictly above `Approval:CostThreshold`, or strategy `EscalateReplacement` | `api/Services/WorkOrderService.cs` (`ApprovalBasisFor`) |
| Which workflow state follows which | C#: a hardcoded table keyed by (state, trigger) | `api/Services/WorkflowTransitions.cs` |
| Failure counts, the 90-day window, repeat failure, warranty cover | C# | `api/Services/AssetService.cs` (via the shared failure rules) |
| Whether a time slot is free (working hours, class buffers, technician overlap) | C# | `api/Services/SlotRules.cs` |
| When a repair is due for verification | C#: `Verification:DelayDays` after completion | `api/Services/VerificationService.cs` |
| Whether a repair held | The reporter's yes/no answer, applied in C# | `api/Services/VerificationService.cs` (`RecordReporterResponseAsync`) |
| Whether a repair *pattern* should escalate | Nothing yet (see §3.4.3) | — |

Within the API the layering is by technical layer rather than by feature, as the conventions
require: `api/Controllers/` receive a request, call one service and select a status code;
`api/Services/` hold the rules and talk to `AppDbContext` directly (there is no repository
layer); `api/Dtos/` are the wire contracts, as `record` types; `api/Models/` are the entities;
and `api/Data/` holds the context, the migrations and the development seeder. Every service is
an interface and an implementation supplied by constructor injection, and every service backed
by EF Core is registered with `AddScoped`.

The two clients compute no business rule. The approval-queue sentence "above the Rs 15,000
approval threshold", for instance, is chosen from booleans the API returns in
`WorkOrderDetailDto.ApprovalBasis`, which is produced by the same `ApprovalBasisFor` function
that routes the order (`api/Services/WorkOrderService.cs`); no JavaScript compares an estimate
with a threshold.

---

## 3.3 The non-blocking design

An agent run involves several model calls and several tool calls, and may take tens of
seconds. The architecture therefore never holds an HTTP request open while an agent works.
The mechanism has four parts.

**A request only writes rows and enqueues an identifier.** `POST /api/workflows`
(`api/Controllers/WorkflowsController.cs`) creates the `AgentWorkflow` row in state
`Submitted`, enqueues its id, and returns **202 Accepted** with a `Location` header pointing
at `GET /api/workflows/{id}`. The status code is 202 rather than 201 because the resource
exists but the work it describes has not been done. The path a reporter actually uses,
`POST /api/reports`, returns **201 Created** instead (`api/Controllers/ReportsController.cs`),
and legitimately so: `ReportService.CreateAsync` (`api/Services/ReportService.cs`) writes the
report, which is complete when the response is sent, then starts the workflow and enqueues it
as a side effect. In both cases the enqueue is passed `CancellationToken.None`, because the
request's own token is cancelled as soon as the response is written and would otherwise abort
the hand-off.

**The queue is an in-process bounded channel.** `WorkflowQueue`
(`api/Services/WorkflowQueue.cs`) wraps a `Channel<int>` with a capacity of 100 and
`BoundedChannelFullMode.Wait`, so a runner that falls behind applies back-pressure rather than
exhausting memory. It is registered as a singleton, which is correct because it holds no
`DbContext`. Its limitation is stated in its own documentation: the channel is in memory, so a
process restart loses any identifier still queued. The row survives in PostgreSQL, but **no
start-up re-queue sweep has been implemented**, so a workflow queued at the moment of a restart
remains in `Submitted` until something re-queues it.

**A hosted service drains the queue.** `WorkflowRunner` (`api/Services/WorkflowRunner.cs`) is a
`BackgroundService`. Because it is a singleton, it cannot hold the scoped `IWorkflowService` or
`AppDbContext`; it takes an `IServiceScopeFactory` and opens a new DI scope for each dequeued
workflow. It starts a run only from `Submitted` or `Diagnosing`
(`WorkflowService.BeginProcessingAsync`); anything else dequeued — a workflow waiting on a
person, or one re-queued in `Strategizing` after a revision request — is skipped with a warning.

**The outbound call never throws.** `IAgentClient` (`api/Services/AgentClient.cs`) is a typed
`HttpClient` with its own timeout (`Agent:TimeoutSeconds`, default 60 s, set in
`api/Program.cs`). A timeout, a refused connection, a non-success status or an unreadable body
is returned as a failed `AgentCallResult`, which the runner converts into the `Failed` state
with the reason written on the row. An exception anywhere else in `ProcessAsync` is caught,
logged, and likewise recorded as `Failed`. A background worker has no request on which to
surface an error, so the workflow row is the only place a failure can be made visible, and a
poll must never see a run that looks busy but is dead.

The runner advances a workflow one agent at a time. The agent service executes a whole segment
of the graph — for example clarifier, diagnostic and strategist — in a single `/run` call and
returns every agent's result; the runner then writes one `AgentStep` for each agent in graph
order, making that agent's transition and saving after each, so that a client polling the
workflow sees it move agent by agent. The step's agent name is taken from the field in which a
result arrived, not from the name the agent reports about itself
(`AgentRunResponse.DownstreamResults()`). The first agent to run in a call carries the whole
call's `DurationMs`; the others carry zero, because the agent service reports no per-agent
split and dividing the total would fabricate one.

---

## 3.4 The workflow state machine

Figure 3.2 is a transcription of the `Table` dictionary in `api/Services/WorkflowTransitions.cs`.
It has 23 entries over the eleven members of `WorkflowState` (`api/Models/WorkflowState.cs`),
and each edge is labelled with a member of `WorkflowTrigger`
(`api/Services/WorkflowTrigger.cs`). The states are those of the development guide's §8 with one
addition, `Failed`, which exists so that a run whose agent service is unreachable or
safe-fails ends somewhere a poll can see it. The verification outcomes — verified, reopened,
escalated — are deliberately modelled as *edges* out of `AwaitingVerification` rather than as
states, since a workflow is never "sitting in" a reopened condition; it is back in `Diagnosing`.

Three states represent waiting on a person. `AwaitingClarification` waits on the reporter and
is left only through `POST /api/reports/{id}/clarifications`. `Strategizing` is where the runner
stops, holding the strategist's proposal; it is left only when a facilities manager raises a
work order, and the runner never reaches `AwaitingManagerApproval` by itself.
`AwaitingManagerApproval` waits on a manager to approve, reject or request a revision.

### 3.4.1 Why the table is keyed by (state, trigger)

A table keyed by pairs of states would answer only "may a workflow go from A to B?". That
question is insufficient, because the same pair of states can be reached by events that must
not substitute for one another. The move from `AwaitingManagerApproval` to `WorkOrderRaised`
means that a manager *approved* an order. Under a state-only table, raising a second order
against the same report — which from `Strategizing` legitimately leads to `WorkOrderRaised` —
could take that same edge from `AwaitingManagerApproval` and would read, in the history, as an
approval that nobody gave. Keying the table by the triggering event makes each edge available
only to the event it was drawn for. `WorkflowTransitions.Move(workflow, trigger)` looks up
(current state, trigger), and when no entry exists it throws
`InvalidWorkflowTransitionException` without modifying the workflow.
`api/Middleware/ExceptionHandlingMiddleware.cs` maps that exception to **409 Conflict**, logged
as a warning. Because `Move` throws before `SaveChanges`, and because the completion path runs
inside a transaction, a 409 means nothing was written. The case described above is pinned by
`ASecondOrderOnTheSameReport_CannotPassForAnApproval` in
`api.Tests/WorkflowStateMachineTests.cs`.

Every service that moves a workflow calls `Move`: `WorkflowService` for the runner's moves,
`WorkOrderService` for raise, approve, reject, request-revision and complete,
`ClarificationService` for the reporter's answers, and `VerificationService` for the sweep and
the reporter's confirmation. The only direct assignments of `CurrentState` are at creation
(`WorkflowService.StartAsync` and the development seeder), which are not transitions. Two small
helpers keep the choice of trigger in C#: `ForClarification(questionCount)` selects
`ClarifierAsked` or `ClarifierFoundNothing`, and `ForRaisedWorkOrder(requiresApproval)` selects
`WorkOrderNeedsApproval` or `WorkOrderAutoApproved`. A third, `CanRaiseWorkOrder(state)`, is
read off the table itself, so that the web client's offer of a "raise work order" control and
the API's acceptance of one cannot disagree.

### 3.4.2 The save-time backstop

`AppDbContext.CheckWorkflowTransitions` (`api/Data/AppDbContext.cs`) runs on every
`SaveChanges`. For each modified `AgentWorkflow` whose `CurrentState` changed, it asks
`WorkflowTransitions.CanReach(original, current)` — whether *any* trigger connects the two
states — and throws the same exception if none does. This is a backstop for code written later
that assigns the state directly and bypasses `Move`. It is necessarily weaker than `Move`: at
save time the new state is visible but the event that caused it is not, so it cannot detect the
second-order case described in §3.4.1. It has one further consequence, which is documented in
the same method: because it compares the value loaded from the database with the value being
written, a single unit of work may move a workflow only one step per save. This is why the
runner saves after every transition. New rows are not checked.

### 3.4.3 Edges that nothing in the code currently fires

Each trigger was traced to its call sites. The table below lists every edge that is in the
table but that no production code path currently takes, together with edges that are taken
only in a narrow case.

| Edge | Status | Evidence |
| --- | --- | --- |
| `WorkOrderRaised → InProgress` on `WorkStarted` | **Never fired.** No endpoint starts a job, and no code sets `WorkOrderStatus.InProgress`. | No call site for `WorkflowTrigger.WorkStarted`; the gap is acknowledged in the comment above the entry in `WorkflowTransitions.cs` |
| `InProgress → Completed` on `WorkCompleted` | **Never taken.** The trigger is fired by `WorkOrderService.CompleteAsync`, but `InProgress` is unreachable, so completion always takes `WorkOrderRaised → Completed`. | Follows from the row above |
| `AwaitingVerification → AwaitingManagerApproval` on `RepairEscalated` | **Never fired.** Whether escalation is decided by a C# rule or by a manager is undecided, and the agent's `escalate` label is stored as an opinion that moves nothing. | No call site for `WorkflowTrigger.RepairEscalated` |
| `Strategizing → Failed` on `AgentFailed` | Fired only in a narrow case: a `/run` reply that contains a diagnosis but no strategist result, or an exception after the `Diagnosed` move. A strategist that *safe-fails* does not take it. | `WorkflowRunner.AdvanceThroughDownstreamAsync` |
| `AwaitingManagerApproval → Strategizing` on `RevisionRequested` | Fired, but the follow-up is not implemented: the workflow is re-queued and the runner skips it, because re-running the strategist alone with the manager's note needs a route in `graph.py` that does not yet exist, and the API does not send `revision_note`. | `WorkOrderService.RequestRevisionAsync`, `WorkflowService.BeginProcessingAsync`, `api/Dtos/AgentRunRequest.cs` |

All other edges have a production call site: `ClarifierAsked`, `ClarifierFoundNothing` and
`Diagnosed` in `WorkflowService`; `AgentFailed` from `Submitted` and `Diagnosing` in
`WorkflowRunner` via `WorkflowService.FailAsync`; `ReporterAnswered` in `ClarificationService`;
`WorkOrderAutoApproved` and `WorkOrderNeedsApproval` from both `Strategizing` and `Failed`,
`ManagerApproved`, `ManagerRejected` and `WorkCompleted` in `WorkOrderService`; and
`VerificationDue`, `RepairVerified` and `RepairReopened` in `VerificationService`.

### 3.4.4 The two loops back into Diagnosing

Two edges return a workflow to `Diagnosing`, and the runner resumes both through the same
method, `WorkflowRunner.ResumeAtDiagnosisAsync`. What distinguishes them is a single column,
`AgentWorkflow.ReopenedWorkOrderId`.

The first loop is **clarification**. When the reporter submits the clarification form,
`ClarificationService.SubmitAnswersAsync` (`api/Services/ClarificationService.cs`) writes the
answers, moves the report to `Clarified` and the workflow along `ReporterAnswered` to
`Diagnosing` in one `SaveChanges`, and only afterwards re-queues the workflow, so that the
runner's own scope cannot read the workflow before the answers are committed. The runner sends
the answers to *this* workflow's questions as `clarification_answers`, and `graph.py` routes any
request carrying answers directly to the diagnostic. Without that route the clarifier would ask
the questions just answered, producing a loop. If the runner finds the workflow in `Diagnosing`
with neither answers nor a reopened order, it moves it to `Failed` rather than risk re-running
the clarifier.

The second loop is **verification**. When a reporter answers "no" to "is the problem fixed?",
`VerificationService.RecordReporterResponseAsync` (`api/Services/VerificationService.cs`) sets
the check to `Reopened`, stamps `AgentQueuedAt`, moves the report's latest workflow along
`RepairReopened` to `Diagnosing`, and records the failed order in `ReopenedWorkOrderId`, all in
one `SaveChanges`, then re-queues the workflow. The runner sends `reopened: true`, which
`graph.py` also routes directly to the diagnostic: re-clarifying a fault that has already been
clarified and repaired would question the reporter again for no purpose. If the report never
named an asset, the runner falls back to the reopened work order's asset, which is never null,
because a re-diagnosis without the machine's service history — now including the record the
failed repair appended — would defeat the purpose of running again. The second diagnosis and
proposal are *appended* as new steps, so both diagnoses remain on record. The first diagnosis is
not passed to the agent: nothing persists between runs, and a second opinion anchored on the
first would not be an independent one. The transition is fired by the reporter's answer, never
by the verification agent's label. This loop is exercised end to end by
`api.Tests/WorkflowEndToEndTests.cs`.

---

## 3.5 The agent service

### 3.5.1 The graph

Figure 3.3 shows the graph compiled by `build_graph` in `agent/graph.py`. It has four nodes,
one per agent, and two routing functions, both of which are plain Python reading the request or
the previous node's output; no routing decision is made by a model. The graph is compiled
without a checkpointer, so no state persists between `/run` calls.

`_route_from_start` examines the request in a fixed order. If `request.verification is not
None` the run goes to `verify`; otherwise, if `request.clarification_answers or
request.reopened` it goes to `diagnose`; otherwise it goes to `clarify`. `_route_after_clarify`
is the first human pause: the run continues to `diagnose` only when
`response.status is AgentStatus.ok and not response.output.questions`. A clarifier that asked
anything, or that safe-failed, ends the run. From `diagnose` the graph always proceeds to
`strategize` and then to `END`; `verify` goes straight to `END`. A verification is kept on a
separate path because it asks a different question about a different thing: appended after
`strategize` it would re-clarify and re-diagnose a fault that has already been repaired.

The `verify` path is implemented and tested in the agent service (`agent/tests/test_verification.py`),
but **no C# code calls it**. `AgentRunRequest` (`api/Dtos/AgentRunRequest.cs`) has no
`verification` field, and no runner reads verification checks queued with `AgentQueuedAt`, so
`VerificationCheck.AgentOutcome` is written only by the development seeder.

`agent/main.py` assembles the response. The clarifier's result remains at the top level of
`RunResponse`, where the API reads it, with the diagnosis and the proposal attached beside it.
On a resumed run the clarifier did not execute, so the top-level fields describe the diagnostic
instead, with an empty question list.

### 3.5.2 The agent contract

Each agent is a class in its own file under `agent/agents/`, declares its own tool subset as the
constant `ALLOWED_TOOLS`, loads its prompt text from `agent/prompts/*.md`, and validates its
reply against a Pydantic model in `agent/schemas.py`. Every input and output model uses
`extra="forbid"`, so a stray field such as `conversation_history` in a request, or `approved` in
a reply, is a validation failure rather than a silently ignored value.

| Agent | Owner | Responsibility | Input schema | Output schema | `ALLOWED_TOOLS` | Safe-failure shape |
| --- | --- | --- | --- | --- | --- | --- |
| `ClarifierAgent` (`clarifier.py`) | Not recorded in code¹ | Ask at most two closed questions whose answers would change what a technician does; zero is a correct answer | `RunRequest` (uses `description`, `room_id`, `asset_id`) | `ClarifierOutput`: `questions`, 0–2 `ClarifyingQuestion` (`question_text` ≤ 300, `answer_type` ∈ `yes_no` / `single_select` / `short_text`, `options` 2–5 for `single_select`) | `get_room`, `get_asset` | `RunResponse` with `status: safe_failure`, `output: {questions: []}`, `error` set |
| `DiagnosticAgent` (`diagnostic.py`) | Not recorded in code¹ | Propose one to three causes from the asset's history, each with confidence and evidence, and one next action | `DiagnosticInput`: `description`, `room_id`, `asset_id`, `clarification_answers` (a projection of `RunRequest`) | `DiagnosticOutput`: `hypotheses` (1–3, each `cause`, `confidence`, 1–5 `evidence`), `primary_hypothesis_index`, `recommended_next_action` ∈ `inspect` / `repair` / `replace` / `monitor`, `reasoning_summary` ≤ 400 | `get_asset`, `get_asset_service_history`, `get_related_open_reports` | `DiagnosticResult` with `status: safe_failure`, `output: null` |
| `ResolutionStrategist` (`strategist.py`) | Not recorded in code¹ | Propose one strategy, an estimated cost, an urgency and a justification | `StrategistInput`: `description`, `asset_id`, `diagnosis` (nullable), `revision_note` (never sent by the API) | `StrategistOutput`: `strategy` (the six `WorkOrderStrategy` values in snake_case), `estimated_cost` (Decimal, 2 d.p.), `urgency`, `justification` ≤ 500, `consolidate_with_work_order_ids` | `get_asset`, `get_asset_service_history`, `get_open_work_orders` | `StrategistResult` with `status: safe_failure`, `output: null`; also returned when the reply names a work order it was not shown |
| `VerificationAgent` (`verification.py`) | Not recorded in code¹ | Judge whether a completed repair held, from the note, reports since and the reporter's answer | `VerificationInput`, built from `RunRequest.verification` (`work_order_id`, `reporter_confirmed`, `reporter_comment`) plus looked-up facts | `VerificationOutput`: `outcome` ∈ `confirm` / `reopen` / `escalate`, `confidence`, `reason` ≤ 400, `evidence` 1–5 | `get_work_order`, `get_asset_service_history`, `get_related_open_reports` | `VerificationResult` with `status: safe_failure`, `output: null`; returned without a model call when the work order cannot be found |

¹ Each agent's module docstring contains the placeholder `Owner: (assign one team member here)`.
The owner for each agent should be recorded there and in this table before submission.

The difference between the safe-failure shapes is deliberate. For the clarifier, an empty list
of questions is a legitimate answer. For the other three, an empty output would be a claim —
"inspect", "defer", "confirm" — that nobody made, so their failure is `null`. On the API side a
clarifier safe failure moves the workflow to `Failed` (`WorkflowRunner.RunFromSubmittedAsync`),
whereas a diagnostic or strategist safe failure is recorded on its step and the workflow still
advances to `Strategizing`, because missing advice is not a reason to prevent a manager from
acting.

No schema contains a free-text message field, no input schema accepts conversation history,
and each agent runs exactly one round per call. These properties are pinned by tests that assert
each output model's field set exactly — for example
`test_diagnostic_output_fields_are_exactly_the_contract` in `agent/tests/test_diagnostic.py` —
and by `test_extra_fields_are_rejected_not_ignored` in `agent/tests/test_schemas.py`. The
strategist's output additionally has no approval field of any kind, and its prompt is never
told the approval threshold, so there is no figure for an estimate to be aimed beneath.

### 3.5.3 Tool × agent matrix

The seven tools are the keys of the hardcoded dictionary in
`api/Controllers/InternalToolsController.cs`. Each handler delegates to the service that owns the
data; none queries `AppDbContext` in the controller. The row caps are constants in the service
interfaces and cannot be widened by the caller, because `ToolCallRequest` carries only a
workflow id, an entity id and an agent name.

| Tool | Id refers to | Service method | Cap | Clarifier | Diagnostic | Strategist | Verification |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: |
| `get_room` | Room | `IRoomService.GetByIdAsync` | — | ✓ | | | |
| `get_building` | Building | `IBuildingService.GetByIdAsync` | — | | | | |
| `get_asset` | Asset | `IAssetService.GetAssetContextAsync` | — | ✓ | ✓ | ✓ | |
| `get_asset_service_history` | Asset | `IAssetService.GetRecentServiceHistoryAsync` | 20, newest first (`MaxToolHistoryRows`) | | ✓ | ✓ | ✓ |
| `get_related_open_reports` | Asset | `IReportService.GetOpenReportsForAssetAsync` | 10 (`MaxToolRelatedReports`) | | ✓ | | ✓ |
| `get_open_work_orders` | Asset (answers for its whole room) | `IWorkOrderService.GetOpenWorkOrdersInAssetRoomAsync` | 10 (`MaxToolOpenWorkOrders`) | | | ✓ | |
| `get_work_order` | Work order | `IWorkOrderService.GetWorkOrderFactsAsync` | — | | | | ✓ |

Each agent's subset differs from every other's. The clarifier cannot read service history,
because reading history is diagnosis. `get_open_work_orders` belongs to the strategist alone,
because consolidating visits is a question about the work rather than the fault.
`get_work_order` belongs to the verification agent alone. `get_building` is registered in the
C# allow-list but appears in no agent's subset; it is currently unused.

Every tool returns facts — a row, a list of rows, or nothing — and never a judgement. There is
deliberately no `diagnose`, `assess` or `recommend` tool, and no tool that raises, approves or
re-costs a work order; `TheAllowListHasNoJudgementTool` and
`TheAllowListHasNoToolThatActsOnAWorkOrder` in `api.Tests/AgentToolTests.cs` request such names
and expect 404. A null result and an empty list are different answers: an unknown asset is
`found: false`, while an asset with no history is `found: true` with an empty list.

### 3.5.4 The LLM loop: retry once, then fail safely

`LlmClient.complete_json` (`agent/llm_client.py`) implements one loop shared by all four agents.
It sends a system and a user prompt, extracts the JSON object from the reply (tolerating code
fences and surrounding prose), parses it and validates it against the agent's Pydantic schema.
If parsing or validation fails, it makes exactly one further attempt, resending the conversation
with the model's rejected reply and the validation error appended; the correction text is itself
a prompt file (`agent/prompts/json_retry.md`). `MAX_ATTEMPTS` is 2. There is no unbounded loop,
no retry library and no back-off. After the second failure it returns
`LlmJsonResult(ok=False, error=…)`, which each agent converts into its safe-failure shape.

The client makes two guarantees to its callers: it never raises, because every provider
exception, timeout and malformed body is caught and returned as a failed result; and it never
hangs, because every HTTP call carries a timeout (`llm_timeout_seconds`, default 30 s, in
`agent/config.py`). A safe failure is returned by `/run` as an ordinary 200 response, because
the caller is a background worker recording a step, not a user awaiting an error page.

Provider structured-output features (`response_format`, JSON mode, tool calling) are
deliberately not used. They differ between providers and may be absent on a local model, which
would break the requirement that changing provider is a configuration change only
(`LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL`). Syntactically valid JSON is also not the same as a
schema-valid reply, so Pydantic validation would be needed regardless. Performing validation
locally keeps a bad reply on an ordinary, testable code path;
`test_malformed_output_triggers_exactly_one_retry` and
`test_the_retry_carries_the_validation_error` in `agent/tests/test_llm_client.py` pin the loop.

### 3.5.5 Prompt-injection defence

Every piece of text an agent reads that was typed by a person — the report description,
clarification answers, technicians' notes, a manager's revision note, a reporter's comment — is
untrusted. The defence has a structural half and an instructional half, and neither is relied
upon alone.

The structural half is that untrusted text is never spliced into a prompt as raw text. Each
agent assembles its data into a single object and serialises it with `json.dumps` (for example
`DiagnosticAgent._render_data` in `agent/agents/diagnostic.py`), and the user prompt template
places that serialised object between `--- BEGIN DATA ---` and `--- END DATA ---` markers
(`agent/prompts/*_user.md`). JSON encoding escapes every newline inside a string, so a
description containing a newline followed by `--- END DATA ---` and a new "task" heading cannot
place the closing marker on a line of its own. Templates are filled with `string.Template`
rather than `str.format`, so braces in example JSON are not interpreted. Each agent has a test
named `test_injection_text_cannot_leave_the_data_block` (in `agent/tests/test_clarifier.py`,
`test_diagnostic.py`, `test_strategist.py` and `test_verification.py`), which fails if the
encoding is replaced by a raw splice.

The instructional half is that each system prompt states that everything between the markers is
data, not instructions, and names the likely manipulation attempts — to ignore the rules, to
choose a particular action, to mark something approved (`agent/prompts/diagnostic.md`,
`strategist.md`, `verification.md`, `clarifier_system.md`). Because a model can still be
persuaded, the architecture limits what a persuaded model could achieve. The output schemas cap
its reply (two questions, three hypotheses, fixed enumerations). The strategist's proposal to
consolidate is refused if it names a work order the agent was not shown. Its tools are
read-only, and the set of tools is enforced in C# (§3.5.6). Finally, nothing it produces moves a
workflow or approves an order. Whether a model actually resists injection is a behavioural
question that a stubbed unit test cannot answer, and is measured separately by the live
evaluations under `agent/evals/`, which are excluded from CI because they incur provider cost.

### 3.5.6 Why the tool allow-list is hardcoded in C#

Two allow-lists exist. Each agent's `ALLOWED_TOOLS` constant is checked by `ToolClient.call`
(`agent/tools.py`) before a request leaves the process, which catches a programming error in an
agent early and loudly. That check is a convenience, not the security boundary. The boundary is
the static `AllowedTools` dictionary in `api/Controllers/InternalToolsController.cs`, compiled
into the API assembly and compared ordinally.

It is hardcoded because the list of capabilities must not be describable, let alone changeable,
by anything a model can influence. If the list were read from configuration, from the request,
or from a description in a prompt, a manipulated or confused model would have a path —
however indirect — to naming a capability into existence. As a compiled dictionary on the far
side of a network hop, it can be changed only by a code change, a pull request and a review. A
tool name that is not a key cannot reach any service: the controller logs a warning, records an
`AgentStep` with `ValidationResult` `RejectedUnknownTool` so the attempt appears in the
workflow's audit trail, and returns 404. If the Python-side check were deleted entirely, the
worst outcome would be that 404 and a log line. The same reasoning is why authentication for
this endpoint is a shared secret rather than a JWT: there is no user behind a tool call, so
there is no role to check, and `AgentSecretFilter` runs as an authorisation filter, before
model validation, so a caller without the secret always receives 401 and never a 400 that would
reveal the expected body.

---

## 3.6 The end-to-end loop

Figure 3.4 traces one fault through the whole system. A reporter files a report from the phone;
the API returns 201 and enqueues the workflow. The runner calls `/run`; the clarifier looks up
the room (and the asset, if a sticker was scanned) through the tool router, asks the model, and
asks one or two questions, so the graph stops and the runner records the questions and moves the
workflow to `AwaitingClarification`. The reporter answers on the phone in one request, which
moves the workflow to `Diagnosing` and re-queues it; the second `/run` goes directly to the
diagnostic and the strategist, and the runner records both and leaves the workflow in
`Strategizing`. A facilities manager opens the report in the web client and raises the work
order; `ApprovalBasisFor` compares the estimate with the threshold in C#, and an order above it
waits in `AwaitingManagerApproval` until the manager approves it. After assignment, the
technician completes the job on the phone: the order, a new `ServiceRecord`, the workflow's move
to `Completed` and a pending `VerificationCheck` are written in one database transaction, so
that none can exist without the others (`WorkOrderService.CompleteAsync`). After
`Verification:DelayDays` (default 5) the sweep moves the workflow to `AwaitingVerification` and
asks the reporter. A "no" moves the workflow along `RepairReopened` back to `Diagnosing`, and
the runner diagnoses again, this time reading a service history that contains the failed repair.

The same path, with `IAgentClient` replaced by a scripted stub, is asserted state by state in
`api.Tests/WorkflowEndToEndTests.cs`.

---

## 3.7 Summary of partial implementation

For clarity, the capabilities that the architecture provides for but that the code does not yet
complete are collected here.

| Capability | What exists | What is missing |
| --- | --- | --- |
| Verification agent in the loop | `VerificationAgent`, its schema, prompt, graph path and tests; checks are queued by stamping `AgentQueuedAt` | A C# runner that reads queued checks, calls `/run` with `verification`, and writes `AgentOutcome` / `AgentReason` / evidence back |
| Escalation of a repeat failure | The `RepairEscalated` edge in the table; the agent's `escalate` label | Anything that fires `RepairEscalated` |
| Job start | The `WorkStarted` edge and the `InProgress` state | An endpoint or action that starts a job |
| Revision re-run | `RevisionRequested` moves the workflow and re-queues it; `StrategistInput.revision_note` exists | A `graph.py` route that re-runs the strategist alone; the API sending `revision_note`; the runner accepting `Strategizing` |
| Durable queue | Bounded in-process channel; rows persist in PostgreSQL | Re-queueing `Submitted` / `Diagnosing` workflows at start-up |
| Authenticated agent endpoint | Shared-secret authentication on the agent-to-API direction | Any authentication on the API-to-agent `/run` call |
