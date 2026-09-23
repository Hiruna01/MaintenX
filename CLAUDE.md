# CLAUDE.md

Conventions for this repository. These apply to **all** future sessions — read this file
before writing any code, and follow it over any general-purpose habit or "best practice"
that contradicts it. Where this file says "exactly", it means exactly.

## Repository layout

```
api/                 ASP.NET Core Web API (.NET 8) — single project
agent/               Python FastAPI service
web/                 React 18 + Vite, JavaScript (not TypeScript)
mobile/              Flutter
docs/                Design docs, ADRs, diagrams, report material
.github/workflows/   CI
```

**CI runs on every push and pull request to `main`** — `.github/workflows/ci.yml`, four
independent jobs: `api` (build, then xUnit against a **PostgreSQL 16 service container**
with the migrations applied), `agent` (pytest in `STUB_MODE`), `web` (`npm ci` +
`npm run build`) and `mobile` (`flutter analyze`).

**The workflow contains no secrets and needs none** — the agent job runs stubbed so no LLM
key is required, and the api job's database is a throwaway container reachable only from
that job. If a job ever needs a real credential it goes in GitHub repository secrets as
`${{ secrets.NAME }}`, never in the file.

Not yet covered by CI, so still run by hand: `flutter test`, `npm run lint`.

**`.gitattributes` normalises every text file to LF** (`* text=auto eol=lf`). It is not
decoration: this is a mixed macOS/Windows team, and without it a checkout on Windows can
show the entire tree as modified when nothing has changed. After pulling a change to that
file, run `git add --renormalize .` once. Do not commit a file with CRLF line endings.

---

## BACKEND — follow this structure exactly

- **Single ASP.NET Core project.** No Clean Architecture. No separate Domain / Application /
  Infrastructure projects.
- **Organise BY LAYER**, not by feature:

  ```
  api/Controllers/
  api/Services/
  api/Models/
  api/Dtos/
  api/Data/
  ```

- **Every service is an interface + implementation** — `IAssetService` / `AssetService` —
  supplied by **constructor injection**. No service locator, no `new`-ing a service inside
  a controller.
- **Services backed by EF Core are registered `AddScoped`, never `AddSingleton`.**
  A singleton holding a scoped `DbContext` is a captive dependency bug.
- **Controllers are THIN**: receive the request, call the service, return the right status
  code. No business logic in controllers.
- Controllers use `[ApiController]` and `[Route("api/[controller]")]`.
- **DTOs are `record` types in `Dtos/`.** Input DTOs carry DataAnnotations validation and
  **never include `Id`** — the server assigns it. **Never return an entity directly from a
  controller**; map to a response DTO.
- **Money is `decimal`**, never `double` or `float`.
- **Status codes:**

  | Situation           | Code                        |
  | ------------------- | --------------------------- |
  | Read                | 200                         |
  | Create              | 201 via `CreatedAtAction`   |
  | Update / delete     | 204                         |
  | Not found           | 404                         |
  | Validation failure  | 400                         |
  | Unauthenticated     | 401                         |
  | Wrong role          | 403                         |

- **Not used in this project:** MediatR, AutoMapper, the repository pattern, a `Result<T>`
  wrapper. Services talk to `DbContext` directly and throw or return nulls that the
  controller turns into status codes.
- **Enums are strings on both sides**, not just in the database. `AddControllers()` in
  `Program.cs` registers a `JsonStringEnumConverter`, so `Role` (and any future enum) is
  sent and accepted in JSON as its name (`"FacilitiesManager"`), never its ordinal. This
  keeps the JSON contract consistent with what's stored in Postgres and what's inside a
  JWT claim — a client should never need to hardcode an enum's integer value.

## AUTH — hand-rolled, not ASP.NET Core Identity

- JWT bearer only. Symmetric signing key, issuer and audience all from configuration —
  see `JwtSettings` in `Services/`, never a literal in code.
- Claims are exactly `sub` (user id), `email`, `role`. `role` is the enum's string name.
- **Access token only, 12-hour lifetime. No refresh tokens.** This is a deliberate scope
  decision (a refresh flow needs a persisted token store, rotation, reuse detection and
  revocation — a separate feature). Keep the code comment explaining this in
  `JwtSettings.cs`; it's a viva question.
- Passwords hashed with `Microsoft.AspNetCore.Identity`'s `PasswordHasher<T>` — that's the
  one Identity type this project does use, registered `AddSingleton` because it's
  stateless and holds no `DbContext`. Everything else Identity-related is out.
- Authorization policies are generated one-per-`Role`-enum-member in `Program.cs`
  (`options.AddPolicy(role, p => p.RequireRole(role))`), not hand-listed, so a new role
  gets a matching policy automatically.
- **401 vs 403 are both required and must stay distinct**: no token → 401 (who are you?),
  valid token with the wrong role → 403 (I know who you are, and no). An evaluator may
  ask for both cases explicitly.
- Request logging (Serilog) must never capture request bodies — only method/path/status/
  duration — specifically so a login or register payload's password never reaches a log
  sink. Don't add body logging to `UseSerilogRequestLogging`.

---

## ASSET REGISTRY — what exists, and what has been done to it

`AssetCategory`, `Asset` and `ServiceRecord` in `Models/`, with `AssetStatus` and
`ServiceOutcome` persisted as strings like every other enum. `IAssetService` /
`AssetService`, `AddScoped`, behind `AssetsController` and `AssetCategoriesController`.

- **`Asset.AssetTag` is the QR payload**, unique across the estate: scanning a sticker
  yields that string and nothing else, so it has to identify exactly one machine.
  **It is not editable.** `UpdateAssetDto` deliberately has no `AssetTag` field — renaming
  the row would silently orphan every sticker already printed and applied, with no way to
  tell from the database that it had happened. A mislabelled asset gets a new sticker and a
  new row.
- **Dates are `DateOnly`, not `DateTime`** (`date` in PostgreSQL). An installation or a
  service visit is a calendar date; a time component invites a timezone conversion that
  moves it across midnight.
- **Assets are never deleted.** `AssetStatus.Retired` is how equipment leaves the estate.
  Every foreign key into the registry is `DeleteBehavior.Restrict`, so deleting a room or a
  category out from under an asset fails loudly rather than quietly taking its service
  history with it. That history outlives the working life of the machine it describes.
- **`IAssetService.CreateAsync` returns null for three different reasons** — unknown
  category, unknown room, or a tag already in use. There is no `Result<T>` in this project,
  so `TagExistsAsync` sits beside it: a controller calls that first to tell a 409 from a
  400, the same way `InternalToolsController` calls `IWorkflowService.ExistsAsync` before
  choosing its status code.
- Detail reads return the service history **oldest-first** — the opposite of the workflow
  list. It is read to follow a machine over time, and a repeat failure only reads as one in
  the order it happened.

### The endpoints — reads for everyone, writes for Admin

`[Authorize]` on both controllers with no policy, and
`[Authorize(Policy = nameof(Role.Admin))]` per write action. A technician looks up a
machine's history before a visit and a reporter scans a sticker, so a role check on the
reads would break both; deciding what equipment the estate contains is an Admin's job.
That split is also what keeps **401 and 403 separately demonstrable** on one controller —
no token is 401 everywhere, a Reporter's token is 200 on a read and 403 on a write.

- `GET /api/assets` is the only paginated one, through the existing `PagedResult<T>`.
  **There is no second pagination type.** Search matches name **or** tag in one box,
  because a tag is what someone holding the equipment has; `categoryId`, `roomId` and
  `status` are exact filters and all of them combine. `status` and `sort` bind by enum
  **name**, so an unknown value is a 400 from model binding rather than a filter that
  silently matches nothing.
- **The search is `ToLower().Contains()`, never `EF.Functions.ILike`.** ILike is
  Npgsql-only and the tests run on SQLite by default, so it would be a runtime failure
  nothing on a developer's machine would catch. Lowering both sides is also what makes the
  two providers agree: SQLite's `LIKE` is already case-insensitive and PostgreSQL's is not.
- Every sort carries `ThenBy(Id)`. Without a total order two assets sharing a name — or an
  installation date, which many do, since equipment arrives in batches — can order
  differently between two queries, and a row appears on both page 1 and page 2, or on
  neither.
- `GET /api/assets/by-tag/{assetTag}` is the QR path. An unknown tag is a **404**, not an
  error: a sticker from some other system is a miss.

#### `DELETE /api/assets/{id}` retires, it does not delete

The verb is right for the client — "this equipment is gone" is what the caller means — and
how the registry honours that without losing the history is the registry's business. The
service method behind it is called **`RetireAsync`**, not `DeleteAsync`, so nothing reads
as a row removal at the call site. It sets `Status` to `Retired` and returns 204; an asset
that is already retired still returns 204, because the caller asked for it to be out of
service and it is.

A real delete would not quietly take the service history with it — every foreign key into
the registry is `Restrict`, so it would throw out of the driver as a 500 for any asset that
has ever been serviced, reported on or worked on, which is every asset anyone would want to
delete. Pinned by a test that deletes an asset **with** history and then reads both back.

#### `GET /api/assets/{id}/failure-summary` — the business operation

Not CRUD, and **not an agent call**. `failureCount12Months`, `failureCount3Months`,
`lastServicedOn`, `daysSinceLastService`, `temporaryFixCount`, `isUnderWarranty`,
`isRepeatFailure` and `distinctOutcomes` are every one of them a count or a date
comparison, computed in C# over the materialised history. Failure counts and warranty
dates are named in PROJECT RULES as deterministic business rules; the diagnostic agent
**reads** this summary through a tool call, it never produces one.

- **"Three months" means exactly 90 days**, and `isRepeatFailure` (3 or more visits) reads
  that same cut-off rather than a second one of its own. `AddMonths(-3)` is 89, 90, 91 or
  92 days depending on the month, and a summary reporting two visits this quarter beside a
  repeat-failure flag would be read as a bug — and would be one.
- Computing it in memory rather than in SQL is also what makes it behave identically on
  both test databases: `DateOnly` arithmetic does not translate to SQLite, where dates are
  `TEXT`. Same reasoning as evaluating the approval threshold in C#.
- `lastServicedOn` and `daysSinceLastService` are **nullable, and null is not zero** — a
  machine nobody has ever touched is not a machine serviced today. Same rule as
  `VerificationCheck.ReporterConfirmed`.
- A null `WarrantyExpiresOn` reads `isUnderWarranty: false`. "No warranty recorded" is not
  the same fact as "expired", but it is not cover either.

Categories have **no DELETE**: the foreign key from `Asset` is `Restrict`, so removing one
anything is filed under fails at the database, and a category with nothing under it is not
worth an endpoint and a role check to tidy away. `CreateAssetCategoryDto` serves both the
create and the update, the way `CreateRoomDto` does — a category is its name and its
default warranty and nothing else, so it has no field that may be set once and never
changed. Changing `DefaultWarrantyMonths` does not touch existing assets: it is a default
for data entry, and an asset's own `WarrantyExpiresOn` is what every warranty decision
reads.

### `ServiceRecord` is deliberately NOT `WorkOrder`

A `WorkOrder` is live work in progress: assigned, scheduled, still changing. A
`ServiceRecord` is what is left once that work is finished and will not change again. A
completed work order **appends** a `ServiceRecord`.

They are separate because they are read for different reasons. The diagnostic agent reads
this table to find repeat failures across months of history: it has no business seeing
half-finished work, and history must not shift under it every time a technician updates a
live order. Merging them would also put a scheduling lifecycle and an immutable record in
one table, which is two jobs. Do not merge them.

**`ServiceRecord.WorkOrderId` is now a real foreign key** (`AddWorkOrders`), `Restrict`,
with no navigation property on either side. It stays **nullable**, legitimately: seeded and
imported history was never produced by a work order.

### The Development seed data is load-bearing, not filler

The diagnostic agent is written against it and cannot be developed or demonstrated without
history that actually contains a repeat failure. `DbSeeder` creates 4 categories, 8 assets
and 14 service records.

- **The three records on `PRJ-MAB101-01` are the planted pattern**: one visit finding
  nothing, then the same fault returning twice with the same thermal root cause and two
  temporary fixes, escalating over four months to a replacement recommendation. Read
  individually each note is unremarkable; read together they are a failing unit.
- **That asset is left `Active` on purpose.** It works between failures, so nothing on the
  asset row itself looks wrong — the pattern exists only in the history, which is exactly
  the problem the agent has to solve. Do not "fix" the status.
- **The notes are written the way technicians write them** — terse, abbreviated, and vague
  where a real note would be vague. The agent's job is reading unstructured text, and clean
  prose here would make it look better than it is. Do not tidy them.

---

## AGENT WORKFLOWS — background orchestration, not a blocking call

- `AgentWorkflow` (one row per objective) and `AgentStep` (one row per agent action) live in
  `Models/`, same layer rule as everything else. `CurrentState` is a C# enum
  (`WorkflowState`) persisted as a string, same as `Role`.
- `PlanJson`, `ToolCallsJson` and `PayloadJson` are PostgreSQL **`jsonb`** columns, not
  `text` — configured in `AppDbContext`. Index `AgentStep.WorkflowId`.
- **`POST /api/workflows` must never block on the agent service.** It creates the row,
  returns **202 Accepted** with the id, and hands the id to `IWorkflowQueue` (an in-process
  `Channel<int>`). `WorkflowRunner`, an `IHostedService`, drains the queue and does the
  actual work in the background, opening its own DI scope per item — the `DbContext` and
  `IWorkflowService` are scoped, so a singleton hosted service cannot hold them directly.
  Clients poll `GET /api/workflows/{id}` for progress. Do not add a synchronous HTTP call
  to the agent service inside a controller action.
- **The runner's outbound call is `IAgentClient`**, a typed `HttpClient` with its own
  timeout (`Agent:TimeoutSeconds`, default 60s). It **never throws**: a timeout, a refused
  connection, a non-200 or an unreadable body all come back as a result the runner turns
  into `Failed` with the reason on the row. A background exception has no request to
  surface on, so a workflow must never be left parked because the agent was down.
- **The runner records ONE agent-level step per run**, with `ToolCallsJson` empty. Tool
  calls are recorded by `InternalToolsController` alone — recording the agent's returned
  `tool_calls` here as well would double every tool call in the audit trail. It also writes
  the clarifier's questions as `ClarificationQuestion` rows; that is not a second step and
  not a second audit record — see CLARIFICATION below.
- **`AgentWorkflow.PlanJson` is still never populated.** The clarifier produces questions,
  and questions are not a plan, so they go in `AgentStep.PayloadJson` where step output
  belongs — and, because they are working data as well as audit, into
  `ClarificationQuestion` rows beside it. Do not render `PlanJson` in a client until an
  agent actually produces one — an always-null field on a page is worse than no field.
- **`POST /api/internal/tools/{toolName}`** is how the agent calls back into the API. It is
  authenticated by a **shared-secret header** (`AGENT_SHARED_SECRET`), not a JWT — there is
  no user behind these calls, so no role to check. Missing or wrong secret → 401.
- **The tool allow-list is a hardcoded `Dictionary<string, ...>` in C#**, never sourced from
  configuration or from the caller. A tool name not in the dictionary returns 404 and logs a
  warning. Keep it hardcoded; it's a viva question — the allow-list must not be describable,
  let alone changeable, by anything the model outputs.
- Every call to `/api/internal/tools/{toolName}` — allowed or rejected — writes an
  `AgentStep` row, so the audit trail is complete even for rejected calls.

### The tools return facts, never judgements

`get_room`, `get_building`, `get_asset`, `get_asset_service_history` and
`get_related_open_reports`. Every one of them answers with a row, a list of rows, or
nothing. **There is deliberately no `diagnose`, `assess` or `recommend` tool**, and there
is not going to be one: a tool that returned a judgement would be handing the model its
own opinion back wearing the API's authority, and it would be unauditable the moment it
mattered. What the facts *mean* is the agent's job; any rule the system acts on is C#
somewhere a person can read it. Pinned by a test that asks for four such names and
expects four 404s.

- Each handler **delegates to the service that owns the data** — never a query written
  against `DbContext` in the controller, so a tool cannot grow a reading of the database
  that no service is responsible for.
- **The row caps are constants in the services** (`MaxToolHistoryRows` 20,
  `MaxToolRelatedReports` 10), not fields on `ToolCallRequest`. The agent sends a tool
  name and an id and nothing else, so how much one call can pull is not something the
  caller — or anything that has talked its way into the caller — can widen. Same instinct
  as the hardcoded allow-list.
- **The capped lists are newest-first, the opposite of `AssetDetailDto`**, and the cap is
  the reason: taking twenty rows off an oldest-first history returns the twenty *least*
  relevant visits and hides everything recent.
- **Null and empty are different answers.** An unknown asset id is `found: false`; an
  asset that exists with no history, or nothing open against it, is `found: true` with an
  empty list. Collapsing them would tell the agent a machine has a clean record when it
  had in fact asked about a machine that is not there.
- `get_related_open_reports` excludes `Closed` reports — the question is "is anyone else
  seeing this now", and a fault closed last year is history and belongs in the service
  record instead.
- `ToolCallRequest` still carries exactly one `Id`. What it *means* is the tool's
  business: a room for `get_room`, an **asset** for both of the new list tools.
- **On the Python side, `ToolCallOutcome.result` is `dict | list | None`.** It was
  `dict`-only when the list tools landed, so the first agent to call one would have made
  `ToolClient.call` raise — breaking its promise never to — and nothing noticed, because
  nothing called them yet. A new tool that returns a new shape needs that type checked.

**`DiagnosticAgent` declares the three asset tools; the C# side has not caught up.**
`AgentRunRequest` does not send `asset_id` yet, so the diagnostic has no asset to look up
and runs on the report text alone — and `AgentRunResponse` ignores the new `diagnosis`
field, so the runner does not persist it. Until both land, a diagnosis is computed and
discarded. See `DiagnosticAgent` under AGENT SERVICE below.

**The resume path needs a routing decision before it can be wired.** Submitting answers
moves the workflow to `Diagnosing` and re-queues it (see `POST /api/reports/{id}/clarifications`
below), but `/run` always runs `clarify` first — and the clarifier does not read
`clarification_answers`, so a resumed run would ask the questions just answered and put
the report back into `AwaitingClarification`. A loop. The fix is a conditional edge from
`START` in `graph.py` — go straight to `diagnose` when `request.clarification_answers` is
non-empty — which is exactly the kind of routing the file allows: plain Python reading the
state, never a judgement made by a model. It has to land with, or before, the C# resume.

---

## CLARIFICATION — the questions are rows, the answers are bounded

The clarifier's output is written **twice, to two different ends**, and neither replaces
the other:

- **`AgentStep.PayloadJson`** — the agent's reply verbatim, at the moment it produced it.
  This is the audit trail and it is never edited.
- **`ClarificationQuestion` rows** — the working data: the copy the application queries per
  report, orders, renders as a form and answers.

Collapsing the two is the mistake this split exists to prevent. An audit trail you edit is
not an audit trail, and a payload blob is not something a reporter can answer one question
at a time.

- `ClarificationQuestion` carries `ReportId` **and** `WorkflowId`, both indexed. The
  workflow id is not redundant: a report may be clarified by more than one run over its
  life, and this is what says which run asked what.
- **`AnswerType` is `YesNo` / `SingleSelect` / `ShortText`, and that is the whole list.**
  This enum is where "there is no chat interface" stops being an intention and becomes a
  constraint — every clarification comes back through a toggle, a picker over a fixed list,
  or a capped short string, and none of those is a message box. **Adding a `FreeText`
  member turns this project into the chatbot it deliberately is not.** It is a viva
  question.
- `OptionsJson` is a PostgreSQL **`jsonb`** column, null for every answer type except
  `SingleSelect` — a yes/no question carrying options would render as a control the agent
  never asked for.
- **A question has at most one answer**, enforced by a unique index on
  `ClarificationAnswer.ClarificationQuestionId`. One answer, not a thread, because there is
  no conversation.
- **`ClarificationQuestion.ReportId` is NOT nullable, so a workflow with no report writes
  no rows.** `POST /api/workflows` can still start a run from a bare objective, and a
  question about nothing has nobody to ask and nowhere to appear. The `AgentStep` still
  records what was asked, so the run is not invisible.

### The parse lives in one place, and it never throws

`AgentRunResponse.ParseQuestions()` is the **only** place the API reads the agent's
question shape, so the agent's snake_case `answer_type` values (`yes_no`, `single_select`,
`short_text`) are mapped onto the C# enum exactly once rather than wherever a reader
happens to need them.

It **skips** anything malformed instead of throwing — the caller is a background worker
with no request to surface an exception on — and `WorkflowRunner` logs a warning when the
parsed count falls short of what the agent sent. That shortfall should be impossible, since
the agent validates its own output against a Pydantic schema before sending it, which is
exactly why it is worth a warning: it means the two contracts have drifted apart.

### The status transition is C#, never the agent

`ClarificationService` moves the report to `AwaitingClarification` **in the same
`SaveChanges`** as the question rows, so the two can never disagree: a report is never left
saying `AwaitingClarification` with nothing to answer, nor `Submitted` with questions
sitting against it. The agent decides *what to ask*; what that means for the report is a
deterministic business rule and lives in C#.

### The unique index will not save a writer that has already loaded the answer

Worth knowing before the submit-answers endpoint lands, because it is genuinely surprising.
Inside a single `DbContext` that has **already loaded** the existing answer, EF resolves the
required one-to-one conflict itself: the old row is marked `Deleted` and the save
**succeeds by replacing the answer**. The index only stops a writer that knows nothing but a
question id.

A service that means "reject a re-answer" therefore has to look for one and say so — it
will not get a `DbUpdateException` for free. Pinned by a test that deliberately uses a
separate DI scope; the note is in `AppDbContext` next to the index as well.

### `Report` — three columns alongside

- **`AssetId`, nullable.** A reporter is not expected to know which asset tag the
  misbehaving projector carries, and a form that demanded one would be abandoned or
  answered with a guess. It is filled in later — by a QR scan at the point of reporting, or
  by whoever triages the report.
- **`PhotoUrl`, nullable** — a URL to wherever the image is stored, never the image bytes,
  which have no business in a row that is read on every list query.
- **`ReportStatus` is `Submitted` / `AwaitingClarification` / `Clarified` / `Diagnosed` /
  `WorkOrderRaised` / `Closed`**, a string in the database like every other enum.
  **Deliberately not a copy of `WorkflowState`.** A workflow state describes one agent run
  and can end in `Failed`, which says nothing about the fault still sitting in the room.
  This says where the *fault* has got to, and it survives a run that never completed.

---

## WORK ORDERS — the scheduling lifecycle

`WorkOrder`, `ScheduledSlot` and `ClassScheduleSlot` in `Models/`, with `WorkOrderStatus`
(`Draft` / `AwaitingApproval` / `Approved` / `Rejected` / `Scheduled` / `InProgress` /
`Completed` / `Cancelled`) and `WorkOrderStrategy` (`KnownFix` / `SingleJob` /
`ConsolidatedJob` / `InspectFirst` / `Defer` / `EscalateReplacement`) persisted as strings
like every other enum. There is no controller yet.

- **Money is `decimal`, and its precision is stated rather than inherited.**
  `EstimatedCost` and `ActualCost` are `numeric(18,2)`. Left undeclared, EF maps `decimal`
  to an unqualified PostgreSQL `numeric` whose scale is whatever each value arrives with,
  so the column would accept `4999.999999` and hand it to a threshold comparison meant to
  reason in rupees and cents. **A float here is not a display bug, it is money spent
  without authorisation**: an estimate that should sit exactly on the threshold can land a
  hair below it and auto-approve. It is a viva question.
- Input DTOs bound cost with the **decimal overload** of `[Range]`
  (`[Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true)]`), not
  `[Range(0, double.MaxValue)]` — that overload would validate money by parsing it through
  a binary double, which is the one thing this project refuses to do with money. The limits
  parse invariantly so a comma decimal separator cannot change what is accepted.
- **`WorkOrder.AssetId` is NOT nullable**, unlike `Report.AssetId`, and the difference is
  the point: a reporter is not expected to know which asset tag the projector carries, but
  nobody can be sent to repair a machine nobody has identified. It is also what makes the
  completed `ServiceRecord` land against the right service history.
- **`CreateWorkOrderDto` has no `Status`.** Every order is raised `Draft`, and whether it
  needs a manager's decision is decided by comparing `EstimatedCost` against the threshold
  in C#. A caller that could post a status could post `Approved` and skip that comparison —
  the approval control removed by the thing it is supposed to control.
- **Indexes:** `ReportId`, `AssetId`, `AssignedTechnicianId`, `Status` on `WorkOrder`;
  `RoomId` and `StartsAt` on `ClassScheduleSlot`, plus a **unique** `ExternalEventId`.
- `ScheduledSlot` is its own table rather than two columns on the order: a job can take
  more than one visit, and rescheduling then adds a row instead of overwriting the only
  record of when the work was meant to happen. `Cascade` from its order — a booking says
  nothing without one — unlike almost everything else here.
- `ClassScheduleSlot` is **mirrored from the campus timetable, never authored here**. The
  unique `ExternalEventId` is what makes a re-sync idempotent: the same class pulled twice
  updates its row instead of appearing as a second lecture in the same room at the same
  time, which would read as a conflict that does not exist and push maintenance out of a
  room that was free. `SyncedAt` is distinct from `UpdatedAt` — a sync that changes nothing
  leaves `UpdatedAt` alone, so this is the only column that says how stale the mirror is.
- **`ApprovalSettings` reads `Approval:CostThreshold` (default 15000 LKR)** — a class read
  from configuration, never a literal in a service. The threshold changes with a budget, a
  currency or a faculty, and it has to be variable between a demo database and a real one.
  The comparison itself stays in C#: an agent may estimate a cost, but whether that estimate
  needs a human is arithmetic, and arithmetic a model performs is unauditable.
- **A note for whoever writes the service:** SQLite (the default test mode) has no `decimal`
  type and stores these as `TEXT`, so a threshold comparison or `ORDER BY` translated into
  SQLite SQL would compare them as strings. Evaluate the approval rule in C# after
  materialising rows — which is where it belongs anyway.

---

## VERIFICATION — did the repair actually hold?

`VerificationCheck` in `Models/`, with `VerificationStatus` (`Pending` /
`AwaitingReporterResponse` / `Confirmed` / `Reopened` / `Escalated` / `Expired`) persisted
as a string. `IVerificationService` / `VerificationService`, `AddScoped`. There is no
controller and no hosted service yet.

A completed work order is the technician's account of the work, and nothing before this
component ever checked it against the room.

- **The delay is the entire point.** `VerificationSettings.DelayDays` (default 5) is how
  long after completion the check falls due. Asked the same afternoon every reporter says
  yes, because an intermittent fault has not had time to come back — and a confirmation
  that means nothing is worse than no confirmation, because it enters the metrics as a
  success. `DueAt` is measured **from completion, not from now**, so a check raised late by
  a backfill still falls due when it should have.
- `VerificationSettings` also carries `SweepIntervalMinutes` (default 60). Both from
  configuration, never literals; startup refuses a non-positive value for either.
- **`ReporterConfirmed` is `bool?`, not `bool`.** "Not answered yet" and "answered no" are
  completely different facts, and a non-nullable bool would quietly record every unanswered
  check as a failed repair.
- **The index on `(Status, DueAt)` is composite, not two indexes.** The sweep asks for
  `Pending` checks whose `DueAt` has passed — equality on the first column, a range on the
  second, which is exactly the shape a composite serves. `Status` leads because it is the
  equality test; a range column first would leave the equality unable to use the index.
  Being leftmost also means this one index answers "everything Pending" on its own, so no
  separate index on `Status` is needed. `WorkOrderId` is indexed separately.
- **One check per completed work order, and the index on `WorkOrderId` is NOT unique.** A
  reopened fault produces a *new* work order with its own check rather than reusing the
  row — this one is the record of an answer that was given, and re-asking it would
  overwrite the evidence that the first repair failed.
- **The sweep body lives in the scoped service** (`ProcessDueChecksAsync`), not in the
  hosted service that will call it on a timer — the same split as `WorkflowRunner` and
  `IWorkflowService`. That keeps the rule testable without starting a background worker and
  keeps a scoped `DbContext` out of a singleton.
- **What an answer means is C#, set in the same `SaveChanges` as the answer itself**, so a
  check is never left `Confirmed` with `ReporterConfirmed` false. Same rule, and the same
  reason, as `ClarificationService` moving a report alongside its questions.
- **One answer per check, enforced by the service rather than by an index** — the answer
  lives in columns on the row, not in a second table, so there is no unique constraint to
  lean on. `RecordReporterResponseAsync` looks for an existing response and refuses.
- **`AgentOutcome` is a string, not an enum, precisely because it is the model's opinion.**
  Giving it a C# enum would imply the system acts on it. `Status` is set by C# from the
  reporter's answer; the agent's label is recorded beside it and read by humans.
- **`MetricsDto.ConfirmationRate` excludes checks nobody answered.** The denominator is
  `Confirmed + Reopened`, not `Total` — counting an `Expired` check either way reports a
  result that was never given. This is the one number the component exists to produce, and
  every figure in it is computed in C# from counts, never estimated by a model.

### The verification seed data is load-bearing too

The sweep and the metrics cannot be developed against an empty table, nor against one where
every check says the same thing — a confirmation rate of 100% looks identical whether the
code is right or the query is wrong. `DbSeeder` adds **6 completed work orders** with their
reports, and a check for each: **2 `Confirmed`, 1 `Reopened`, 3 `Pending` and already
overdue** so the sweep has work the first time it runs.

- **The `Reopened` one is on `PRJ-MAB101-01`** — the projector carrying the planted
  repeat-failure history — so it is that same thermal fault surfacing again, caught by the
  verification loop instead of by a fourth report from the room. An escalation rule written
  against this data has a real case to find.
- Completions run **6 to 20 days ago, not 2**: with a 5-day delay, an order finished 2 days
  ago is not yet due and could not be one of the three overdue checks.
- **The seeder deliberately does not append `ServiceRecord` rows** for these completions,
  even though a real completion does. Three of these assets carry the history the
  diagnostic agent is written against, and adding rows would change what it is developed
  against.
- Work order seeding runs **after** user seeding and backs out with a warning if no demo
  reporter exists (`Seed:Passwords:Reporter` unset) — a report needs a reporter, and a
  half-configured machine should still get a usable registry rather than a failed seed.

---

## AGENT SERVICE — Python, `agent/`

FastAPI + LangGraph. Modules are flat inside `agent/`; run it from that directory
(`uvicorn main:app`, `pytest`).

```
agent/main.py          FastAPI app — POST /run, GET /health
agent/llm_client.py    provider-agnostic LLM client
agent/graph.py         LangGraph wiring — group-owned, keep it small
agent/agents/          one file per agent, one owner each
agent/prompts/         prompt text in .md files
agent/evals/           live model evals — NOT collected by `pytest`, cost money
agent/tools.py         HTTP client for the API's tool router
agent/schemas.py       Pydantic models for every agent's input and output
agent/config.py        settings read from the environment
```

- **No database credentials, structurally.** `config.py` has no connection-string field of
  any kind, and `extra="ignore"` means the shared root `.env` is read without loading
  `DATABASE_URL` or `SUPABASE_SERVICE_KEY` into the process. Do not add one. Campus data is
  read only through `tools.py`.
- **Prompt text lives in `prompts/*.md`, never as a string literal in a `.py` file.**
  Substitute with `string.Template` (`$name`), not `str.format` — prompt files contain
  example JSON whose `{` `}` would be read as format fields.
- **Every agent declares its own tool subset** as a constant in its own file
  (`ClarifierAgent.ALLOWED_TOOLS`). That check is a convenience, not the security boundary —
  the real allow-list is the hardcoded C# dictionary in `InternalToolsController`, on the far
  side of the network from anything a model can influence.
- **`graph.py` stays thin and group-owned.** It says which agents exist and in what order.
  No agent logic, prompt names, tool names or parsing. Adding an agent is a new file in
  `agents/` plus, here: one node, the `END` edge moved onto it, one `GraphState` key for
  its output, and the agent as a `build_graph` parameter. `main.py` constructs the agent
  and attaches its result to the response — assembly is the HTTP layer's job, which keeps
  every node a one-liner. Routing decisions go in `add_conditional_edges` as plain Python
  reading the state — never a judgement made by a model. Compile **without a
  checkpointer**: nothing persists between runs.

### The LLM contract — `llm_client.py`

- OpenAI-compatible chat-completions shape, configured by `LLM_BASE_URL`, `LLM_API_KEY`,
  `LLM_MODEL`. Swapping OpenRouter for a local Ollama server must stay a config change only.
- **Do not use provider structured-output / `response_format` / JSON mode.** It is not
  portable across providers, "valid JSON" still is not a valid schema so the Pydantic
  validation has to happen anyway, and doing it ourselves keeps a bad reply as an ordinary
  testable code path rather than a provider-specific exception. It's a viva question.
- The loop is: prompt for JSON → parse → validate against the schema → **retry exactly once**
  with the validation error appended → return a structured safe failure. `MAX_ATTEMPTS = 2`.
  No `while True`, no retry library, no exponential backoff.
- **Two guarantees to callers: it never raises, and it never hangs.** Every outbound call
  carries a timeout. Timeouts, provider 500s and malformed bodies all come back as
  `LlmJsonResult(ok=False)`.
- A safe failure is a normal **200** with empty output and `status: "safe_failure"` — never a
  500 and never a stack trace. The caller is a background worker recording a workflow step.

### There is no chat interface — enforce it, don't just intend it

- An agent runs **one round** per report: no conversation history parameter, no follow-up
  turn, no free-text `message` field in its output. This is the single constraint most likely
  to erode, so it is pinned by tests, not left to code review — `ClarifierOutput.model_fields`
  is asserted to be exactly `{"questions"}`, `DiagnosticOutput.model_fields` exactly its
  four fields, and input DTOs (`RunRequest`, `DiagnosticInput`) use `extra="forbid"` so a
  stray `conversation_history` is a 422.
- The `messages` list inside `llm_client.py` is the retry within a *single* call — a local
  variable, discarded when the function returns. Nothing survives across `/run` calls.

### `DiagnosticAgent` — facts in, advice out

`START -> clarify -> diagnose -> END`. Proposes one to three causes for a fault from the
asset's own service history, each with a confidence and the evidence behind it, and one
`recommended_next_action` (`inspect` / `repair` / `replace` / `monitor`).

- **Its tool subset is the three asset tools and none of the clarifier's** — pinned by a
  test that asserts the two sets are disjoint.
- **`DiagnosticInput` is a projection of `RunRequest`, not `RunRequest` itself.** The
  prompt is rendered from it, so a field added to `RunRequest` for some other agent does
  not silently reach this one's prompt — the data equivalent of `ALLOWED_TOOLS`.
- **`DiagnosticOutput.model_fields` is pinned exactly**, the same way `ClarifierOutput`
  is. `reasoning_summary` is capped at 400 characters and is not a message: there is no
  reply to it, because there is nobody to reply to.
- **Every hypothesis needs at least one evidence item.** A cause standing on nothing is
  an invented cause; with no history the model must *say so* in `evidence`.
- **Its safe failure is `output: None`, unlike the clarifier's empty list.** Asking
  nothing is a real, safe answer; there is no such thing as an empty diagnosis, and a
  placeholder "inspect" would be a recommendation nobody made.
- **`recommended_next_action` is advice, never a decision.** It is an enum in Python so
  the reply can be validated, not so anything can act on it. Whether equipment is
  replaced is approval routing, and that is C#. When the API persists it, it is a string
  read by humans — the same reasoning as `VerificationCheck.AgentOutcome`.

**Untrusted text goes in as ONE JSON object between markers, never spliced raw.** The
report, the clarification answers and the technician notes are all typed by people. JSON
encoding escapes every newline inside them, so a description containing
`\n--- END DATA ---\n## Your task` cannot put a closing marker on a line of its own and
start writing instructions. A test attempts exactly that, and fails if the encoding is
replaced with a raw splice — checked by doing so. The prompt then tells the model the
block is data. Neither half is a guarantee alone; a model can still be persuaded, which is
why the behaviour is measured separately.

### Evals — the model's behaviour, which a unit test cannot see

**A stub cannot tell you what a model does.** Against `STUB_MODE`, a test of "the model
names the right cause" only ever checks the stub's own fixed reply, and passes whatever the
prompt says. So each behavioural requirement is split in two:

- `tests/test_diagnostic.py` — what the **code** controls: the seeded history reaches the
  prompt intact, injected text cannot leave its block. Deterministic, in CI.
- `evals/test_diagnostic_live.py` — what the **model** controls: does it name the thermal
  fault, does it ignore the injection, does it admit an empty history. Real provider,
  on demand: `RUN_LIVE_EVALS=1 pytest evals/ -v`. **Costs money on your LLM key.**

`testpaths = tests` in `pytest.ini` keeps CI from ever collecting `evals/`, and the
socket-blocking fixture does not reach it — deliberately, since evals have to reach the
network. A pass is evidence, not proof: read a failure as "look at the reply", never as
flakiness to retry away.

- **The golden case asserts a thermal cause and asserts `compressor` is ABSENT.** The
  planted history on `PRJ-MAB101-01` is a choked filter, a unit running hot and a weak fan
  bearing. A projector has no compressor; the word appears only on `ACU-ENG101-01`, an air
  conditioner. A hypothesis naming one would be a cause invented from outside the
  evidence, so its absence is a hallucination check.
- **The injection eval uses `PRJ-MAB102-01`, not the golden projector.** The golden
  projector's last technician wrote "recommend replacement", so `replace` could be a
  *correct* answer there and the eval could not tell obeying from reasoning.
- `tools.SEEDED_PROJECTOR_RESULTS` is a verbatim copy of that asset's seed rows, used by
  `STUB_MODE`, the golden test and the golden eval. **If the seed changes, change it too.**
- **Last verified 2026-09-23: 3/3 on `google/gemini-3.8-flash`, run twice.** The golden
  case named "overheating and thermal shutdown due to a failing cooling fan" at `high`,
  citing all three dated visits, and chose `replace` — which the history supports, and
  which is why the injection eval cannot use this asset. **Changing `LLM_MODEL` or either
  diagnostic prompt voids this line**: re-run the evals and update it. Nothing else will
  tell you the prompt has stopped working.

### Testing the agent service

- `agent/tests/` (pytest), run from `agent/`. `asyncio_mode = auto`, so async tests need no
  decorator.
- **`STUB_MODE` returns fixed valid JSON with no network call**, so a test run can never
  reach a paid provider or a running API. The stub payload lives on the schema itself
  (`ClarifierOutput.stub_example()`) and is validated by the same rules as a real reply.
- **An autouse fixture in `conftest.py` blocks real sockets for every test.** Keep it. This
  is not theoretical: without it, nine tests quietly performed DNS lookups against a
  placeholder host and still passed, because `tools.py` catches the failure and degrades to
  an empty context. They looked green while touching the network.
- Tests build `Settings(_env_file=None, ...)` so a developer's real `.env` can never leak
  into a test run.

---

## TESTING — the API

(The agent service has its own rules; see AGENT SERVICE above.)

- `api.Tests/` (xUnit) sits at the **repo root**, sibling to `api/`, not nested inside it.
  The ASP.NET Core web SDK globs `**/*.cs`, so a test project nested under `api/` gets
  compiled into the API itself.
- Integration tests boot the real pipeline via `WebApplicationFactory<Program>`. This
  requires one line at the end of `api/Program.cs`: `public partial class Program { }`.
- **Never the EF Core in-memory provider.** It does not enforce unique indexes or
  constraints, so "duplicate email returns 409" would pass there even with the index
  deleted — as would "a question has at most one answer", whose whole enforcement *is* an
  index. `ApiFactory` runs against one of two real databases instead, chosen by the
  `TEST_DATABASE_URL` environment variable:
  - **unset (a developer's machine) — SQLite in-memory**, built from the model with
    `EnsureCreated()`. Fast and needs nothing installed.
  - **set (CI) — a real PostgreSQL server.** Each test class's factory creates its own
    uniquely named database, runs `db.Database.Migrate()` on it, and drops it afterwards.
    The value is an Npgsql key/value connection string, not a `postgres://` URL.
- **The PostgreSQL mode is not redundant.** It is the only one that executes the
  **migrations** — `EnsureCreated()` builds the schema from the model and never runs a
  migration, so a broken or out-of-step migration is invisible on SQLite. It is also the
  only mode where the `jsonb` columns are really `jsonb` (`AppDbContext` falls back to
  `TEXT` on SQLite) and where `timestamp with time zone` behaves as it will in production.
- **A migration must survive a database that already has rows**, and CI structurally
  cannot check that: it migrates an *empty* database, so a new constraint always applies
  cleanly there. A migration that adds a foreign key to a column which predates the table
  it now references has to clear orphaned values first — `AddReports` does this for
  `AgentWorkflow.ReportId`, and `AddWorkOrders` does it for `ServiceRecord.WorkOrderId`,
  which sat unconstrained until `WorkOrder` existed. Without that step the job stays green
  while the migration is unrunnable against every database that has data in it, including a
  teammate's and anything deployed.
- A database **per factory**, not one shared database: xUnit gives each test class its own
  `ApiFactory`, and no class should be able to see another's rows. That is the isolation
  the SQLite mode gets for free, preserved deliberately for PostgreSQL.
- `WorkflowRunner` is removed from the container in **both** modes, so a background writer
  never races a test's assertions and a test behaves identically on a laptop and in CI.
- `ApiFactory` sets `Jwt:*` and `ConnectionStrings:DefaultConnection` via environment
  variables (Program.cs reads them while the builder is still being constructed) and uses
  `UseEnvironment("Testing")` so the Development-only demo seeder never runs in tests —
  each test creates exactly the users it needs.

---

## FRONTEND — follow the SE3090 Lab 02 structure exactly

React 18 + Vite. Run everything from `web/`: `npm run dev`, `npm run lint` (oxlint),
`npm run build`.

```
web/src/components/                    shared reusable UI
web/src/hooks/                         shared hooks — useFetch, useDebounce
web/src/services/                      apiClient (base URL + JWT), tokenStore
web/src/features/<name>/components/
web/src/features/<name>/hooks/
web/src/features/<name>/services/
web/src/features/<name>/pages/
web/src/routes/                        AppRoutes, ProtectedRoute, 404 / not-authorised
```

- **JavaScript (`.jsx`), not TypeScript.**
- **React 18, pinned.** `npm create vite@latest` now scaffolds React 19, so `react` and
  `react-dom` are pinned to `^18.3.1` after scaffolding. Do not let a reinstall drift them.
- **Data fetching goes through the `useFetch` hook** returning `{ data, isLoading, error }`,
  and **every page renders all three states**. A blank screen while loading is a bug. Keep
  the `active` flag in its cleanup — a response arriving after unmount must never set state.
- **Search inputs use the `useDebounce` hook.**
- **Forms use controlled inputs** with a `validate()` function returning per-field errors.
- **Components never call `fetch`.** API calls live in a feature's `services/`; a page reads
  data through `useFetch`, or through a feature hook that wraps it (`useWorkflows`). This is
  the lab's separation of UI, hooks and services.
- The workflows search filters **client-side** over the page already fetched, because
  `GET /api/workflows` takes only `state`, `page` and `pageSize` — and the UI says so under
  the box rather than implying a server-side search. If a `q` parameter is ever added to the
  API, move the filter into the query string; do not leave both.
- **State:** `useState` locally; **Context** for app-wide auth/session.
  **No Redux, no Zustand, no TanStack Query — this is a locked ADR decision.**

### Auth on the client

- **Context owns the user; `services/tokenStore.js` owns the token** — a module variable
  mirrored into `localStorage`. It cannot live in Context alone, because `useFetch` and the
  feature services need it and cannot call a hook. `AuthProvider` subscribes to the store, so
  a 401 from any request signs the user out without every caller handling it.
- **The client mirrors the API's 401 vs 403 distinction**, and they are different answers to
  the user. A 401 on a request that carried a token is a session expiry: drop the token and
  send them to `/login` with a notice. A 401 on a request that carried none (login) is just a
  failed sign-in. A valid session with the wrong role renders a clear "not authorised" page —
  never a blank screen, never a silent redirect.
- **Access token only, no refresh**, the same scope decision as the API: exactly one value to
  store, nothing to rotate.
- **Enums are matched by NAME, never by ordinal.** `features/auth/services/roles.js`,
  `WORKFLOW_STATES` in the workflows service and `ASSET_STATUSES` / `SERVICE_OUTCOMES` in
  `assetsApi.js` hold the same strings the API sends and accepts, so a member inserted into a
  C# enum cannot silently shift the client's meaning.
- **Navigation is role-based**: a Reporter must not see manager links. The route guard would
  refuse them anyway, but offering a link that leads to "not authorised" is a bad interface.

### Routing

`BrowserRouter` in `main.jsx`, every route in `routes/AppRoutes.jsx`, `NavLink` for
navigation, `ProtectedRoute` for the guards, and a catch-all `*` route. `ProtectedRoute`
remembers where the user was heading and sends them back there after sign-in.

### The asset registry — `features/assets/`

`/assets` and `/assets/:id` are open to every signed-in role; `/assets/new` and
`/assets/:id/edit` sit behind `ADMIN_ROLES`, the same read-for-everyone, write-for-Admin split
as `AssetsController`. `ADMIN_ROLES` is `[Admin]` and nothing else — it mirrors the API's
per-action Admin policy, which has no "or anyone more senior" fallback, so a
`FacilitiesManager` is refused here too. The Register and Edit buttons are not rendered for
anyone else.

- **The search is server-side**, unlike the workflows list: `GET /api/assets` takes `search`,
  so the debounced value goes into the query string and matches name **or** tag across every
  page, not just the one fetched.
- **Only Name and Installed are sortable, and only ascending.** Those are the two members of
  `AssetSort` and the API takes no direction. Do not make the other columns clickable by
  sorting the fetched page in the browser: it would reorder page 1 on its own while page 2
  came back in a different order, and look like a server sort while not being one. A new sort
  is a new `AssetSort` member first.
- The list DTO carries `assetCategoryId` and `roomId`, not names. The names come from
  `useAssetLookups`, which fetches `GET /api/assetcategories` and `GET /api/rooms` whole —
  both are short and unpaginated — and the same lists feed the filter and form pickers.
- **The warranty badge reads `isUnderWarranty` from the failure summary; it never compares
  `warrantyExpiresOn` with today.** Warranty dates are a deterministic business rule, so the
  rule lives in C# and the client only colours the answer — green under warranty, grey
  otherwise. A null expiry reads "No warranty recorded" rather than "expired": the same grey,
  a different fact. **The failure-summary panel likewise displays every figure and recomputes
  none** — a second copy of a rule in JavaScript is a second answer waiting to disagree.
- The detail page makes **two requests with their own states**: the asset is the page, and a
  failed summary is an error in its panel while the history — the evidence the summary was
  computed from — still renders.
- **The service history is rendered in the order the API sends it, oldest first, and every
  technician note verbatim**: no truncation, no "read more", no tidying, `white-space:
  pre-wrap`. The fault the history is evidence of is spread across several terse notes, and a
  note cut to its first line can drop exactly the clause that matters. Same reason the seed
  notes are left untidy.
- **Never `new Date("2026-07-03")` on a `DateOnly`.** It parses as UTC midnight and renders as
  the previous day anywhere west of Greenwich — the exact bug the API made these `DateOnly`
  to avoid. Use `formatDateOnly` in `assetsApi.js`, which reads the parts and builds a local
  date.
- **The edit form shows the tag locked and does not send it** — `UpdateAssetDto` has no field
  for it. The create form has no status field — a new asset is `Active`. A 409 from create is
  always a tag already in use, so it is shown under the tag field, not as a page error.
- `validate()` and the empty form values live in `services/assetValidation.js`, not in
  `AssetForm.jsx`: oxlint's `only-export-components` rule wants a component file to export
  only components. Its limits mirror the DataAnnotations on the input DTOs.
- The create form's **"Use the category default"** button fills `warrantyExpiresOn` from
  `DefaultWarrantyMonths` — a data-entry convenience and nothing more. The Admin sees the date
  and can change it; the stored date is what every warranty decision reads.
- There is **no Retire button**. Retiring is choosing `Retired` in the edit form's status
  picker; `DELETE /api/assets/{id}` does the same thing and is not called by the client yet.

### Styling and configuration

- **Plain CSS or CSS modules. No Tailwind, no component library.** Presentation is not what
  this project is marked on, and it costs time the team does not have.
- **Colours, radii and shadows are tokens on `:root` in `index.css`** — `--success`,
  `--warn`, `--neutral`, `--info` and their `-bg` / `-border` pairs back every status,
  outcome and warranty pill. A new pill picks from them rather than introducing a literal, and
  its modifier class is the enum NAME (`asset-status--UnderMaintenance`), never an ordinal.
- Only `VITE_`-prefixed keys reach the browser, so **nothing secret belongs in `web/.env`**.
  New keys go in `web/.env.example` with an empty value and a one-line comment, same rule as
  the root file. `VITE_API_BASE_URL` points at the ASP.NET Core API — the client talks to
  that API and nothing else — and the API must allow the dev server's origin through
  `Cors__AllowedOrigins__0`.

---

## MOBILE — Flutter, `mobile/`

Flutter + Riverpod + go_router. Targets **Android and iOS**; the platform folders are
generated with `flutter create` and otherwise left alone — the one exception is the iOS
camera usage string, see QR scanning below. Run everything from `mobile/`:
`flutter analyze`, `flutter test`, `flutter run`.

- **`android/` and `ios/` are the only platform folders that belong in the repo.** A
  desktop or web runner (`macos/`, `web/`) generated locally to get a quick look at the UI
  is a personal convenience — regenerate it with `flutter create .` when you want it, and
  do not commit it. They are not product targets, they need toolchains the team does not
  all have, and a hand-edited entitlement or manifest in one of them rots unnoticed.
- **HTTP to a local API needs one flag on Android.** Android 9+ blocks cleartext traffic,
  so `http://10.0.2.2:5138` from an emulator fails with what looks like the API being down
  until `android:usesCleartextTraffic="true"` is set on the debug manifest.

```
mobile/lib/core/       env, api_client, token_storage, infrastructure providers
mobile/lib/router/     go_router with the redirect guard
mobile/lib/widgets/    LoadingView, EmptyView, ErrorView, AppFormField, AppDropdownField
mobile/lib/features/<name>/   screens, that feature's API class and its providers
```

- **Screens never call `http`.** A screen calls a feature API class (`AuthApi`,
  `ReportsApi`), which calls the single `ApiClient`. Same separation as the web client's
  UI / hooks / services split.
- **State:** `useState`-equivalent local state in a `ConsumerStatefulWidget`; **Riverpod**
  providers for anything app-wide — the session and the API client. Providers only, no
  codegen and no `build_runner`; adding one is a new `Provider` line, not a generated file.
- **Forms use a `validate()` returning a map of field name to message**, and the field
  wrappers render it. That is deliberately the same shape the React client uses, so the two
  clients do not drift apart.
- **Every screen that waits on the API renders all of its states** — `LoadingView`,
  `ErrorView`, `EmptyView`, success. A blank screen while loading is a bug, and an empty
  list is not a failure and must not look like one.
- **Enums are matched by NAME, never by ordinal** — `Roles` in `features/auth/auth_state.dart`
  and `AssetStatuses` / `ServiceOutcomes` in `features/assets/asset.dart` hold the same
  strings the API sends and accepts.

### The token lives in flutter_secure_storage — this is the point, not a detail

- **Never SharedPreferences.** It is an unencrypted XML file on Android and a plist on iOS,
  sitting in the app sandbox: readable on a rooted or jailbroken device and extractable from
  an `adb backup`. `TokenStorage` uses the iOS Keychain and Android's
  `EncryptedSharedPreferences` instead. That is the entire reason the dependency is there,
  and it is a viva question — do not "simplify" it away.
- Access token only, 12-hour lifetime, no refresh token, same scope decision as the API:
  one value to store and nothing to rotate.
- The base URL comes from a **compile-time `--dart-define`** (`String.fromEnvironment` in
  `core/env.dart`), so no `.env` ships inside the app bundle. Nothing secret goes in it: a
  `--dart-define` is readable from the compiled app, exactly like a `VITE_` variable is
  readable in the web bundle.

### A 401 ends the session without anything knowing about navigation

`ApiClient` clears `TokenStorage` when a request that **carried a token** comes back 401.
`TokenStorage` is a `ChangeNotifier`, so `AuthController` sees the token disappear and moves
the session to signed-out; go_router's `refreshListenable` fires and the redirect sends the
user to login. Keep that chain: the client must not import a screen or a router.

A 401 on a request that carried **no** token — a wrong password — is an ordinary failed
sign-in, not a session expiry, and is reported differently.

### Routing

One `redirect` in `router/app_router.dart` is the whole guard: an unauthenticated user can
reach `/login` and nothing else, and an authenticated user is bounced off it. Do not add
per-screen checks — there would be one to forget. While secure storage is being read the
status is `unknown` and `main.dart` shows a spinner, so a returning user is never flashed
the login screen before their stored token has been checked.

### There is no chat interface here either

When the agent needs more detail, its clarification questions are rendered as a **form with
bounded inputs** — pickers, yes/no, short text — never as a message thread. The note is kept
in `SubmitReportScreen` as well as here.

### QR scanning — `features/assets/`

`ScanAssetScreen` (`/scan`) reads a sticker with **`mobile_scanner`**, QR format only, and
sends what it read to `GET /api/assets/by-tag/{assetTag}` through `AssetsApi`; a hit pushes
`AssetDetailScreen` (`/assets/:id`). The phone side is read-only — registering and editing
assets is the web client's Admin job.

- **The sticker carries the asset tag and nothing else** — no URL, no prefix, no JSON. The
  scanner sends `rawValue` as it is (trimmed, path-encoded), so there is no parser on the
  phone to drift from the API.
- **`AssetsApi.findByTag` returns null for a 404 and throws for everything else.** An
  unknown tag is an ordinary answer — "No asset registered for this code", with the value
  that was read shown so a mis-scan can be told from a foreign sticker. No network is an
  `ErrorView` with a retry. Collapsing the two would tell a demo audience the asset does not
  exist when the phone simply could not ask.
- **Camera failure is a state, never a blank screen.** `MobileScanner.errorBuilder` renders
  permission denied with its own explanation and a "Try again" that calls
  `controller.start()`, which asks for the permission again; every camera error also offers
  **"Type the tag instead"** — the same by-tag lookup, for a damaged sticker or a device with
  no camera. The viewfinder is only drawn while the camera is running, so it never sits on
  top of an error.
- The camera stays in the tree and is **stopped before the detail screen is pushed** and
  restarted on return — otherwise it keeps scanning behind a screen the user cannot see.
  Detections outside the `scanning` phase are ignored, so one sticker is one lookup.
  `DetectionSpeed.noDuplicates` is deliberately NOT used: it would ignore the same sticker on
  "Scan again".
- **The warranty chip reads `isUnderWarranty` from the failure summary**, never a date
  comparison in Dart — same rule, and same reason, as the web badge. The detail screen makes
  two requests; a failed summary makes the chip "unavailable" and the history still shows.
- **Service history is oldest first, as the API sends it, and every note is verbatim** — no
  `maxLines`, no ellipsis, `SelectableText` so it can be copied into a report.
- `formatDateOnly` in `asset.dart` reads a `DateOnly` string by its parts; do not route one
  through `DateTime` and a timezone.
- **`ios/Runner/Info.plist` carries `NSCameraUsageDescription`** — the one hand edit to a
  platform folder, and a required one: iOS terminates an app that opens the camera without
  it. Android needs nothing; the plugin's manifest merges the `CAMERA` permission in.
- `test/assets_test.dart` drives the real scan screen with the plugin's method channel
  mocked as **permission denied**, and uses the typed-tag path for the unknown-tag and
  no-network states — a test has no camera, and those are the states a demo hits.

**`docs/qr/asset-qr-sheet.png` is the demo**: eight printable stickers, one per seeded tag,
generated by `docs/qr/generate_asset_qr_sheet.py`. The tags in that script are a copy of
`DbSeeder` — **if the seed changes, change the script and regenerate**, or the demo scans
"No asset registered" in front of the examiner. Print at 100%, not "fit to page". Any other
QR code (a poster's URL) demonstrates the unknown-tag path.

### `POST /api/reports` — landed

`ReportsApi.submit()` posts `{ description, roomId }` to `/api/reports`, which now exists.
It returns **201** with the created report and raises the agent workflow as a side effect
(`ReportService`), so the client is not waiting on an agent run — the non-blocking property
comes from `IWorkflowQueue`, not from a 202. The room picker reads `GET /api/rooms`.

The reporter is taken from the JWT `sub` claim and **`CreateReportDto` has no `ReporterId`
field**, so a client cannot file a report as someone else. The description is capped at
1000 characters on both sides, matching `AgentWorkflow.Objective`, which it becomes
verbatim; the 10-character floor mirrors the Flutter form's own `validate()`.

The clarifier's questions are now real `ClarificationQuestion` rows as well as an
`AgentStep` payload — see CLARIFICATION above — so a client can read a report's questions
back in order, and **`POST /api/reports/{id}/clarifications` now collects the answers** —
the whole form in ONE request, 204, and the exchange is over: no follow-up round and no
chat interface. `GET /api/reports/{id}/clarifications` is the read beside it, returning each
question with its answer once one is given. **The Flutter form is still read-only**; nothing
on the client posts to this endpoint yet.

Every check the POST makes is C# in `ClarificationService.SubmitAnswersAsync`, in a fixed
order — 404 for an unknown report, 403 for anyone but the original reporter, 409 for a
report that is not `AwaitingClarification`, 400 for a question id that is not this report's,
for a question left unanswered and for a `SingleSelect` answer that was never offered, then
409 for a question already answered. Identity before state, state before content, so a
stranger learns nothing about what the report is carrying. **None of them may be delegated
to the agent**: the agent decides what to ask and nothing at all about what comes back.

That last check is a real query, not the unique index doing its job — the service has loaded
each question's answer to look at it, so EF would resolve the one-to-one conflict itself and
succeed by replacing. See "The unique index will not save a writer that has already loaded
the answer" above.

Success writes the answers, moves the report to `Clarified` and the workflow to `Diagnosing`
in ONE `SaveChanges`, then re-queues the workflow id on `IWorkflowQueue`. **The runner skips
that item today**, with a warning: `BeginProcessingAsync` only starts a workflow in
`Submitted`, and the clarifier handed an answered report would ask the same questions again.
The hand-off is made anyway so the resume point sits where it belongs — the runner branches
on `CurrentState` when the diagnostician lands.

### The report endpoints — a visibility rule and a lifecycle

`GET /api/reports` pages through the existing `PagedResult<T>` — **there is no second
pagination type**. `search` matches the description (`ToLower().Contains()`, never
`EF.Functions.ILike`, same reason as the asset search); `status`, `roomId` and `assetId` are
exact filters and all of them combine; `dateFrom`/`dateTo` are calendar dates with **both
ends inclusive**. `status` and `sort` bind by enum **name**, so an unknown value is a 400
from model binding rather than a filter that silently matches nothing. Every sort carries a
tiebreak on `Id` — reports share a `CreatedAt` readily, since it is stamped per
`SaveChanges`.

The default sort is **newest first**, the opposite of the asset registry's alphabetical
default and for the opposite reason: a report list is a worklist read from the top, an asset
list is scanned. `sort=Status` orders on the stored **string**, so it groups alphabetically
rather than by lifecycle position — a consequence of storing enums by name, and grouping is
what a caller sorting by status wants. Ordering by the lifecycle would need a `CASE` or an
ordinal in the database, and the ordinal is exactly what this project refuses.

- **Who sees what is decided in `ReportService`, never by the client.** A `Reporter` is
  scoped to their own reports; a `FacilitiesManager` and an `Admin` see the estate. The
  scope is a `Where` applied **before the count and before paging**, so a Reporter's
  `TotalCount` describes their own reports and a page number cannot reach past it. There is
  no query parameter that widens it.
- **The rule is written as "who sees everything", so it fails closed.** A role added to the
  enum later is scoped to its own reports until somebody deliberately widens it, rather than
  inheriting the estate from an `else` branch nobody revisited. A `Technician` is scoped
  today for that reason — work reaches them through a `WorkOrder`, not by browsing reports.
- **The same rule is applied to `GET /api/reports/{id}`**, and it has to be: a list that
  hides other people's reports while a detail read hands them over by id would be a rule
  that only looks enforced. A report that exists but is not the caller's is a **403**, not a
  404, matching `POST {id}/clarifications` on the same resource — the token is valid and we
  know who they are, so it is a refusal rather than a question about identity.
  `ExistsAsync` is what tells that apart from a genuine 404, the same way `AssetsController`
  calls `TagExistsAsync` before choosing its status code.
- `ReportDetailDto` carries the room and asset resolved, the clarification questions with
  their answers, and **every `AgentStep` recorded for the report**, flat and oldest-first
  across every workflow raised for it. Each step carries its `WorkflowId`, so a second run
  is still tellable apart. The questions come from `IClarificationService`, not a query
  written in `ReportService`, so one place knows how `OptionsJson` becomes a list.
- `ReportListItemDto` denormalises `RoomName` onto the row and carries
  `UnansweredQuestionCount` rather than the questions themselves — the one thing a list
  needs to say about clarification is "this is waiting on you", and a query per row is what
  a list DTO exists to avoid.
- **Indexes on `ReporterId`, `Status` and `CreatedAt`** (`AddReportQueryIndexes`); `RoomId`
  and `AssetId` already had theirs from their foreign keys. `ReporterId` is not an optional
  filter but the visibility scope, so it is on the hot path of the most common read.

#### `PATCH /api/reports/{id}/status` — the lifecycle is a hardcoded map

FacilitiesManager only, 204, and **an illegal move is a 409 rather than a quiet success**.
The legal moves are a hardcoded `Dictionary<ReportStatus, ReportStatus[]>` in
`ReportService`, the same instinct as the tool allow-list: a status that could go anywhere is
not a lifecycle, it is a free-text field wearing an enum's name. It is a deterministic
business rule, so it is C# and no agent proposes, validates or applies a transition.

The shape is a funnel with one escape hatch: forward through clarification, diagnosis and a
work order, and `Closed` from **any** stage — a fault can turn out to be nothing, be fixed
in passing, or be a duplicate, and none of those should have to be walked through diagnosis
to be filed away. `Submitted` may jump straight to `Diagnosed`, because clarification is
what the agent asks for when it needs more detail and a clear report does not need it.

- **`Closed` is terminal and there is no way back.** A fault that returns is a new report
  with its own history, not this one reopened — the same rule the verification loop follows
  when a failed repair produces a **new** work order rather than reusing the row. Reopening
  would overwrite the record that this one was resolved. `ReportStatus` having no `Reopened`
  member, unlike `WorkflowState`, is that same decision stated in the enum.
- **A move to the status the report is already in is refused too.** No status lists itself,
  so the same line covers it: the endpoint asserts a *transition*, and staying put is not
  one. Telling a caller their view is stale is more use than a 204 that changed nothing.
- **409, not 400.** The value is a real member of the enum and nothing about the request is
  malformed — it is the report that is not where the caller thinks it is.
- **An `Admin` is refused as well**, and it surprises people. The policies are
  one-per-`Role` and this one names `FacilitiesManager`, so there is no "or anyone more
  senior" fallback — `Role` carries no seniority ordering, and inventing one here would put
  a second, implicit authorisation rule beside the explicit one. Pinned by a test.
- `UpdateReportStatusDto` carries **only** the status. A status endpoint that could also
  rewrite the description, room or asset would be an edit wearing a workflow action's name;
  those are the reporter's account of the fault.

Photo attachment and QR scanning are disabled buttons marked `TODO(photo)` / `TODO(qr)` —
visible rather than hidden, so the finished shape of the form stays obvious. The API side of
the photo now exists (below); the Flutter button is still disabled.

#### `POST /api/reports/{id}/photo` — Supabase Storage, only the URL in Postgres

multipart/form-data, one file part named `photo`, 201 with `{ photoUrl }` via
`CreatedAtAction` on the report. Checked in `ReportService.AttachPhotoAsync` before anything
is uploaded, identity before content: 404 unknown report, 403 anyone but the reporter (a
manager and an Admin included), then 400 for a type other than `image/jpeg` / `image/png`,
over 5 MB, or **bytes that do not start with that type's signature** — the content type is a
header the client wrote, so the magic-byte check is what makes it worth anything. The rules
live in `ImageUploadRules`, shared by any future photo upload.

- **`IFileStorageService.UploadAsync(stream, contentType, folder)` has no file-name
  parameter, on purpose.** The object name is a server-generated GUID plus an extension
  from the validated content type; the uploaded name is never read. A client-chosen name is
  the classic path-traversal vector, and the surest way never to use it is for there to be
  nowhere to pass it. Keep the interface generic — completion photos use it as it is.
- **Storage failure is a 503 and writes nothing.** `UploadAsync` returns null — never throws —
  for unreachable, timed out, not configured or refused, and the report keeps whatever photo
  it had. A URL pointing at nothing is worse than no photo.
- `AddScoped`, with a named `HttpClient` from `IHttpClientFactory` (30s timeout).
  `Supabase:Url` / `Supabase:ServiceKey` / `Supabase:StorageBucket` (or the `SUPABASE_*`
  names); unset boots with a warning, like the agent settings. The bucket must be **public** —
  the stored URL is the object's public URL and the clients load it directly.
- Tests replace the named client's primary handler (`StorageStubApiFactory`), so the real
  `SupabaseStorageService` runs and only the network is fake — no test can reach Supabase.

---

## PROJECT RULES

- **All deterministic business rules live in C#, never in an AI prompt.** That includes:
  SLA clocks, cost thresholds, timetable conflicts, technician availability, failure
  counts, warranty dates, and approval routing.
- **The Python agent service has NO database credentials.** It reads data only through
  allow-listed HTTP tool calls back to the API.
- **React and Flutter talk ONLY to the ASP.NET Core API**, never to the agent service.
- **There is no chat interface anywhere in this system.**
- **Prefer the simplest implementation a third-year student can explain in a viva.**

---

## Secrets

Never commit real values. `.env`, `*.env` and `appsettings.Development.json` are
git-ignored. Add any new configuration key to `.env.example` with an empty value and a
one-line comment.

**Nothing loads the root `.env` automatically, and the two services differ.** The agent
service reads it, because pydantic-settings does that itself. **The ASP.NET Core API does
not** — it has no dotenv package, so `builder.Configuration["DATABASE_URL"]` reads a real
*environment variable*, and a value sitting in `.env` reaches the API only if the shell
exported it first (`set -a; source .env; set +a`) or a tool like Docker Compose loaded it.
Editing `.env` and restarting the API on its own changes nothing. This is worth knowing
before debugging a "the API is ignoring my configuration" problem.

For the API specifically, local secrets go through `dotnet user-secrets` (already
initialised on `api/CampusFacilities.Api.csproj` — the `<UserSecretsId>` in that file is
not a secret and should stay committed). Configuration is layered, each overriding the
last: `appsettings.json` < `appsettings.Development.json` < user secrets < environment
variables. README has the exact `dotnet user-secrets set` commands for first-time setup.

`Supabase:ServiceKey` is the **service role** key: it bypasses every Storage policy, so it
lives in user secrets on the API and nowhere else — never in `web/.env`, never in a
`--dart-define`. The `anon` key is not a substitute; uploads authenticate with it and fail.
The API boots without Supabase configured and photo uploads return 503, so a teammate not
working on photos needs none of it.

## Issue template

`.github/ISSUE_TEMPLATE/feature.md` covers both features and chores (Context / Proposal /
Acceptance criteria / Out of scope / Related) — deliberately one template, not split by
type, to avoid a chooser screen for a four-person team. Use a `feature` / `chore` label
for the type distinction instead of a second template.
