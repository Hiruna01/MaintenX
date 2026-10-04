<!--
  Report chapters 1 and 2. Chapter 3 (architecture) is 03-architecture.md.
  BEFORE SUBMITTING: replace every [Student …] placeholder in Table 1.3 with the owner's
  name and IT number, and confirm the component-to-owner mapping with the team.
-->

# 1. Project Overview and Scope

## 1.1 The business problem

A university campus runs on shared equipment: projectors in lecture halls, air conditioners
in computer labs, workstations, water pumps. When one of them fails, the usual process is
informal. Someone complains, a technician is sent, a quick fix is applied, and the case is
considered closed. That process has five weaknesses, and MaintenX is designed around them.

1. **Reports are vague.** A student writing "the projector isn't working" does not say whether
   it is dead or intermittent, whether it is safe to leave, or which of the room's machines is
   meant. The technician finds out on arrival, often after a wasted visit.
2. **Repeat failures go unnoticed.** Each complaint is handled on its own. A projector that
   overheats three times in four months, and is "fixed" with a filter clean each time, looks
   like three unrelated jobs rather than one failing unit. The pattern is visible only in the
   repair history, and nobody reads that history across visits.
3. **Spending is not controlled consistently.** Whether a repair is costly enough to need a
   manager's sign-off depends on who raises it, not on a stated rule.
4. **Repairs disrupt teaching.** Maintenance visits are booked without reference to the
   timetable, so a technician can arrive in the middle of a lecture.
5. **Nobody checks whether a repair held.** A completed job is the technician's account of the
   work. Nothing compares it with what the people in the room experience days later, so a
   failed repair enters every figure as a success.

## 1.2 The solution: MaintenX

MaintenX is a campus facilities maintenance system that follows a fault **from the moment it
is reported to the day the repair is confirmed to have held**, as one closed loop:

> **Report → Clarify → Diagnose → Propose → Approve → Schedule → Repair → Verify**
> (and, when the repair did not hold, back to **Diagnose**)

A reporter files a fault from the phone, optionally scanning the equipment's QR sticker and
attaching a photo. Five AI agents then do the reading-heavy work that people skip:

- they decide whether the report needs clarifying and ask at most two bounded questions;
- they read the machine's full service history to identify likely causes and repeat failures;
- they propose one repair strategy with a cost estimate;
- after the repair, they judge whether it held.

People make every decision that matters, and fixed business rules written in C# make the rest.
The agents' output is **advice**. Whether a work order needs approval is an arithmetic
comparison in the API. When a slot is free is a calculation against the mirrored campus
timetable. Whether a repair held is the reporter's own yes or no. This separation is the central
design principle of the system and runs through every chapter of this report:

> **The agents interpret unstructured text; deterministic C# code decides.**

## 1.3 Objectives

| # | Objective | How it is met |
|---|---|---|
| O1 | Capture faults with enough detail to act on, without a free-text conversation | A phone report form with QR asset identification and photo; at most two clarification questions, each answered through a bounded control (yes/no, a picker, or text of up to 100 characters) |
| O2 | Detect repeat failures from the service history | Deterministic failure summary (3 or more visits in 90 days), plus a diagnostic agent that reads every technician note verbatim |
| O3 | Enforce one approval rule for spending | An approval gate in C#: an estimate above the configured threshold (Rs 15,000 by default), or any replacement, waits for a Facilities Manager |
| O4 | Schedule repairs around teaching | A slot finder that reads the campus timetable (mirrored from Google Calendar) and keeps a buffer around every class |
| O5 | Verify repairs after a delay | A background sweep asks the reporter "is it fixed?" five days after completion, and a "no" reopens the case for a fresh diagnosis |
| O6 | Make every automated step auditable | Every agent run, tool call and approval decision is stored as a workflow step and displayed as an execution history |
| O7 | Give each kind of user a client suited to their work | A React web application for managers and administrators; a Flutter mobile application for reporters and technicians; one shared API behind both |

## 1.4 The system at a glance

MaintenX consists of one public API, one internal agent service, one database and two client
applications. Chapter 3 describes the architecture in detail.

| Part | Technology | Purpose |
|---|---|---|
| **API** | ASP.NET Core Web API (.NET 8), EF Core | The authoritative application layer: authentication, authorisation, validation, every business rule, persistence, workflow orchestration, approval and audit |
| **Database** | PostgreSQL | The system of record: 15 tables, managed through EF Core migrations |
| **Agent service** | Python, FastAPI, LangGraph | An internal service called only by the API. It hosts the five agents and holds no database credentials; it reads campus data only through an allow-listed tool router on the API |
| **Web application** | React 18 (Vite), Context API | The management side: report intake, the approval queue, the dispatch board and scheduling, the asset registry, the estate, user accounts, verification, metrics and agent workflow monitoring |
| **Mobile application** | Flutter, Riverpod, go_router | The field side: self-registration, filing a report (QR scan, camera or gallery photo), answering clarification questions, a technician's jobs and their completion, and a reporter's confirmation that a repair held |

The two clients serve **different purposes** and **communicate only with the API**. The phone
is for people standing next to the equipment. The web application is for people making
decisions about it. The same user identities, permissions, data and business rules apply to
both, because both go through the same API.

**Third-party services integrated:**

| Service | Business purpose | Accessed from |
|---|---|---|
| Google Calendar API | The campus timetable, read through a service account and mirrored into the database, so that repairs are never booked during a class | API only |
| Supabase Storage | Stores report photos and repair-completion photos (metadata such as GPS location is stripped before upload) | API uploads; clients display the public URL |
| An OpenAI-compatible LLM provider (OpenRouter in development) | Model inference for the agents | Agent service only |

## 1.5 Business components

The system is divided into four primary business components, one per student. Each component
spans the full stack: API endpoints with at least one operation beyond CRUD, database
entities, a React surface, a Flutter surface, tests and a distinct agent.

**Table 1.3 — Business components and ownership**

| | **A. Asset registry and agent orchestration** | **B. Fault reporting and clarification** | **C. Work orders, approval and scheduling** | **D. Repair verification and analytics** |
|---|---|---|---|---|
| **Owner** | [Student A — name, IT number] | [Student B — name, IT number] | [Student C — name, IT number] | [Student D — name, IT number] |
| **Main entities** | `Asset`, `AssetCategory`, `ServiceRecord`, `Building`, `Room` | `Report`, `ClarificationQuestion`, `ClarificationAnswer` | `WorkOrder`, `ScheduledSlot`, `ClassScheduleSlot` | `VerificationCheck` |
| **Business operations beyond CRUD** | Failure summary (repeat-failure and warranty rules); retirement instead of deletion; QR lookup by asset tag | Bounded clarification submission; report lifecycle; photo upload with metadata stripping; the reporter's progress stage | Approval gate; approve / reject / request revision / resubmit; timetable-aware slot finder; completion transaction; repair SLA; Google Calendar sync | Delayed verification sweep; reporter confirmation; confirmation and reopen rates; estate-wide metrics |
| **React** | Asset registry, asset detail and QR label; buildings, rooms and categories; user accounts | Report intake queue; report detail with agent reasoning, status control and raise-order panel | Dispatch board; work order detail with assignment, slot finder and booking; approval queue | Verification list and detail; metrics dashboard |
| **Flutter** | QR scanner; asset detail with service history | Report form (QR, photo); my reports; clarification form | My jobs; job detail; job completion with photo | Pending confirmations; "is the problem fixed?" form |
| **Agent** | Diagnostic Agent (and the orchestration: `graph.py`, `WorkflowRunner`) | Clarifier Agent | Resolution Strategist | Verification Agent |

The fifth agent, the **Planner Agent**, coordinates each run. It reads the objective, produces
the structured plan the workflow follows, and decides whether the clarifier is needed. Its
owner is [Student — name].

The agent workflow itself (`AgentWorkflow` and `AgentStep`, the workflow state machine, and
the endpoints for starting, monitoring and auditing runs) is shared infrastructure that every
component's agent runs through.

## 1.6 Scope

### 1.6.1 In scope

- Four user roles, with self-registration for Reporters and Admin-issued accounts for staff.
- The full fault lifecycle described in §1.2, including both human pauses (the reporter's
  clarification answers and the manager's approval decision) and the reopen loop.
- An asset registry with QR stickers, service history, warranty dates and failure summaries.
- An estate of buildings and rooms, and a mirror of the campus timetable.
- Work order approval, technician assignment, timetable-aware scheduling, completion with
  actual cost and outcome, and a repair SLA.
- Delayed repair verification and the operational metrics built on it.
- Five agents on a LangGraph pipeline, with persisted workflow state, an allow-listed tool
  router, deterministic validation of every output, and a complete audit trail.
- Search, filtering, sorting and pagination on every list that can grow, and analytics for
  managers.

### 1.6.2 Out of scope, by design

Each exclusion below was deliberate. The reason is given because each one is a likely viva
question.

| Excluded | Reason |
|---|---|
| **A chat interface of any kind** | Every exchange with a reporter is a bounded form. The clarifier asks at most two questions, each answered through a toggle, a fixed-option picker or up to 100 characters of text, and the API enforces those bounds itself. A chat box would make the agents' input unbounded and their behaviour unauditable. |
| Refresh tokens | Access tokens only, with a 12-hour lifetime. A refresh flow needs a persisted token store, rotation, reuse detection and revocation, which is a separate feature. Deactivation and role changes still take effect on the next request, because the API re-checks the account on every call. |
| Deleting assets, users or history | Equipment is retired and accounts are deactivated, never deleted. The service history outlives the machine, and every foreign key into it is `Restrict`. |
| Agents taking decisions | No agent approves, raises a cost, moves a workflow or closes a case. Those are C# rules or human actions. |
| Automatic escalation of a repeat failure | The verification agent may say `escalate`, but that label is advice that nothing acts on. Whether escalation should be a C# rule or a manager's call is left open. |
| Email, SMS and push notifications | A reporter sees a check waiting on the phone when the sweep puts it there; the state change is the notification. |
| Public holidays in scheduling | The slot finder works to weekdays, 08:00–17:00 campus time, and the timetable. |
| Payments and procurement | Costs are recorded and approved, not paid. |

## 1.7 Assumptions and constraints

- **Mandated stack:** ASP.NET Core Web API, EF Core with PostgreSQL, React and Flutter, with
  an agentic AI subsystem integrated through the API.
- **No-cost services:** the system has to run on free tiers (PostgreSQL and storage on
  Supabase, a free or low-cost LLM model, Google Calendar through a service account).
  Background work is therefore designed to survive a host that sleeps and restarts.
- **Currency and time:** costs are Sri Lankan rupees, held as `decimal` with two decimal
  places. Working hours are campus-local time (`Asia/Colombo`); every stored instant is UTC.
- **LLM provider:** any OpenAI-compatible chat-completions endpoint. Changing provider is a
  configuration change, not a code change.
- **Timeframe:** nine weeks (31 July – 30 September 2026), four developers.

## 1.8 Demonstration data

In development, the API seeds a small but deliberately realistic campus.

- **2 buildings and 6 rooms:** the Main Academic Block (Lecture Halls A and B, Seminar Room 1)
  and the Engineering Faculty (Computer Labs 1 and 2, the Electronics Lab).
- **4 asset categories, 8 assets and 14 service records.** Projector `PRJ-MAB101-01` in Lecture
  Hall A carries a planted repeat-failure history: three visits over four months, the same
  thermal fault returning twice after temporary fixes, with notes written tersely, the way
  technicians write them.
- **One demo account per role:** `reporter@`, `technician@`, `manager@` and `admin@campus.test`.
- **Completed repairs with verification checks** (confirmed, reopened and overdue), and **live
  work orders** waiting for approval and assignment, so that every screen opens with data.
- **A 15-lecture weekly timetable** across the six rooms (`docs/timetable/campus-timetable.ics`),
  and **printable QR stickers** for all eight assets (`docs/qr/asset-qr-sheet.png`).

---

# 2. Requirements and User Roles

## 2.1 User roles

MaintenX has four roles. A user holds exactly one. Roles are **not** a seniority ladder: every
permission is granted to named roles explicitly, so an Admin is refused a Facilities Manager's
actions, such as approving a work order, just as a Technician is.

**Table 2.1 — User roles**

| Role | Who they are | Responsibilities | Main client |
|---|---|---|---|
| **Reporter** | A student, lecturer or member of staff who uses the rooms | Reports faults, answers the clarification questions about their own reports, follows their reports' progress, and confirms whether a repair held | Mobile |
| **Technician** | Maintenance staff | Works the jobs assigned to them, reads each machine's history and the agent's diagnosis before a visit, and completes jobs with outcome, cost, notes and a photo | Mobile (the web board also shows their jobs) |
| **Facilities Manager** | The person accountable for maintenance and its budget | Triages reports, approves, rejects or sends back work orders, assigns technicians, books visits around the timetable, runs the verification sweep and the timetable sync, and monitors agent workflows and metrics | Web |
| **Admin** | The system administrator | Maintains the asset registry, buildings, rooms and categories, and user accounts (create, edit, deactivate or reactivate, reset password); monitors workflows and metrics | Web |

**How accounts are created.** Anyone may register on the phone, and every self-registration
creates a Reporter. The API ignores any other role requested without an Admin's token, and
refuses it with **403**. Technician, Facilities Manager and Admin accounts are created by an
Admin on the web. The web application has no sign-up page, because it is for staff.

## 2.2 Permission matrix

The matrix below is enforced in the API, through one authorisation policy per role, a fallback
policy that makes "signed in" the default for every endpoint, and visibility rules in the
services. The clients mirror it by hiding what a role would only be refused. Only four
endpoints are anonymous: register, login, `/health`, and the internal tool router, which is
protected by a shared secret instead.

**Table 2.2 — Who may do what** (✓ = permitted; *own* = limited to the user's own records; — = refused)

| Capability | Reporter | Technician | Facilities Manager | Admin |
|---|:---:|:---:|:---:|:---:|
| Register (self), sign in, sign out | ✓ | ✓ | ✓ | ✓ |
| File a report, attach a photo, scan an asset's QR code | ✓ | ✓ | ✓ | ✓ |
| View reports | *own* | *own* | ✓ all | ✓ all |
| Answer clarification questions | *own* | *own* | — | — |
| Change a report's status manually | — | — | ✓ | — |
| View and start agent workflows | — | — | ✓ | ✓ |
| Browse assets, service history and failure summaries | ✓ | ✓ | ✓ | ✓ |
| Register, edit and retire assets; manage categories, buildings and rooms | — | — | — | ✓ |
| Manage user accounts | — | — | — | ✓ |
| View work orders | — | assigned to them | ✓ all | ✓ all |
| Raise, approve, reject, request revision, resubmit | — | — | ✓ | — |
| Assign technicians, find slots, book visits, sync the timetable | — | — | ✓ | — |
| Complete a job and attach a completion photo | — | assigned to them | — | — |
| View repair verification checks | *own reports* | *own reports* | ✓ all | ✓ all |
| Confirm whether a repair held | *own reports* | *own reports* | — | — |
| Run the verification sweep now; view verification metrics | — | — | ✓ | — |
| View estate-wide metrics | — | — | ✓ | ✓ |

**401 and 403 are kept distinct throughout.** A request with no valid token is **401**
("who are you?"). A valid token for the wrong role, or for someone else's record, is **403**
("I know who you are, and no"). A record that does not exist is **404**, told apart from
someone else's record by an existence check made before the permission check.

## 2.3 Functional requirements

Each requirement is identified by component. The API endpoints named are the authoritative
implementation; the clients call them.

### Cross-cutting (all components)

| ID | Requirement |
|---|---|
| FR-X1 | Users sign in with email and password and receive a JWT access token (12-hour lifetime) carrying their id, email and role. |
| FR-X2 | Anyone may self-register as a Reporter from the mobile application; only an Admin may create an account with any other role. |
| FR-X3 | A deactivated account cannot sign in, and a deactivation or role change takes effect on the user's next request. |
| FR-X4 | Every list that can grow is paginated on the server with a stable order. Assets, reports, work orders and verification checks also support server-side search, exact filters and a choice of sort; users support search and filters; workflows filter by state. |
| FR-X5 | Every screen in both clients shows distinct loading, empty, error and success states. |

### Component A — Asset registry and agent orchestration

| ID | Requirement |
|---|---|
| FR-A1 | An Admin can register, edit and retire assets. Retiring (`DELETE /api/assets/{id}`) sets the status to `Retired` and keeps all history. |
| FR-A2 | Every asset has a unique asset tag that is its QR payload. The tag cannot be edited after registration. |
| FR-A3 | Any signed-in user can look up an asset by scanning its QR sticker (`GET /api/assets/by-tag/{tag}`). An unknown tag is a clear "not registered" answer, not an error. |
| FR-A4 | An asset's detail shows its full service history, oldest first, with every technician note verbatim. |
| FR-A5 | The system computes a failure summary for any asset (`GET /api/assets/{id}/failure-summary`): failures in the last 12 months and last 90 days, days since last service, temporary-fix count, warranty cover and a repeat-failure flag. |
| FR-A6 | An Admin can manage buildings, rooms and asset categories, and every refusal (a code in use, a building with rooms, a room still referenced) is reported as a 409 or 400, never a server error. |
| FR-A7 | An Admin can create, edit, deactivate, reactivate and reset the password of user accounts, but cannot deactivate or change the role of their own account. |
| FR-A8 | Filing a report starts an agent workflow in the background. The request never waits for the agents. |

### Component B — Fault reporting and clarification

| ID | Requirement |
|---|---|
| FR-B1 | A signed-in user can file a report with a description (10–1000 characters) and a room, and optionally the asset identified by scanning its sticker. The asset must be in that room. |
| FR-B2 | A reporter can attach one photo (JPEG or PNG, up to 5 MB) to their report. Location and camera metadata are removed before it is stored. |
| FR-B3 | When the agents need more detail, the reporter is shown at most two questions, each answered with a yes/no toggle, a picker over fixed options, or short text of up to 100 characters. All answers are submitted together, once. |
| FR-B4 | The API rejects any answer outside its control: an option that was not offered, anything but exactly "Yes" or "No", text over the limit, a question left unanswered, or a second answer. |
| FR-B5 | A reporter can list and search their own reports and see each one's plain-language progress stage (being reviewed, waiting on you, awaiting approval, repair planned, repaired, not going ahead, closed). |
| FR-B6 | A Facilities Manager can view the report intake queue with filters, and each report's full agent reasoning (every agent run and tool call, in order). |
| FR-B7 | A report's status follows a fixed lifecycle. It moves automatically with its workflow, and a manager can move it manually only along permitted transitions. |

### Component C — Work orders, approval and scheduling

| ID | Requirement |
|---|---|
| FR-C1 | A work order is raised from the strategist's proposal automatically when it is usable, or by a Facilities Manager from the report page otherwise. The client never chooses the order's status. |
| FR-C2 | An order whose estimate is above the approval threshold, or whose strategy is a replacement, waits for a Facilities Manager. Any other order is approved automatically. |
| FR-C3 | The approval queue presents each waiting order with everything the decision needs: the report, the cost against the threshold, the agent's diagnosis and proposal, the asset's failure summary and history. |
| FR-C4 | A Facilities Manager can approve, reject (with a reason) or request revision (with a note). A revision sends the order back to the strategist with the note, and the revised order is resubmitted through the same approval gate. |
| FR-C5 | A Facilities Manager can assign an approved order to an active Technician. |
| FR-C6 | The system offers free visit slots for an asset that avoid classes in its room (with a buffer), fall inside working hours, and do not clash with the technician's other visits. It re-checks a slot when it is booked. |
| FR-C7 | The campus timetable is synchronised from Google Calendar on a schedule and on demand. If Google is unavailable, the last synchronised timetable is still used. |
| FR-C8 | The assigned Technician completes a job with an outcome, the actual cost, a resolution note of at least 20 characters and an optional photo. Completion appends a permanent service record and starts the verification clock. |
| FR-C9 | Every approved order carries a repair SLA due date, and shows whether it is on track, overdue, met or missed. |

### Component D — Repair verification and analytics

| ID | Requirement |
|---|---|
| FR-D1 | A verification check is created for every completed work order, falling due a configured number of days after completion (5 by default). |
| FR-D2 | A background sweep, which a Facilities Manager can also run on demand, moves due checks to the reporter. |
| FR-D3 | The reporter answers "is the problem fixed?" with yes or no and an optional comment of up to 300 characters. A yes closes the case; a no reopens it for a fresh diagnosis on the same asset. |
| FR-D4 | Every check, answered or silent after its response window, is reviewed by the verification agent, whose verdict is recorded beside the reporter's answer as advice. |
| FR-D5 | Facilities Managers can view the confirmation rate. Managers and Admins can view estate-wide metrics: reopen rate by category and over six months, clarification figures, and repeat-failure assets ranked by cost. |

### Agentic AI workflow

These requirements implement the specification's minimum assessed workflow (§9.1).

| ID | Requirement |
|---|---|
| FR-AG1 | **Objective:** each report's description becomes the objective of one agent workflow. |
| FR-AG2 | **Plan:** the Planner Agent produces a structured plan of two or three steps (agent and purpose). The API validates it against fixed rules before storing it, and stores a safe default plan if it fails. |
| FR-AG3 | **Delegation:** the plan's steps are delegated to four distinct specialised agents (Clarifier, Diagnostic, Strategist, Verification), each with its own input and output contract and its own tool subset. |
| FR-AG4 | **Tools:** agents read campus data only through an allow-listed tool router on the API, authenticated by a shared secret. An unknown tool is refused and recorded. |
| FR-AG5 | **State:** the workflow's state, plan, every step, tool result, validation result, error, retry count, timing and approval decision are persisted in PostgreSQL. |
| FR-AG6 | **Validation:** every agent output is validated against a strict schema (with one retry, then a safe failure), and every business consequence is decided by C# rules. |
| FR-AG7 | **Human approval:** two pauses. The reporter's answers are required before diagnosis continues, and a Facilities Manager must approve, reject or revise any high-cost or replacement order before work proceeds. |
| FR-AG8 | **Outcome:** each run ends in an auditable result or a recorded safe failure. A failed run can be run again, and a failed agent never prevents a manager from acting. |
| FR-AG9 | **Observability:** managers can see each workflow's state, plan and step-by-step execution history in the web application. |

## 2.4 Business rules

Every rule below is deterministic, lives in C# in the API, and is configurable where a real
deployment would need it to be. **None of them is decided by an AI model.**

**Table 2.4 — Business rules**

| Rule | Value (default) | Where enforced |
|---|---|---|
| Approval required | Estimate **strictly above** Rs 15,000, **or** strategy `EscalateReplacement` at any cost. Exactly Rs 15,000 is auto-approved. | `WorkOrderService` (`ApprovalBasisFor`), `Approval:CostThreshold` |
| Repair SLA | 7 calendar days from **approval** (not from raising). Completion exactly at the due time is on time. | `SlaRules`, `Sla:ResolutionDays` |
| Repeat failure | 3 or more service visits in the last **90 days** (exactly 90, not "3 months") | `FailureRules` |
| Warranty cover | Covered up to and including the expiry date; no recorded expiry means not covered | `FailureRules` |
| Verification delay | The check falls due 5 days after completion, measured from the completion time | `Verification:DelayDays` |
| Reporter response window | After 3 days without an answer, the check goes to the agent anyway | `Verification:ResponseWindowDays` |
| Working hours | Weekdays 08:00–17:00 campus time (`Asia/Colombo`), a visit on a single day, 30-minute grid, at most 20 slots offered | `SlotRules`, `SchedulingSettings` |
| Class buffer | No visit within 15 minutes either side of a timetabled class in the room | `SlotRules` |
| Technician availability | No overlap with the technician's visits on live orders | `SlotRules` |
| Clarification bounds | At most 2 questions; yes/no, a picker over fixed options, or text ≤ 100 characters; one answer per question | Agent schema and `ClarificationService` |
| Resolution note | At least 20 characters, at most 2000 | `CompleteWorkOrderDto` |
| One live run per report | A report cannot start a new workflow while its latest one is still running, or once it is closed | `WorkflowService` |
| One order per run | A run raises one order; while a revised draft waits, a second order is refused | `WorkOrderService` |
| Only the assigned technician completes a job | Anyone else is refused with 403 | `WorkOrderService` |
| Only the reporter answers | Clarification answers and repair confirmations come from the reporter who filed the fault | `ClarificationService`, `VerificationService` |
| Admin safety | An Admin cannot deactivate themselves or change their own role; a Technician with unfinished work cannot be deactivated or re-roled | `UserService` |

## 2.5 Non-functional requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-1 | **Security** | JWT bearer authentication with issuer, audience, signing key and lifetime validated. Passwords hashed with ASP.NET Core's `PasswordHasher`. Role-based policies, with authenticated access as the default. Secrets come only from configuration and environment variables and are never committed. Request logs never contain request bodies. |
| NFR-2 | **Secure token storage** | The mobile application stores its token in the iOS Keychain or Android encrypted storage (`flutter_secure_storage`), never in plain preferences. |
| NFR-3 | **Agent isolation** | The agent service holds no database credentials. The clients never contact it. Both directions of API ↔ agent traffic require a shared secret. |
| NFR-4 | **Prompt-injection resistance** | Untrusted text (reports, answers, technician notes, manager notes) reaches a model only as one JSON-encoded data block. Agent outputs are schema-validated. No output can grant approval, because no output schema has a field for it. |
| NFR-5 | **Responsiveness** | No request waits on an agent. Report filing returns immediately and the workflow runs in a background worker; clients follow progress by polling. |
| NFR-6 | **Reliability and safe failure** | Every outbound call has a timeout. The LLM client retries once and then returns a recorded safe failure rather than an error. A workflow interrupted by a restart is re-queued at startup. Google Calendar or Supabase being unavailable degrades one feature and never breaks the API. |
| NFR-7 | **Data integrity** | Money is `decimal` (`numeric(18,2)`), never floating point. Unique indexes, foreign keys and CHECK constraints are enforced in the database. Multi-step writes (filing a report with its workflow, completing a job, approval decisions, booking a slot) are transactions. |
| NFR-8 | **Auditability** | Every agent run, tool call and approval decision is recorded with its payload, result, timing and attempts, and is never edited afterwards. |
| NFR-9 | **Privacy** | Photo metadata (GPS, camera, comments) is stripped before storage. Agent tools return fact records without names or reporter ids. No hidden reasoning, passwords or tokens are stored. |
| NFR-10 | **Usability and accessibility** | Both clients are responsive down to phone width. The web client has visible focus rings and respects reduced-motion preferences. Every screen has explicit loading, empty and error states. |
| NFR-11 | **Portability** | The LLM provider is a configuration choice (base URL, key and model), with no provider-specific features in use. |
| NFR-12 | **Maintainability and quality** | Automated tests at every layer (API against SQLite and PostgreSQL, agent service, React, Flutter), run by GitHub Actions on every push and pull request to `main`. |

## 2.6 The main cross-platform workflow

The specification requires at least one workflow that begins in one client, passes through
ASP.NET Core, PostgreSQL and the agents, needs approval in the other client, and returns an
updated status to the initiating user. In MaintenX that workflow is the core of the product:

| Step | Actor and client | What happens | Reporter sees |
|---|---|---|---|
| 1 | Reporter, **Flutter** | Files a report, scans the projector's sticker and attaches a photo. The API saves it, starts a workflow and returns 201 at once. | Being reviewed |
| 2 | Agents, via **API** | The Planner plans the run; the Clarifier reads the room and asset through tools and asks two bounded questions. The workflow pauses. | **Waiting on you** |
| 3 | Reporter, **Flutter** | Answers the form in one submission. The workflow resumes. | Being reviewed |
| 4 | Agents, via **API** | The Diagnostic Agent reads the service history and names the failing cooling fan; the Strategist proposes a replacement. The C# approval gate routes it to a manager. | Awaiting approval |
| 5 | Facilities Manager, **React** | Reviews the case in the approval queue and approves, rejects or requests revision; assigns a technician; books a slot clear of the timetable. | Repair planned (or *Not going ahead*) |
| 6 | Technician, **Flutter** | Completes the job with outcome, cost, note and photo. A service record is appended. | Repaired |
| 7 | Sweep, **API** | Five days later the verification check reaches the reporter. | **Waiting on you** |
| 8 | Reporter, **Flutter** | Answers "is it fixed?". Yes closes the case; no reopens it, and the Diagnostic Agent runs again with the failed repair on record. | Closed, or Being reviewed |

Every step changes rows in PostgreSQL (the report, the workflow and its steps, the work order,
the service record and the verification check) and adds to the workflow's execution history,
which the manager can inspect on the web at any point.

## 2.7 Traceability to the minimum domain requirements

**Table 2.7 — Specification §4.1 against MaintenX**

| Specification requirement (§4.1) | How MaintenX meets it |
|---|---|
| At least three user roles with different permissions | Four roles: Reporter, Technician, Facilities Manager and Admin (Tables 2.1 and 2.2) |
| At least four major business components with relational data and business-specific operations | Components A–D (Table 1.3), 15 related tables, business operations listed per component |
| CRUD plus status workflows, search, filtering, sorting, pagination and reporting or analytics | CRUD on the estate, assets, users and work orders; lifecycles for reports, workflows, work orders and verification checks; FR-X4; the metrics page (FR-D5) |
| Different purposes for React and Flutter | Web for management and decisions, mobile for reporting and field work (§1.4) |
| At least one third-party integration | Google Calendar (timetable) and Supabase Storage (photos), plus the LLM provider |
| A complete cross-platform workflow through React, Flutter, ASP.NET Core, PostgreSQL and agentic AI | §2.6 |
| A meaningful device feature (§8) | QR scanning of asset stickers, and the camera and gallery for photos |
