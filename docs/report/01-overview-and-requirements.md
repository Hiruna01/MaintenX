# 1. Project Overview and Scope

## 1.1 The business problem

University campuses depend on shared equipment such as projectors, air conditioners, lab
workstations and pumps. When something fails, the usual process is informal: someone complains,
a technician applies a quick fix, and the case is closed. This causes five problems:

1. **Vague reports.** "The projector isn't working" does not say whether it is dead or
   intermittent, or which machine is meant, so technicians often make wasted visits.
2. **Repeat failures go unnoticed.** Each complaint is handled on its own. A unit that overheats
   three times in four months looks like three unrelated jobs, because nobody reads the repair
   history across visits.
3. **Inconsistent spending control.** Whether a costly repair needs a manager's sign-off depends
   on who raises it, not on a rule.
4. **Repairs disrupt teaching.** Visits are booked without checking the timetable.
5. **Nobody checks whether a repair held.** A completed job counts as a success even when the
   fault returns days later.

## 1.2 The solution: MaintenX

MaintenX is a campus maintenance system that follows a fault **from report to a confirmed
repair**, as one closed loop:

> **Report → Clarify → Diagnose → Propose → Approve → Schedule → Repair → Verify**
> (and back to **Diagnose** if the repair did not hold)

A reporter files a fault on the phone, optionally scanning the equipment's QR sticker and adding
a photo. Five AI agents do the reading-heavy work: they plan the case, ask at most two bounded
questions, read the machine's full service history to find likely causes and repeat failures,
propose a repair with a cost estimate, and later judge whether the repair held.

The central design principle is **agents advise, C# decides**. Agent output is advice only.
Whether a work order needs approval, when a visit can be booked, and whether a repair held are
decided by deterministic business rules in the API or by a person.

## 1.3 Objectives

- **O1** Capture faults with enough detail to act on, through bounded questions rather than a chat.
- **O2** Detect repeat failures from each asset's service history.
- **O3** Apply one approval rule to spending: costly work and replacements need a manager.
- **O4** Schedule repairs around the campus timetable.
- **O5** Verify repairs with the reporter after a delay, and reopen the case if it failed.
- **O6** Record every automated step so it can be audited.
- **O7** Give each kind of user a suitable client, both backed by one shared API.

## 1.4 System at a glance

| Part | Technology | Purpose |
|---|---|---|
| API | ASP.NET Core Web API (.NET 8), EF Core | Authentication, authorisation, validation, every business rule, persistence, workflow orchestration, approval and audit |
| Database | PostgreSQL | System of record (15 tables, EF Core migrations) |
| Agent service | Python, FastAPI, LangGraph | Five agents, called only by the API; no database access |
| Web app | React 18, Context API | Management: report intake, approvals, dispatch and scheduling, assets, users, workflow monitoring, metrics |
| Mobile app | Flutter, Riverpod | Field work: register, report a fault (QR scan, photo), answer questions, complete jobs, confirm repairs |

**Third-party services:** Google Calendar (the campus timetable, so visits avoid classes),
Supabase Storage (report and repair photos), and an OpenAI-compatible LLM provider (agent
inference).

## 1.5 Business components

There are four primary components, one per student. Each spans the API, database, React,
Flutter and one agent.

| Component | Owner | Main entities | Key business operations | Agent |
|---|---|---|---|---|
| **A. Asset registry and orchestration** | [Name, IT no.] | Asset, AssetCategory, ServiceRecord, Building, Room | Failure summary (repeat failure, warranty); retire instead of delete; QR lookup; workflow runner | Diagnostic |
| **B. Fault reporting and clarification** | [Name, IT no.] | Report, ClarificationQuestion, ClarificationAnswer | Bounded clarification; report lifecycle; photo upload with metadata removal | Clarifier |
| **C. Work orders, approval and scheduling** | [Name, IT no.] | WorkOrder, ScheduledSlot, ClassScheduleSlot | Approval gate; approve / reject / revise; timetable-aware slot finder; completion; SLA; calendar sync | Resolution Strategist |
| **D. Verification and analytics** | [Name, IT no.] | VerificationCheck | Delayed verification sweep; reporter confirmation; reopen rates and metrics | Verification |

The **Planner** agent, owned by [Name], coordinates every run. The agent workflow tables and
endpoints are shared by all four components.

## 1.6 Scope

**In scope:** four user roles; the full fault lifecycle, including both human pauses and the
reopen loop; the asset registry with QR stickers and service history; work order approval,
assignment, timetable-aware scheduling and completion; delayed repair verification; analytics;
and five agents with persisted state, allow-listed tools, validation and a full audit trail.

**Out of scope, by design:**

| Excluded | Reason |
|---|---|
| A chat interface | Every exchange with a reporter is a bounded form. Free text would make agent input unbounded and unauditable |
| Refresh tokens | Access tokens only (12 hours). A deactivation or role change still applies on the next request |
| Deleting records | Assets are retired and users deactivated, so history is never lost |
| Agents making decisions | Approval, cost limits and state changes are C# rules or human actions |
| Automatic escalation | The verification agent's "escalate" verdict is advice only |
| Notifications, payments, public holidays | Not needed for the core loop |

**Constraints:** the mandated stack; free-tier services only; costs in LKR as `decimal`; campus
time `Asia/Colombo`, stored in UTC; nine weeks, four developers.

---

# 2. Requirements and User Roles

## 2.1 User roles

There are four roles. Each permission is granted to named roles explicitly. Roles are not a
seniority ladder, so an Admin cannot approve a work order.

| Role | Who | Responsibilities | Client |
|---|---|---|---|
| **Reporter** | Students and staff | Report faults, answer clarification questions, confirm whether a repair held | Mobile |
| **Technician** | Maintenance staff | Work assigned jobs, read the asset history and diagnosis, complete jobs | Mobile |
| **Facilities Manager** | Head of maintenance | Approve, reject or revise work orders; assign and schedule; monitor workflows and metrics | Web |
| **Admin** | System administrator | Manage assets, buildings, rooms and user accounts | Web |

Anyone can self-register on the phone, and always as a Reporter. Staff accounts are created by
an Admin.

## 2.2 Permission matrix

✓ = allowed · *own* = their own records only · — = refused

| Capability | Reporter | Technician | Manager | Admin |
|---|:---:|:---:|:---:|:---:|
| File a report, scan QR, attach a photo | ✓ | ✓ | ✓ | ✓ |
| View reports | *own* | *own* | ✓ | ✓ |
| Answer clarification questions / confirm a repair | *own* | *own* | — | — |
| Browse assets and service history | ✓ | ✓ | ✓ | ✓ |
| Manage assets, buildings, rooms, users | — | — | — | ✓ |
| View work orders | — | assigned | ✓ | ✓ |
| Approve / reject / revise, assign, schedule | — | — | ✓ | — |
| Complete a job | — | assigned | — | — |
| View and start agent workflows | — | — | ✓ | ✓ |
| View metrics | — | — | ✓ | ✓ |

The API enforces this, and both clients hide what a role cannot do. No token returns **401**; the
wrong role, or someone else's record, returns **403**.

## 2.3 Functional requirements

**All components**
- FR-1 Sign in with email and password and receive a JWT; Reporters can self-register.
- FR-2 The main lists (assets, reports, work orders, verification checks) are paginated, searchable, filterable and sortable on the server.
- FR-3 Every screen shows loading, empty, error and success states.

**A. Assets and orchestration**
- FR-A1 Admins register, edit and retire assets. Each asset has a unique, permanent QR tag.
- FR-A2 Any user can scan a sticker to see the asset and its full service history.
- FR-A3 The system computes each asset's failure summary (repeat failure, warranty cover).
- FR-A4 Filing a report starts an agent workflow in the background without making the user wait.

**B. Reporting and clarification**
- FR-B1 A user files a report with a description and room, plus an optional scanned asset and photo.
- FR-B2 The reporter answers at most two questions using a yes/no toggle, a picker or short text.
- FR-B3 The API rejects any answer outside its allowed options or length.
- FR-B4 Reporters follow each report's progress stage on the phone.

**C. Work orders, approval and scheduling**
- FR-C1 The agent's proposal is raised as a work order through the approval gate.
- FR-C2 Orders above the threshold, or replacements, wait for a manager's decision.
- FR-C3 Managers approve, reject with a reason, or request revision with a note.
- FR-C4 Managers assign a technician and book a slot that avoids classes and clashes.
- FR-C5 The technician completes the job with outcome, cost, note and photo, which adds to the service history.

**D. Verification and analytics**
- FR-D1 Five days after completion, the reporter is asked whether the repair held.
- FR-D2 "Yes" closes the case. "No" reopens it for a new diagnosis.
- FR-D3 The verification agent's opinion is recorded beside the reporter's answer.
- FR-D4 Managers see confirmation and reopen rates, clarification figures and repeat-failure assets.

**Agentic workflow** (detailed in Chapter 3)
- FR-AG1 Each run receives an objective, creates a structured plan and delegates to distinct agents.
- FR-AG2 Agents use only allow-listed tools; every output is schema-validated.
- FR-AG3 High-cost work pauses for a manager's approval.
- FR-AG4 State, steps, tool calls, errors and decisions are persisted and shown as an audit trail,
  with each agent's latency, retries and token use monitored across runs.

## 2.4 Key business rules (deterministic, in C#)

| Rule | Value |
|---|---|
| Approval required | Estimate **above Rs 15,000**, or any replacement |
| Repair SLA | 7 days from approval |
| Repeat failure | 3 or more visits in 90 days |
| Verification | Reporter asked 5 days after completion |
| Working hours | Weekdays 08:00–17:00 (Asia/Colombo), 15-minute buffer around classes |
| Clarification | At most 2 questions; short answers of up to 100 characters |
| Completion note | At least 20 characters |
| Report photo | JPEG or PNG, up to 5 MB, location metadata removed |

## 2.5 Non-functional requirements

| Category | Requirement |
|---|---|
| Security | JWT authentication, hashed passwords, role-based policies, rate-limited sign-in and report filing, secrets in configuration only, no request bodies in logs |
| Mobile security | Token stored in secure storage (Keychain or encrypted storage) |
| Agent isolation | Agent service has no database access; API ↔ agent traffic needs a shared secret |
| Responsiveness | No request waits for an agent; agents run in the background |
| Reliability | Timeouts on every external call; one LLM retry, then a recorded safe failure; unfinished runs resume after a restart |
| Data integrity | Money as `decimal`; foreign keys, unique and CHECK constraints; transactions for multi-step writes |
| Auditability | Every agent run, tool call and approval decision is recorded |
| Usability | Responsive layouts, accessible focus states, clear loading and error states, no chat interface |
| Quality | Automated tests for every layer, run by GitHub Actions on every push |

## 2.6 The main cross-platform workflow

| Step | Who and where | What happens |
|---|---|---|
| 1 | Reporter, **Flutter** | Files a report and scans the projector. The API saves it and returns at once |
| 2 | Agents, via **API** | Planner plans; Clarifier asks two questions; the run pauses |
| 3 | Reporter, **Flutter** | Answers the questions; diagnosis and a proposal follow, and the approval gate routes it to a manager |
| 4 | Manager, **React** | Approves (or rejects or revises), assigns a technician, books a free slot |
| 5 | Technician, **Flutter** | Completes the job; the service history is updated |
| 6 | Reporter, **Flutter** | Five days later confirms the fix, which closes the case, or reports it failed, which re-diagnoses it |

At every step the data is stored in PostgreSQL, and the reporter sees the updated status on the
phone.
