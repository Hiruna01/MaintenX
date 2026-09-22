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
  `agents/` plus one node and one edge here. Routing decisions go in
  `add_conditional_edges` as plain Python reading the state — never a judgement made by a
  model. Compile **without a checkpointer**: nothing persists between runs.

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
  is asserted to be exactly `{"questions"}`, and input DTOs use `extra="forbid"` so a stray
  `conversation_history` is a 422.
- The `messages` list inside `llm_client.py` is the retry within a *single* call — a local
  variable, discarded when the function returns. Nothing survives across `/run` calls.

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
- **Enums are matched by NAME, never by ordinal.** `features/auth/services/roles.js` and
  `WORKFLOW_STATES` in the workflows service hold the same strings the API sends and accepts,
  so a member inserted into a C# enum cannot silently shift the client's meaning.
- **Navigation is role-based**: a Reporter must not see manager links. The route guard would
  refuse them anyway, but offering a link that leads to "not authorised" is a bad interface.

### Routing

`BrowserRouter` in `main.jsx`, every route in `routes/AppRoutes.jsx`, `NavLink` for
navigation, `ProtectedRoute` for the guards, and a catch-all `*` route. `ProtectedRoute`
remembers where the user was heading and sends them back there after sign-in.

### Styling and configuration

- **Plain CSS or CSS modules. No Tailwind, no component library.** Presentation is not what
  this project is marked on, and it costs time the team does not have.
- Only `VITE_`-prefixed keys reach the browser, so **nothing secret belongs in `web/.env`**.
  New keys go in `web/.env.example` with an empty value and a one-line comment, same rule as
  the root file. `VITE_API_BASE_URL` points at the ASP.NET Core API — the client talks to
  that API and nothing else — and the API must allow the dev server's origin through
  `Cors__AllowedOrigins__0`.

---

## MOBILE — Flutter, `mobile/`

Flutter + Riverpod + go_router. Targets **Android and iOS**; the platform folders are
generated with `flutter create` and otherwise left alone. Run everything from `mobile/`:
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
  holds the same strings the API sends and accepts.

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
back in order. **Nothing collects the answers yet.** `SubmitAnswersRequest` exists as a DTO
but no endpoint is behind it, so the questions are still displayed read-only. When that
endpoint lands it takes the whole form in ONE request and the exchange is over: there is no
follow-up round and no chat interface.

Photo attachment and QR scanning are disabled buttons marked `TODO(photo)` / `TODO(qr)` —
visible rather than hidden, so the finished shape of the form stays obvious.

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

## Issue template

`.github/ISSUE_TEMPLATE/feature.md` covers both features and chores (Context / Proposal /
Acceptance criteria / Out of scope / Related) — deliberately one template, not split by
type, to avoid a chooser screen for a four-person team. Use a `feature` / `chore` label
for the type distinction instead of a second template.
