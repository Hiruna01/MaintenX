# 3. Full-Stack and Agentic AI Architecture

This chapter describes the architecture of MaintenX as it is implemented in the repository.
Every rule stated here is attributed to the source file that enforces it, and where a capability
is only partly implemented the text says so (collected in §3.12). The figures are Mermaid and
DBML sources under `docs/report/diagrams/`.

| Figure | Source file | Shows |
| --- | --- | --- |
| 3.1 | `diagrams/system-architecture.mmd` | Runtime components, every connection, its protocol and its authentication |
| 3.2 | `diagrams/workflow-state-machine.mmd` | The workflow state machine, transcribed from `api/Services/WorkflowTransitions.cs` |
| 3.3 | `diagrams/agent-graph.mmd` | The LangGraph in `agent/graph.py` and its three routing functions |
| 3.4 | `diagrams/e2e-sequence.mmd` | One fault end to end: report, plan, clarify, diagnose, propose, approve, repair, verify, reopen |
| 3.5 | `diagrams/schema.dbml` | The database schema (ER diagram), transcribed from the EF Core model snapshot |

---

## 3.1 System context

MaintenX consists of three deployable services and two client applications, together with three
external services and the database.

| Component | Technology | Location | Responsibility |
| --- | --- | --- | --- |
| Mobile client | Flutter, Riverpod, go_router | `mobile/` | Self-registration; filing reports (QR scan, camera or gallery photo); answering clarification questions; a technician's jobs and their completion; a reporter's repair confirmation |
| Web client | React 18 (Vite), Context API, React Router | `web/` | Report intake; the approval queue; the dispatch board and scheduling; the asset registry, estate and user accounts; workflow monitoring; verification and metrics |
| API | ASP.NET Core Web API (.NET 8), EF Core | `api/` | Every business rule, all persistence, authentication and authorisation, workflow orchestration, the approval gate, the audit trail, and four background workers (§3.4) |
| Agent service | Python, FastAPI, LangGraph | `agent/` | Five LLM-backed agents (planner, clarifier, diagnostic, strategist, verification) that return **advice** |
| Database | PostgreSQL (16 in CI) | configured in `api/Program.cs` | The system of record, 15 tables (Figure 3.5) |
| Object storage | Supabase Storage | `api/Services/SupabaseStorageService.cs` | Report and completion photos |
| Timetable source | Google Calendar API | `api/Services/GoogleCalendarClient.cs` | The campus timetable, mirrored into `ClassScheduleSlots` |
| LLM provider | Any OpenAI-compatible chat-completions endpoint | `agent/llm_client.py` | Model inference for the agents |

### 3.1.1 Connections, protocols and authentication

Figure 3.1 labels every edge with its protocol and its authentication mechanism.

| From → To | Protocol | Authentication | Established in |
| --- | --- | --- | --- |
| Flutter → API | HTTPS REST (JSON; multipart for photos) | JWT bearer token in the `Authorization` header | `mobile/lib/core/api_client.dart` |
| React → API | HTTPS REST (JSON) | JWT bearer token in the `Authorization` header | `web/src/services/apiClient.js` |
| API → PostgreSQL | TCP, Npgsql via EF Core | Connection-string credentials (`ConnectionStrings:DefaultConnection` or `DATABASE_URL`) | `api/Program.cs` |
| API → Supabase Storage | HTTPS REST (object upload) | Service-role key, sent as both `Authorization: Bearer` and `apikey` | `api/Services/SupabaseStorageService.cs` |
| API → Google Calendar | HTTPS, Calendar API v3 | Google service account, scope `calendar.readonly` | `api/Services/GoogleCalendarClient.cs` |
| API → Agent service | HTTP `POST /run` (JSON) | Shared secret in the `X-Agent-Secret` header, compared in constant time; closed when no secret is configured | `api/Program.cs` (the `IAgentClient` registration), `agent/main.py` (`require_agent_secret`) |
| Agent service → API | HTTP `POST /api/internal/tools/{toolName}` | The same shared secret, checked in fixed time; closed when none is configured | `agent/tools.py`, `api/Middleware/AgentSecretFilter.cs` |
| Agent service → LLM | HTTPS `POST /chat/completions` | `Authorization: Bearer LLM_API_KEY` | `agent/llm_client.py` |
| Clients → Supabase Storage | HTTPS `GET` of a public object URL | None (public bucket) | `web/src/features/reports/components/ReportPhoto.jsx`, `mobile/lib/features/workorders/job_detail_screen.dart` |

The JWT is validated in `api/Program.cs` with issuer, audience, signing key and lifetime all
checked and `ClockSkew` set to zero, so expiry is exact. Authorisation policies are generated
one per member of the `Role` enum, and a **fallback policy** (`RequireAuthenticatedUser`) makes
"signed in" the default for every endpoint, so a forgotten `[Authorize]` fails closed. Exactly
four endpoints are anonymous: register, login, `/health`, and the agent tool router, which
has its shared-secret filter instead. The list is pinned by
`TheOnlyAnonymousEndpoints_AreLoginRegisterHealthAndTheAgentToolRouter` in
`api.Tests/AuthTests.cs`.

On top of the token check, `JwtBearerEvents.OnTokenValidated` calls
`IAuthService.IsSessionValidAsync` on every authenticated request. It confirms that the account
exists, is active, and still has the role the token claims. This is why a deactivation or a
role change takes effect on the next request rather than when the 12-hour token expires, even
though there are no refresh tokens.

### 3.1.2 The trust boundary

The two clients communicate only with the ASP.NET Core API and never with the agent service.
Neither contains a URL or configuration key for it: `web/src/services/apiClient.js` reads only
`VITE_API_BASE_URL`, and `mobile/lib/core/env.dart` reads only the API base URL. Every photo
upload is routed through the API rather than written to storage directly, so the Supabase
service-role key exists only in the API's configuration.

One qualification is drawn in Figure 3.1. A stored photo is displayed by loading its public URL
directly from Supabase Storage. That is a read of an object the API has already validated,
stripped of its metadata and stored; it carries no credential and writes nothing, but it is a
connection from a client to a service other than the API.

Both directions between the API and the agent service are authenticated with the same shared
secret. Only the API can start an agent run: `/run` rejects a request without the secret with
401 before the body is even validated. In the other direction, the agent service can reach
campus data only through the tool router (§3.6.5). **The agent service holds no database
credentials.** `agent/config.py` declares no connection-string field, and its `extra="ignore"`
setting stops a `DATABASE_URL` in the shared `.env` file from being loaded into the process.

---

## 3.2 Layer responsibilities: who decides what

The architecture separates *advice* from *decisions*. The agents interpret unstructured text
(a reporter's description, a technician's notes) and produce advice. Every decision that
changes the state of the system is taken by deterministic C# code, or by a person. No agent
output is used as a number in a calculation, and no agent output moves a workflow. What moves
a workflow is the fact that an agent *ran*, or an action taken by a person.

| Decision | Decided by | File |
| --- | --- | --- |
| Which agents run for a report, and whether the clarifier is needed | Planner agent (proposal), then checked in C# against fixed rules before it is stored | `agent/agents/planner.py`, `api/Services/PlanRules.cs` |
| Which questions to ask a reporter | Clarifier agent (advice), bounded by `ClarifierOutput` (at most two questions) | `agent/agents/clarifier.py`, `agent/schemas.py` |
| Whether a reporter's answer is acceptable (option membership, exactly `"Yes"`/`"No"`, 100-character cap) | C# | `api/Services/ClarificationService.cs` |
| Likely cause of a fault | Diagnostic agent (advice) | `agent/agents/diagnostic.py` |
| Proposed strategy and estimated cost | Strategist agent (advice) | `agent/agents/strategist.py` |
| Whether a proposal can be raised as a work order (a report, an asset, a mappable strategy, an estimate inside the DTO's bounds at two decimal places) | C# | `api/Services/WorkflowRunner.cs` (`RaisableProposal`) |
| Whether a work order needs a manager's approval | C#: estimate strictly above `Approval:CostThreshold`, or strategy `EscalateReplacement` | `api/Services/WorkOrderService.cs` (`ApprovalBasisFor`) |
| Approve, reject or request revision | A Facilities Manager | `WorkOrdersController` → `WorkOrderService` |
| Which workflow state follows which | C#: a hardcoded table keyed by (state, trigger) | `api/Services/WorkflowTransitions.cs` |
| What a workflow move means for its report | C#: a table keyed by trigger | `api/Services/ReportProgress.cs` |
| Failure counts, the 90-day window, repeat failure, warranty cover | C# | `api/Services/FailureRules.cs` |
| Whether a time slot is free (working hours, class buffers, technician overlap) | C# | `api/Services/SlotRules.cs` |
| The repair SLA and whether it was met | C# | `api/Services/SlaRules.cs` |
| When a repair is due for verification | C#: `Verification:DelayDays` after completion | `api/Services/VerificationService.cs` |
| Whether a repair held | The reporter's yes/no answer, applied in C# | `VerificationService.RecordReporterResponseAsync` |
| Whether a repair held, in the agent's opinion | Verification agent (advice, stored as a string nothing acts on) | `agent/agents/verification.py`, `api/Services/VerificationAgentService.cs` |
| Whether a repeat failure should escalate | Nothing yet (§3.12) | — |

---

## 3.3 Full-stack layering

### 3.3.1 The API

The API is a single ASP.NET Core project organised **by technical layer**, not by feature:

| Folder | Holds | Rule |
| --- | --- | --- |
| `api/Controllers/` | Thin controllers with `[ApiController]` and `[Route("api/[controller]")]` | Receive the request, call one service, choose the status code. No business logic |
| `api/Services/` | An interface and an implementation per service, plus pure rule classes (`WorkflowTransitions`, `PlanRules`, `SlotRules`, `SlaRules`, `FailureRules`, `ReportProgress`, `MetricRules`) and the background workers | Services talk to `AppDbContext` directly. There is no repository layer, no MediatR, no AutoMapper and no `Result<T>` wrapper |
| `api/Dtos/` | `record` DTOs; input DTOs carry DataAnnotations and never an `Id` | An entity is never returned from a controller |
| `api/Models/` | EF Core entities and enums | Enums are persisted and sent in JSON as their **name** (`JsonStringEnumConverter`), never an ordinal |
| `api/Data/` | `AppDbContext`, 18 migrations, the development seeder | `CreatedAt`/`UpdatedAt` are stamped centrally in `ApplyTimestamps` |
| `api/Middleware/` | `ExceptionHandlingMiddleware` (ProblemDetails; an illegal workflow move becomes a 409), `AgentSecretFilter` | Exception detail only in Development |

Every service is supplied by constructor injection. Every service backed by EF Core is
registered `AddScoped`, because a singleton holding a scoped `DbContext` would be a
captive-dependency bug. The singletons are the stateless or `DbContext`-free pieces:
`PasswordHasher<User>`, the workflow queue, the verification-agent doorbell, the Google Calendar
client and `TimeProvider`. Serilog logs method, path, status and duration for each request, and
never a request body, so a password in a login payload never reaches a log sink. Swagger is
served in Development, and elsewhere when `Swagger:Enabled` / `SWAGGER_ENABLED` is set.

The status-code contract is uniform: 200 read, 201 create (`CreatedAtAction`), 202 for a
started workflow, 204 update or delete, 400 validation, **401 no valid token, 403 the wrong
role or someone else's record** (told apart from a 404 by an existence check made first), 404
not found, and 409 for a request that conflicts with the current state: a duplicate, a
decision already taken, or an illegal transition.

### 3.3.2 The web client

`web/src` keeps the lab's separation of UI, hooks and services inside each feature folder
(`features/<name>/components`, `hooks`, `services`, `pages`):

- **Components never call `fetch`.** API calls live in a feature's `services/`, which all go
  through `services/apiClient.js` (base URL plus bearer token). Pages read data through the
  `useFetch` hook, which returns `{ data, isLoading, error }` and ignores a response that
  arrives after unmount. Every page renders all three states. A refresh remounts a component
  with a new `key`; there is no global cache.
- **State:** `useState` locally; **React Context** (`features/auth/components/AuthProvider.jsx`)
  for the signed-in user. The token lives in `services/tokenStore.js`, a module variable
  mirrored into `localStorage`, because services cannot call hooks. A 401 on a request that
  carried a token clears the store, and `AuthProvider`, which subscribes to it, signs the user
  out with a "session expired" notice. Redux, Zustand and TanStack Query are not used (an ADR
  decision).
- **Routing:** `routes/AppRoutes.jsx` with `ProtectedRoute` (the role lists in
  `features/auth/services/roles.js` mirror the API's policies exactly) and `RolePanelGuard`
  for slide-over panels that are routes. The wrong role sees a "not authorised" page, never a
  blank screen or a silent redirect.
- **The client computes no business rule.** Thresholds, warranty, overdue, SLA state, counts
  and rates are all read from the API and only formatted and coloured.

### 3.3.3 The mobile client

`mobile/lib` follows the same separation: **screens never call `http`**. A screen calls a feature
API class (`ReportsApi`, `WorkOrdersApi` and others), which calls the single `ApiClient`
(`core/api_client.dart`).

- **State:** local state in `ConsumerStatefulWidget`s, and **Riverpod** providers for anything
  app-wide (the session, the API client). There is no code generation.
- **Secure storage:** the token is kept by `TokenStorage` (`core/token_storage.dart`) in
  `flutter_secure_storage`, which uses the iOS Keychain or Android `EncryptedSharedPreferences`,
  never plain `SharedPreferences`.
- **Session expiry without navigation knowledge:** `ApiClient` clears `TokenStorage` on a 401
  for a request that carried a token. `TokenStorage` is a `ChangeNotifier`, so `AuthController`
  sees the token disappear and marks the session signed out. go_router's `refreshListenable`
  fires, and the single `redirect` in `router/app_router.dart` sends the user to login. That
  redirect is the whole route guard.
- **Device features:** QR scanning (`mobile_scanner`) and the camera and photo library
  (`image_picker`).

### 3.3.4 One API, two clients

Both clients go through the same endpoints, the same JWT, the same role policies and the same
business rules. What differs is purpose. The phone is for people next to the equipment
(Reporter, Technician). The web is for people making decisions about it (Facilities Manager,
Admin). Enum values are shared **by name** (`roles.js`, `workOrdersApi.js`, `auth_state.dart`,
`work_order.dart` hold the same strings the API sends), so a member added to a C# enum cannot
silently shift either client's meaning.

---

## 3.4 The non-blocking design and the background workers

An agent run involves several model calls and several tool calls, and can take tens of seconds.
The architecture therefore **never holds an HTTP request open while an agent works**.

**A request only writes rows and enqueues an id.** `POST /api/reports`
(`ReportService.CreateAsync`) writes the report and starts its workflow in one explicit
transaction, then enqueues the workflow id after the commit and returns **201**. The report is
complete when the response is sent. `POST /api/workflows` returns **202 Accepted** with a
`Location` header, because the resource exists but the work it describes has not been done.
The enqueue is passed `CancellationToken.None`, because the request's own token is cancelled as
soon as the response is written and would otherwise abort the hand-off.

**The queue is an in-process bounded channel.** `WorkflowQueue` wraps a `Channel<int>` with a
capacity of 100 and `BoundedChannelFullMode.Wait`, so a runner that falls behind applies
back-pressure rather than exhausting memory. Because it is in memory, a restart empties it.
**`WorkflowRunner.RequeueUnfinishedRunsAsync` therefore re-queues at startup** every workflow an
agent run was working on: `Submitted`, `Diagnosing`, and `Strategizing` with a revision pending.
This is idempotent, because the runner only starts from those states (pinned by
`AtStartup_EveryRunLeftSubmittedOrDiagnosing_IsQueuedAgain_AndNothingElse` and
`AtStartup_ARevisionNotYetAnswered_IsQueuedAgain_AndNoOtherStrategizingRun`).

**Four hosted services, one shape.** Each is a singleton `BackgroundService` that holds no
`DbContext`, opens a new DI scope per unit of work, and catches every exception so the loop
never dies.

| Worker | Wakes on | Does |
| --- | --- | --- |
| `WorkflowRunner` | The workflow queue | Runs one agent segment per dequeued workflow (§3.5) |
| `VerificationSweepService` | Startup, then every `SweepIntervalMinutes` (default 60), or "Run sweep now" | Moves due workflows to `AwaitingVerification`, asks reporters, queues checks for the agent |
| `VerificationAgentRunner` | Startup, the `IVerificationAgentSignal` doorbell, or the sweep interval | Drains verification checks waiting on the agent, one at a time (§3.8) |
| `TimetableSyncWorker` | Startup, then every `Google:SyncIntervalMinutes` (default 60) | Mirrors the Google Calendar timetable |

**The outbound call never throws.** `IAgentClient` (`api/Services/AgentClient.cs`) is a typed
`HttpClient` with its own timeout, `Agent:TimeoutSeconds`. The default of **360 s**
(`AgentSettings.DefaultTimeoutSeconds`) is budgeted above the agent's worst case: four agents
× two LLM attempts × 30 s, plus their tool calls, is about 320 s. A timeout, a refused
connection, a non-success status or an unreadable body comes back as a failed
`AgentCallResult`, which the runner turns into `Failed` with the reason on the row. Any other
exception in `ProcessAsync` is caught, logged and recorded the same way. A background worker
has no request on which to surface an error, so the workflow row is the only place a failure
can be seen, and a poll must never see a run that looks busy but is dead.

**Clients follow progress by re-reading.** The phone's `ClarifierWait` polls
`GET /api/reports/{id}` every 2 s (for at most 3 minutes) after filing, and opens the question
form the moment questions exist. The web client re-reads a workflow or report when it is
refreshed.

---

## 3.5 The workflow runner: one agent at a time

The agent service executes a whole *segment* of the graph between two human pauses in a single
`/run` call and returns every agent's result. The runner (`api/Services/WorkflowRunner.cs`) then
walks those results **in graph order**. For each agent it writes that agent's `AgentStep` with
its output verbatim, marks the agent's plan step, makes the agent's transition, and saves, so a
client re-reading the workflow sees it move agent by agent. `ProcessAsync` chooses the branch
from the state it dequeued:

| Dequeued in | Branch | What is sent to `/run` | What the runner does with the reply |
| --- | --- | --- | --- |
| `Submitted` | `RunFromSubmittedAsync` | The objective, room, asset | Records the **planner's** step and stores the plan (§3.6.2). If the plan kept the clarifier: its step, and either its questions (`ClarificationQuestion` rows, the report to `AwaitingClarification`, **the run stops: human pause 1**) or nothing to ask (on to diagnosis). If the plan left it out: `PlannedWithoutClarification` → `Diagnosing`. Then the diagnostic (→ `Strategizing`), the strategist, and the proposal is raised through the approval gate |
| `Diagnosing` (answered) | `ResumeAtDiagnosisAsync` | `clarification_answers` for *this* workflow's questions | Diagnostic, then strategist, then raise. No answers to send → `Failed` rather than re-asking |
| `Diagnosing` (reopened) | `ResumeAtDiagnosisAsync` | `reopened: true`; `asset_id` falls back to the reopened order's asset | A second diagnostic and strategist step are **appended** to the plan and the trail, so both diagnoses stay on record |
| `Strategizing` (revision pending) | `ReviseAsync` | `revision_note`, `revision_work_order_id`, the order's asset | Strategist only; a usable proposal **resubmits the same Draft** through the same gate. A failed call is recorded as a `CallFailed` strategist step, which marks the revision answered, and the Draft waits for a manager |
| Anything else | — | nothing | Skipped with a warning (waiting on a person) |

**The runner raises the proposal through the approval gate.** After the strategist,
`RaiseFromProposalAsync` calls `IWorkOrderService.CreateAsync`, the same method, and so the same
gate, that a manager's order goes through. It raises only a proposal it *can*: a report, an
asset (the report's, or the reopened order's), a strategist that succeeded, a strategy that
`AgentAnalysis` maps to `WorkOrderStrategy`, and an estimate inside `CreateWorkOrderDto`'s
`[Range]` at no more than two decimal places (`RaisableProposal`: never rounded, never guessed).
Otherwise the workflow waits in `Strategizing` for a manager to raise an order from the report
page. The gate then decides where the order lands. **A consequence worth stating:** a proposal
at or under the threshold is approved with nobody deciding, exactly as a manager's order at that
cost would be, and the gate's audit step says "Raised by the workflow runner". A raise that the
gate refuses because a manager got there first is logged, never a failed run. Pinned by
`AProposalForANamedAsset_IsRaisedByTheRunner_ThroughTheApprovalGate` and
`AProposalTheApiCannotRaise_IsLeftForAManager`.

**Safe failures cost advice, not the ability to act.** A diagnostic or strategist that
safe-failed still moves the workflow on; the failure is on its step and its plan step. An agent
that is *absent* from the reply (the graph broke its promise) fails the run. A clarifier safe
failure also fails the run, because the runner cannot tell whether to pause.

**Each step records the agent's own time and attempts.** `graph.py` stamps `duration_ms` around
each agent and each agent reports its LLM `attempts` (1, or 2 after the one retry). These are
stored as `AgentStep.DurationMs` and `AgentStep.Attempts`. The step's agent name comes from the
*field* in which a result arrived (`AgentRunResponse.DownstreamResults()`), never from the name
the agent reports about itself.

---

## 3.6 The agent service

### 3.6.1 The graph

Figure 3.3 shows the graph compiled by `build_graph` in `agent/graph.py`. It has **five nodes,
one per agent**, and three routing functions. All three are plain Python reading the request or
a previous node's *validated* output; no routing decision is made by a model's free text. The
graph is compiled without a checkpointer, so nothing persists between `/run` calls.

`_route_from_start` examines the request in a fixed order:

| Request carries | Route | Why |
| --- | --- | --- |
| `verification` | `verify → END` | A different question about a different thing; run in line it would re-clarify a repaired fault |
| `revision_note` | `strategize → END` | The fault is diagnosed already; what the manager sent back is the plan for the work |
| `clarification_answers`, or `reopened` | `diagnose → strategize → END` | The clarifier already asked, or the fault was clarified and repaired once. Sending it to `clarify` again would ask the same questions and loop |
| nothing else (a fresh report) | `plan` | Every fresh run is planned first |

`_route_after_plan` is where the plan **delegates**. It reads the planner's plan, already
validated by `PlannerOutput`, and goes to `clarify` when the plan includes the clarifier and
straight to `diagnose` when it does not. A planner that safe-failed produced no plan, and the run
takes the full pipeline, clarifier first: asking is the safe default. `_route_after_clarify` is
**human pause 1**: a clarifier that asked anything, or that safe-failed, ends the run; only a
clean "nothing to ask" continues to `diagnose`. From `diagnose` the graph always proceeds to
`strategize`, then `END`.

`agent/main.py` assembles the `RunResponse`. The planner's result travels in its own `plan`
field on every fresh run. The clarifier's result stays at the top level, with the diagnosis and
proposal beside it. When the clarifier did not run (a resume, a reopen, or a plan without it),
the top level describes the diagnostic instead, which is how the API knows the clarifier did not
run. On a revision it describes the strategist, and on a verification the verifier.

### 3.6.2 The plan: proposed by a model, decided in C#

Every fresh run starts with the **Planner Agent** (`agent/agents/planner.py`). From the report's
description and whether a room and an asset are identified (`PlannerInput`, a projection that
never shows it ids), it returns `PlannerOutput`: two or three `steps`, each an `agent` and a
`purpose` (at most 200 characters), plus a `rationale` (at most 300). Its one real decision is
whether the clarifier comes first. The prompt says to leave it out only when the report already
states what is failing, dead or intermittent, and that nothing is unsafe, and "when in doubt,
include it".

The plan is checked twice, on both sides of the network. `PlannerOutput` enforces the pipeline
rules in the schema: agents only from `clarifier`, `diagnostic`, `strategist`, in that order,
none repeated, the diagnostic and strategist always present, and `extra="forbid"`, so an
`"approved": true` field is a validation failure. Then `PlanRules.Validate`
(`api/Services/PlanRules.cs`) checks the same rules again in C# before the plan is stored. A plan
that fails is **not** stored: `PlanRules.Fallback` (every agent, clarifier included) is stored
with the reason, and the planner's step is marked `Rejected`. The planner's reply is always kept
verbatim on its own `AgentStep`, so *what the model said* and *what the system ran from* can be
told apart.

`WorkflowService.SetPlanAsync` is the only writer of `AgentWorkflow.PlanJson`. The stored shape
is `source` (planner or fallback), `rationale`, `note`, and `steps[]` of `order`, `agent`,
`purpose`, `status` and `addedBy`. Each step's status (`pending`, `completed`, `failed`,
`skipped`) is settled as the runner records that agent. A reopened repair appends a second
diagnostic and strategist step, and a revision appends a strategist step. The web client's
`PlanPanel` displays it on the workflow page.

### 3.6.3 The five agents and their contracts

Each agent is a class in its own file under `agent/agents/`. It declares its own tool subset as
the constant `ALLOWED_TOOLS`, loads its prompt text from `agent/prompts/*.md` (substituted with
`string.Template`, never `str.format`), and validates its reply against a Pydantic model in
`agent/schemas.py`. Every input and output model uses `extra="forbid"`.

| Agent | Responsibility | Input contract | Output contract | Tools | Safe failure |
| --- | --- | --- | --- | --- | --- |
| **Planner** (`planner.py`) | Plan the run; decide whether clarification is needed | `PlannerInput`: `description`, `room_identified`, `asset_identified` | `PlannerOutput`: `steps` (2–3 × `agent`, `purpose`), `rationale` | none | `output: null` → the API stores the fallback plan |
| **Clarifier** (`clarifier.py`) | Ask at most two closed questions that would change what a technician does; zero is a correct answer | `RunRequest` (`description`, `room_id`, `asset_id`) | `ClarifierOutput`: `questions`, 0–2 × (`question_text` ≤ 300, `answer_type` ∈ `yes_no` / `single_select` / `short_text`, `options` 2–5 for `single_select`) | `get_room`, `get_asset` | `questions: []` with `status: safe_failure` → the run is `Failed` |
| **Diagnostic** (`diagnostic.py`) | One to three causes from the asset's history, each with confidence and evidence, and one next action | `DiagnosticInput`: `description`, `room_id`, `asset_id`, `clarification_answers` | `DiagnosticOutput`: `hypotheses` (1–3 × `cause`, `confidence`, 1–5 `evidence`), `primary_hypothesis_index`, `recommended_next_action` ∈ `inspect` / `repair` / `replace` / `monitor`, `reasoning_summary` ≤ 400 | `get_asset`, `get_asset_service_history`, `get_related_open_reports` | `output: null` |
| **Resolution Strategist** (`strategist.py`) | One strategy, an estimated cost, an urgency and a justification | `StrategistInput`: `description`, `asset_id`, `diagnosis` (nullable), `revision_note`, `revision_work_order_id` | `StrategistOutput`: `strategy` (the six `WorkOrderStrategy` values in snake_case), `estimated_cost` (Decimal, 2 d.p., ≤ 10,000,000), `urgency`, `justification` ≤ 500, `consolidate_with_work_order_ids` | `get_asset`, `get_asset_service_history`, `get_open_work_orders` | `output: null`; also when it names a work order it was not shown |
| **Verification** (`verification.py`) | Judge whether a completed repair held, from the note, reports since, the history and the reporter's answer | `VerificationInput`, built from `RunRequest.verification` (`work_order_id`, `reporter_confirmed` nullable, `reporter_comment` ≤ 300) plus facts it looks up | `VerificationOutput`: `outcome` ∈ `confirm` / `reopen` / `escalate`, `confidence`, `reason` ≤ 400, `evidence` 1–5 | `get_work_order`, `get_asset_service_history`, `get_related_open_reports` | `output: null`; returned **without a model call** when the work order cannot be found |

The difference between safe-failure shapes is deliberate. For the clarifier, an empty list of
questions is a legitimate answer. For the others, an empty output would be a claim ("inspect",
"defer", "confirm") that nobody made, so their failure is `null`.

**There is no chat interface, and that is enforced rather than intended.** No output schema has
a free-text message field, no input schema accepts conversation history, and each agent runs
exactly one round per call. Each output model's field set is pinned exactly by a test (for
example `test_diagnostic_output_fields_are_exactly_the_contract`), and
`test_extra_fields_are_rejected_not_ignored` in `agent/tests/test_schemas.py` pins
`extra="forbid"`. **No output schema has an approval field**, and the strategist's prompt is
never told the approval threshold, so there is no figure for an estimate to be aimed beneath.

In the verification agent, every derived fact is computed by code before the prompt is
rendered: `days_since_completion` from an injected clock, `new_reports_since_completion` by
timestamp comparison, `service_visits_on_record` as a row count, and `is_this_repair` on each
visit. The model judges "this keeps happening"; it never does the counting.

### 3.6.4 Tool × agent matrix

The seven tools are the keys of the hardcoded dictionary in
`api/Controllers/InternalToolsController.cs`. Each handler delegates to the service that owns the
data; none queries `AppDbContext` in the controller.

| Tool | Id refers to | Service method | Cap | Planner | Clarifier | Diagnostic | Strategist | Verification |
| --- | --- | --- | --- | :---: | :---: | :---: | :---: | :---: |
| `get_room` | Room | `IRoomService.GetByIdAsync` | — | | ✓ | | | |
| `get_building` | Building | `IBuildingService.GetByIdAsync` | — | | | | | |
| `get_asset` | Asset | `IAssetService.GetAssetContextAsync` | — | | ✓ | ✓ | ✓ | |
| `get_asset_service_history` | Asset | `IAssetService.GetRecentServiceHistoryAsync` | 20, newest first | | | ✓ | ✓ | ✓ |
| `get_related_open_reports` | Asset | `IReportService.GetOpenReportsForAssetAsync` | 10 | | | ✓ | | ✓ |
| `get_open_work_orders` | Asset (answers for its whole room) | `IWorkOrderService.GetOpenWorkOrdersInAssetRoomAsync` | 10 | | | | ✓ | |
| `get_work_order` | Work order | `IWorkOrderService.GetWorkOrderFactsAsync` | — | | | | | ✓ |

**Least privilege.** Every agent's subset differs from every other's. The planner has none; it
judges the report's own words. The clarifier cannot read service history, because reading
history is diagnosis. `get_open_work_orders` belongs to the strategist alone, because
consolidating visits is a question about the work, not the fault. `get_work_order` belongs to the
verification agent alone. `get_building` is in the C# allow-list but in no agent's subset.

**Tools return facts, never judgements.** Each answers with a row, a list of rows, or nothing.
There is deliberately no `diagnose`, `assess` or `recommend` tool, and no tool that raises,
approves or re-costs a work order. `TheAllowListHasNoJudgementTool` and
`TheAllowListHasNoToolThatActsOnAWorkOrder` in `api.Tests/AgentToolTests.cs` ask for such names
and expect 404. The list tools return *fact* DTOs (`ToolServiceVisitDto`, `ToolReportDto`,
`ToolWorkOrderDto`) that leave out technicians' names, reporter ids and photo URLs, because every
tool response is copied into `AgentStep.PayloadJson` and into an LLM provider's prompt. A null
result and an empty list are different answers: an unknown asset is `found: false`, an asset
with no history is `found: true` with an empty list. The row caps are constants in the services,
not request fields, so the caller cannot widen how much one call can pull.

### 3.6.5 Why the tool allow-list is hardcoded in C#

Two allow-lists exist. Each agent's `ALLOWED_TOOLS` is checked by `ToolClient.call`
(`agent/tools.py`) before a request leaves the process, which catches a programming error early.
That check is a convenience, not the security boundary. The boundary is the static
`AllowedTools` dictionary in `InternalToolsController`, compiled into the API and compared
ordinally, on the far side of a network hop from anything a model can influence.

It is hardcoded because the list of capabilities must not be describable, let alone changeable,
by anything a model outputs. Read from configuration, from the request or from a prompt, a
manipulated model would have a path, however indirect, to naming a capability into existence.
As compiled code it can only be changed by a pull request and a review.

The router applies four checks, in order:

1. **No secret, or the wrong one** → 401. `AgentSecretFilter` runs as an authorisation filter,
   before model validation, so a caller without the secret never gets a 400 revealing the
   expected body. Nothing is recorded, because there is no workflow to trust yet.
2. **A workflow that does not exist** → 400.
3. **A workflow that has ended** (`Failed` or `Closed`) → 409, nothing recorded. This is the run
   the API gave up on while the agent was still working, and its late calls must not keep
   writing onto it. There is one exception: a `verification` call on the report's *latest*
   workflow while a check on that report is waiting on the agent
   (`IVerificationAgentService.IsJudgingOnWorkflowAsync`). A confirmed repair's workflow is
   `Closed`, and the agent must still read the repair it is judging. The database opens that
   exception, never the agent name alone.
4. **A tool name that is not a key** → a warning, an `AgentStep` with `ValidationResult`
   `RejectedUnknownTool` (so the attempt is in the audit trail), and 404.

Every call that passes checks 1–3 writes an `AgentStep`, whether it was allowed or rejected.
`ToolCallRequest` ids are `[Range(1, …)]`.

### 3.6.6 The LLM loop: retry once, then fail safely

`LlmClient.complete_json` (`agent/llm_client.py`) is one loop shared by all five agents. It sends
a system and a user prompt, extracts the JSON object from the reply (tolerating code fences and
surrounding prose), and validates it against the agent's schema. If parsing or validation fails,
it makes **exactly one** further attempt, with the rejected reply and the validation error
appended (`agent/prompts/json_retry.md`). `MAX_ATTEMPTS` is 2; there is no unbounded loop, no
retry library and no back-off. After the second failure it returns `LlmJsonResult(ok=False)`,
which each agent turns into its safe-failure shape.

The client guarantees two things: it **never raises** (every provider exception, timeout and
malformed body is caught) and it **never hangs** (every call carries `llm_timeout_seconds`,
default 30 s; tool calls carry `tool_timeout_seconds`, default 10 s). A safe failure is an
ordinary 200 from `/run`, because the caller is a background worker recording a step.

Provider structured-output features (`response_format`, JSON mode, native tool calling) are
deliberately not used. They differ between providers and may be absent on a local model, which
would break the rule that changing provider is a configuration change only (`LLM_BASE_URL`,
`LLM_API_KEY`, `LLM_MODEL`). "Valid JSON" is also not "schema-valid", so validation would be
needed regardless. Pinned by `test_malformed_output_triggers_exactly_one_retry` and
`test_the_retry_carries_the_validation_error`.

### 3.6.7 Prompt-injection defence

Every piece of text an agent reads that a person typed is untrusted: the report, the
clarification answers, technicians' notes, a manager's revision note, a reporter's comment. The
defence has a structural half and an instructional half, and neither is relied on alone.

- **Structural.** Untrusted text is never spliced into a prompt raw. Each agent serialises its
  data into one object with `json.dumps` and places it between `--- BEGIN DATA ---` and
  `--- END DATA ---` markers. JSON escapes every newline inside a string, so a description
  containing a newline, `--- END DATA ---` and a new heading cannot put a closing marker on a
  line of its own. Each of the five agents has
  `test_injection_text_cannot_leave_the_data_block`, which fails if the encoding is replaced by
  a raw splice.
- **Instructional.** Each system prompt says that everything between the markers is data, not
  instructions, and names the likely manipulations (ignore the rules, pick an action, mark it
  approved).
- **Blast radius.** Because a model can still be persuaded, what a persuaded model could achieve
  is limited. Outputs are capped by schema, tools are read-only and enforced in C#, a
  consolidation naming an unseen work order is refused, and nothing an agent produces moves a
  workflow or approves an order. Whether a model resists injection is measured by the live
  evaluations under `agent/evals/`, excluded from CI because they cost money.

---

## 3.7 Human approval

### 3.7.1 The workflow state machine

Figure 3.2 transcribes the `Table` dictionary in `api/Services/WorkflowTransitions.cs`: **22
entries** over the eleven members of `WorkflowState`, each edge labelled with a member of
`WorkflowTrigger`. `Failed` is included so that a run whose agent service is unreachable ends
somewhere a poll can see. The verification outcomes are modelled as *edges* out of
`AwaitingVerification`, not as states: a reopened workflow is back in `Diagnosing`, not sitting
in a "reopened" state.

**The table is keyed by (state, trigger), not by pairs of states.** A state-only table answers
only "may a workflow go from A to B?", and the same pair can be reached by events that must not
stand in for one another. `AwaitingManagerApproval → WorkOrderRaised` means a manager *approved*.
Under a state-only table, raising a second order on the same report could take that edge and
read, in the history, as an approval nobody gave (pinned by
`ASecondOrderOnTheSameReport_CannotPassForAnApproval`). Likewise `PlannedWithoutClarification`
and `ClarifierFoundNothing` both lead `Submitted → Diagnosing`, but "the clarifier did not run"
and "it ran and asked nothing" are different facts.

`WorkflowTransitions.Move(workflow, trigger)` is **the only way a state changes**. Every service
calls it: `WorkflowService` (the runner's moves), `WorkOrderService` (raise, resubmit, approve,
reject, revision, complete), `ClarificationService` (answered) and `VerificationService` (the
sweep and the reporter's answer). An illegal move throws `InvalidWorkflowTransitionException`
before anything is saved, and `ExceptionHandlingMiddleware` returns **409**. As a backstop,
`AppDbContext.CheckWorkflowTransitions` checks every changed `CurrentState` on save against
`CanReach`. It is weaker than `Move`, because at save time the event is not visible, and it
limits a unit of work to one step per save, which is why the runner saves after each transition.

### 3.7.2 The two human pauses

| Pause | State | Who | How it is left |
| --- | --- | --- | --- |
| 1 | `AwaitingClarification` | The reporter who filed the fault (Flutter) | `POST /api/reports/{id}/clarifications`: the whole form in one request, every answer checked in C#, then `ReporterAnswered` → `Diagnosing` and re-queued |
| 2 | `AwaitingManagerApproval` | A Facilities Manager (React approval queue) | `approve` (→ `WorkOrderRaised`), `reject` with a reason (→ `Closed`, report closed), or `request-revision` with a note (→ `Strategizing`, order back to `Draft`, re-queued for the strategist) |

**The high-impact action is spending money or replacing equipment.** The gate is
`RouteThroughGateAsync` in `WorkOrderService`, called by both `CreateAsync` and `ResubmitAsync`,
so there is one copy of the rule. An estimate **strictly above** `Approval:CostThreshold` (Rs
15,000 by default), or the strategy `EscalateReplacement` at any cost, routes the order to
`AwaitingApproval` and the workflow to `AwaitingManagerApproval`. Anything else is approved at
once. The rule is evaluated in C# on a `decimal` before the row is written; `CreateWorkOrderDto`
has no `Status` field, so a caller cannot post `Approved` and skip the comparison.

**Every approval event is on the audit trail.** `ApprovalAudit` writes an `AgentStep` named
`approval` for each event: when an order is raised (`ApprovalRequired` or `AutoApproved`, with
the threshold it was measured against and who put it through the gate), and for each manager
decision (`ManagerApproved`, `ManagerRejected` with the reason, `RevisionRequested` with the
note, carrying the manager's user id). It is written in the same save as the move it records.

**A decision cannot be taken twice.** Each decision is one transaction that first **claims** the
order with a conditional `UPDATE … WHERE Id = @id AND Status = 'AwaitingApproval'`. Two managers
who both read `AwaitingApproval` cannot both decide: the loser's claim updates no rows and gets
the same 409, and the audit trail never holds two contradictory decisions (pinned by
`ADecisionThatLosesTheRace_WritesNothing`).

**The revision loop is closed.** A revision re-runs the strategist alone with the manager's
note, which reaches the model inside the JSON data block, like any other untrusted text. A
usable revised proposal resubmits the same Draft through the same gate. While a Draft waits, a
second order on the report is refused, so the Draft and its note cannot be orphaned (pinned by
`ARevision_RunsTheStrategistAlone_WithTheNote_AndResubmitsTheSameOrder`).

### 3.7.3 The loops back into Diagnosing

Two edges return a workflow to `Diagnosing`, and `AgentWorkflow.ReopenedWorkOrderId` is what
tells them apart.

- **Clarification.** `ClarificationService.SubmitAnswersAsync` writes the answers, moves the
  report to `Clarified` and the workflow along `ReporterAnswered`, all in one `SaveChanges`, and
  only then re-queues, so the runner cannot read the workflow before the answers are committed.
- **A repair that did not hold.** When the reporter answers "no" to "is the problem fixed?",
  `VerificationService.RecordReporterResponseAsync` sets the check to `Reopened`, stamps
  `AgentQueuedAt`, moves the latest workflow along `RepairReopened` and records the failed order
  in `ReopenedWorkOrderId`, in one `SaveChanges`, then re-queues it. The diagnostic's tools now
  return a history that includes the failed repair's own `ServiceRecord`. The first diagnosis is
  *not* passed to the agent: a second opinion anchored on the first would not be independent.
  Pinned by `AReopenedRepair_RunsTheDiagnosticAgain_OnTheOrdersAsset_AndKeepsBothDiagnoses`.

**The report moves with its workflow.** `ReportProgress` (`api/Services/ReportProgress.cs`) maps
the triggers that mean something for the *fault* onto the report: `Diagnosed` → `Diagnosed`, a
raised order → `WorkOrderRaised`, `ManagerRejected` or `RepairVerified` → `Closed`. Each move is
checked against the report lifecycle and skipped, never forced, when the lifecycle refuses it.
`ReportProgress.StageFor` gives the reporter's plain-language stage (Being reviewed, Waiting on
you, Awaiting approval, Repair planned, Repaired, Not going ahead, Closed), which the phone shows
on each report. That is how the updated status returns to the user who started the workflow.

---

## 3.8 The verification loop

`VerificationSweepService` runs `IVerificationService.ProcessDueChecksAsync` on a timer and on
the "Run sweep now" button. It has three steps, each row saved on its own:

1. **Workflows.** A `Completed` workflow whose `CompletedAt` is at least `DelayDays` (default 5)
   old moves along `VerificationDue` to `AwaitingVerification`.
2. **Ask.** A `Pending` check past its `DueAt` becomes `AwaitingReporterResponse`, and
   `ProcessedAt` is stamped. The state change *is* the notification: the check appears on the
   reporter's phone.
3. **Hand to the agent.** An answered check, or one still unanswered after `ResponseWindowDays`
   (default 3), gets `AgentQueuedAt` stamped. **The row is the queue**, not a `Channel`, because
   the free tier sleeps and an in-memory queue would be lost. Every pass rings
   `IVerificationAgentSignal`.

`VerificationAgentRunner` drains checks that are *queued and not judged since*
(`VerificationAgentRules.AwaitingJudgement`), oldest first. Each check is sent to `/run` on the
report's latest workflow with `verification` { `work_order_id`, `reporter_confirmed` (null for
silence), `reporter_comment` }. On success it writes, in one save, a `verification` `AgentStep`
and `AgentOutcome`, `AgentReason`, `AgentEvidenceJson` and `AgentJudgedAt`. **It never writes
`Status`**: the check's status is the reporter's answer, set in C#. The attempt count is bounded:
at most `VerificationAgentRules.MaxAttempts` = 3 calls per queue stamp, each counted and saved
*before* the call, so a process that dies mid-call has still used one. A call failure is retried
on a later pass; a safe failure is final at once, because the agent already retried its model.
If an answer arrives while the agent is judging the silence, the verdict goes on its step only
and the check stays queued for a second judgement *with* the answer (pinned by
`AnAnswerThatArrivesWhileTheAgentIsJudging_IsNotLost` and
`ALateAnswer_IsJudgedAgain_WithTheAnswer`).

---

## 3.9 Shared state and observability

The specification's list of shared state maps onto the schema (Figure 3.5) as follows. All of it
is durable PostgreSQL, readable through the API.

| Required state | Where it is persisted |
| --- | --- |
| Workflow id and objective | `AgentWorkflows.Id`, `Objective` (the report's description verbatim) |
| The plan and its step statuses | `AgentWorkflows.PlanJson` (`jsonb`, via `PlanRules.Serialize`) |
| Completed steps, with timings and retries | One `AgentSteps` row per agent run: `AgentName`, `PayloadJson` (the output verbatim), `DurationMs`, `Attempts` |
| Tool calls and tool results | One `AgentSteps` row per tool call, written by the tool router: `PayloadJson` { `Tool`, `Found`, `Result` }, rejected tools included |
| Validation results and errors | `AgentSteps.ValidationResult` (`Ok`, `NotFound`, `RejectedUnknownTool`, `SafeFailure`, `CallFailed`, `Rejected`), `AgentSteps.ErrorMessage` |
| Approval status and decisions | `WorkOrders.Status`, `ApprovedByUserId`, `ApprovedAt`, `RejectionReason`, `RevisionNote`, plus `approval` steps on the workflow's trail |
| Current state and final outcome | `AgentWorkflows.CurrentState`, `Outcome` (the result, or the reason for a safe failure), `CompletedAt` |
| Working copies of agent output | `ClarificationQuestions` (the questions as a form), `VerificationChecks.AgentOutcome` / `AgentReason` / `AgentEvidenceJson` |

What is **not** stored: no hidden chain-of-thought (the diagnostic's `reasoning_summary`, capped
at 400 characters, is a displayed summary), no passwords except the hash in `Users`, no tokens,
no API keys, and no names or reporter ids in tool payloads.

The audit copy and the working copy are kept apart on purpose. `AgentStep.PayloadJson` records
what an agent said at the moment it said it and is never edited. The working rows (questions a
reporter answers, a check's verdict) are what the application queries. Collapsing them would give
either an audit trail that gets edited or a blob that nobody can answer one question at a time.

**Observability in the product.** `GET /api/workflows/{id}` (Facilities Manager, Admin) returns
the state, the typed plan, every step and every diagnosis. The web client renders these on the
workflow page and the report page through one component, `AuditTrail.jsx`. Each row gives the
agent, whether it was an agent run, a tool call or an approval, an outcome pill, the duration,
the attempts, and a one-sentence description. A failed step shows its reason inline, and the raw
`ValidationResult`, `ToolCallsJson` and `PayloadJson` are one click away, verbatim.
`PlanPanel` shows the plan and `DiagnosisComparison` places a reopened fault's diagnoses side by
side.

---

## 3.10 How the minimum agentic workflow is met

| Specification §9.1 | MaintenX |
| --- | --- |
| Receive a domain objective | A report's description becomes `AgentWorkflow.Objective` |
| Create a structured multi-step plan | Planner Agent → `PlannerOutput`, re-checked by `PlanRules`, stored in `PlanJson` (§3.6.2) |
| Delegate steps to distinct agent roles | `_route_after_plan` delegates from the validated plan; five agents with separate responsibilities, contracts and tool subsets (§3.6.3) |
| Allow-listed tools, validated inputs, structured outputs | The hardcoded C# dictionary, the shared-secret filter, `[Range]` ids, fact DTOs, `found`/`result` envelopes (§3.6.4–3.6.5) |
| Persist workflow state | `AgentWorkflows`, `AgentSteps`, `ClarificationQuestions`, `VerificationChecks` (§3.9) |
| Deterministic checks | Pydantic schemas with one retry; `PlanRules`; `RaisableProposal`; the approval gate; `WorkflowTransitions`; `ClarificationService` answer checks |
| Pause a high-impact action for an authorised user | Orders above the threshold or replacing equipment wait for a Facilities Manager to approve, reject or request revision (§3.7.2) |
| Auditable result or safe failure | Every agent run, tool call and approval decision is a step; failures end in `Failed` with the reason on `Outcome` |
| At least four distinct agents | Five: Planner, Clarifier, Diagnostic, Resolution Strategist, Verification |
| Security: roles, input and output validation, secrets, timeouts, retry limits, safe failure | §3.1, §3.6.5–3.6.7, §3.4 |

---

## 3.11 The end-to-end loop

Figure 3.4 traces one fault through the whole system.

1. A reporter files a report on the phone, scanning the sticker. The API returns 201 and
   enqueues the workflow.
2. The runner calls `/run`. The planner plans, the clarifier looks up the room and the asset
   through the tool router and asks two questions, and the run stops (pause 1).
3. The reporter answers on the phone in one request, which moves the workflow to `Diagnosing` and
   re-queues it.
4. The second `/run` goes straight to the diagnostic and the strategist. The runner records both,
   moves the workflow to `Strategizing`, and raises the proposal through the gate. A replacement
   waits in `AwaitingManagerApproval` (pause 2).
5. The Facilities Manager approves in the React approval queue, assigns a technician and books a
   slot the timetable leaves free.
6. The technician completes the job on the phone. In **one database transaction**
   (`WorkOrderService.CompleteAsync`) the order becomes `Completed`, a `ServiceRecord` is
   appended, the workflow moves to `Completed`, and a pending `VerificationCheck` is raised.
7. After `Verification:DelayDays` the sweep moves the workflow to `AwaitingVerification` and asks
   the reporter.
8. A "no" moves the workflow along `RepairReopened` back to `Diagnosing`, and the runner diagnoses
   again, this time reading a history that contains the failed repair. The verification agent
   records its opinion beside the reporter's answer.

The same path, with `IAgentClient` replaced by a scripted stub and the clock movable, is asserted
state by state, including the report's status and the reporter's stage after every step, in
`api.Tests/WorkflowEndToEndTests.cs`.

---

## 3.12 Known limitations

| Capability | What exists | What is missing |
| --- | --- | --- |
| Escalating a repeat failure | The `RepairEscalated` edge; the verification agent's `escalate` label | Anything that fires `RepairEscalated`. Whether a C# rule or a manager should decide is undecided, so the label stays advice |
| Starting a job | The `WorkStarted` edge and the `InProgress` states | An endpoint that starts a job. Completion goes `WorkOrderRaised → Completed` directly, so `InProgress → Completed` is never taken |
| Consolidated jobs | The strategist may propose `consolidated_job` with order ids it was shown | The ids are not acted on; the order is raised with that strategy and nothing is linked on a model's say-so |
| Live end-to-end verification agent run | The agent is evaluated live against a real model (4/4, 30 Sep 2026), and the C# runner is tested against a scripted agent | The C# runner and a live model have not yet been run together end to end |
| Durable queue | The workflow rows persist, and the startup re-queue recovers every unfinished run | The queue itself is in memory: a run interrupted by a crash waits until the API next starts, rather than being picked up by another instance (the API runs as one instance) |
| SLA on the phone | `WorkOrderDto.Sla` and the web's `SlaPill` | The technician's app does not show it yet |
