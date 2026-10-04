# 3. Full-Stack and Agentic AI Architecture

This chapter describes how MaintenX is built: the components and how they connect, how
responsibility is divided between the AI agents and deterministic code, and how the agentic
workflow plans, uses tools, validates, pauses for approval and records what it did.

---

## 3.1 System overview

MaintenX has one public API, one internal agent service, one database and two clients
(Figure 3.1).

![Figure 3.1](diagrams/system-architecture.png)

*Figure 3.1 — System architecture: components, connections, protocols and authentication.*

| Component | Technology | Responsibility |
| --- | --- | --- |
| Mobile app | Flutter, Riverpod, go_router | Reporters and technicians: file a report (QR scan, photo), answer questions, complete jobs, confirm repairs |
| Web app | React 18, Context API, React Router | Managers and admins: report intake, approval queue, dispatch and scheduling, asset registry, users, workflow monitoring, metrics |
| API | ASP.NET Core (.NET 8), EF Core | Every business rule, authentication and authorisation, persistence, workflow orchestration, the approval gate, the audit trail |
| Agent service | Python, FastAPI, LangGraph | Five AI agents that return **advice** |
| Database | PostgreSQL | The system of record (15 tables) |
| External services | Supabase Storage, Google Calendar, an OpenAI-compatible LLM | Photos, the campus timetable, model inference |

**Connections and security.**

| From → To | Authentication |
| --- | --- |
| Flutter / React → API | JWT bearer token (12-hour lifetime; role and account re-checked on every request). Sign-in and report filing are rate-limited |
| API → PostgreSQL | Connection-string credentials held only by the API |
| API → Agent service (`POST /run`) | Shared secret header (`X-Agent-Secret`) |
| Agent service → API (tool calls) | The same shared secret |
| API → Supabase / Google Calendar | Service-role key / Google service account (read-only) |
| Agent service → LLM provider | API key |

Three rules define the trust boundary:

1. **The clients talk only to the API.** Neither the React nor the Flutter app knows the agent
   service exists.
2. **The agent service has no database credentials.** It can read campus data only by calling
   a fixed list of read-only tools on the API.
3. **Every endpoint requires a signed-in user by default** (a fallback authorisation policy). Only
   login, register, the health check and the secret-protected tool router are anonymous.

---

## 3.2 The core principle: agents advise, C# decides

The agents read unstructured text (a reporter's description, technicians' notes) and produce
advice. **Every decision that changes the system's state is made by deterministic C# code or by
a person**, never by a model.

| Decision | Made by |
| --- | --- |
| Which agents run, and whether to ask the reporter questions | Planner agent proposes; C# validates the plan before it is used |
| What to ask the reporter | Clarifier agent (at most two bounded questions) |
| Whether an answer is acceptable | C# |
| Likely cause of the fault | Diagnostic agent (advice) |
| Proposed repair strategy and cost | Strategist agent (advice) |
| **Whether a work order needs a manager's approval** | **C#**: estimate above the threshold (Rs 15,000) or a replacement |
| Approve, reject or request revision | A Facilities Manager |
| Workflow state transitions | C#: a fixed transition table |
| Repeat failures, warranty, SLA, free time slots | C# |
| Whether a repair held | The reporter's yes/no answer |
| Whether a repair held, in the agent's opinion | Verification agent (recorded as advice; nothing acts on it) |

This keeps every rule that costs money or affects people auditable and testable, and limits
what a mistaken or manipulated model could do.

---

## 3.3 Full-stack layering

**API.** A single ASP.NET Core project organised by layer: thin **controllers** (receive,
call one service, return a status code), **services** (interface + implementation, constructor
injection, `AddScoped`, all business rules), **DTOs** (`record` types with validation; entities
are never returned), **models** and **data** (EF Core `DbContext`, migrations, seed data).
Global error handling returns ProblemDetails, Serilog logs requests without their bodies, and
Swagger documents every endpoint. Status codes are consistent: 200, 201, 202 (a started
workflow), 204, 400, **401 (no valid token) and 403 (wrong role or someone else's record)**,
404 and 409 (a conflict with the current state).

**Web client.** Organised by feature, with UI, hooks and services kept separate. Components never
call `fetch`. They read data through a `useFetch` hook, and every page renders loading, empty
and error states. React Context holds the signed-in user, `ProtectedRoute` mirrors the API's
role policies, and a 401 ends the session automatically.

**Mobile client.** The same separation: screens call a feature API class, which calls one
`ApiClient`. Riverpod holds app-wide state, and go_router's single redirect is the route guard.
The token is kept in **`flutter_secure_storage`** (iOS Keychain or Android encrypted storage),
and a 401 signs the user out automatically. Device features: QR scanning, camera and gallery.

**One API, two clients.** Both apps use the same endpoints, token, roles and business rules.
Neither client computes a business rule; thresholds, warranty and SLA status are all read
from the API.

---

## 3.4 Non-blocking orchestration

An agent run can take tens of seconds, so **no HTTP request ever waits for an agent**. Filing a
report saves the report and its workflow in one transaction, puts the workflow id on an
in-process queue, and returns **201** immediately. A background worker, `WorkflowRunner`, takes
ids off the queue, calls the agent service, and records each agent's result as it arrives. The
phone polls the report until questions appear.

| Background worker | Purpose |
| --- | --- |
| `WorkflowRunner` | Runs the agents for each queued workflow |
| `VerificationSweepService` | Asks reporters "is it fixed?" five days after a repair |
| `VerificationAgentRunner` | Sends each repair check to the verification agent |
| `TimetableSyncWorker` | Mirrors the campus timetable from Google Calendar |

**Reliability.** The agent call has a timeout of 360 s, budgeted above the worst case. A
timeout, an unreachable service or a bad reply never throws: the workflow is marked `Failed`
with the reason recorded. Because the queue is in memory, unfinished workflows are re-queued
when the API starts, so a restart never leaves a run stuck.

---

## 3.5 The agentic workflow

### 3.5.1 Five distinct agents

Each agent has its own responsibility, input and output schema, prompt file and tool
permissions (Figure 3.2).

| Agent | Responsibility | Output | Tools |
| --- | --- | --- | --- |
| **Planner** | Read the objective and produce a 2–3 step plan; decide whether clarification is needed | `steps` (agent and purpose), `rationale` | none |
| **Clarifier** | Ask at most two bounded questions that would change the repair | 0–2 questions (yes/no, picker or short text) | room, asset |
| **Diagnostic** | Identify one to three likely causes from the asset's service history, with evidence | hypotheses with confidence and evidence, a next action | asset, service history, related open reports |
| **Resolution Strategist** | Propose one repair strategy with a cost estimate | strategy, estimated cost, urgency, justification | asset, service history, open work orders |
| **Verification** | Judge whether a completed repair held | confirm / reopen / escalate, with evidence | work order, service history, related open reports |

### 3.5.2 Planning and delegation

Every new report run starts with the **Planner**. Its plan is checked twice: once by the agent's
own schema, and again in C# before it is stored. Agents must come from the pipeline, in order,
with no repeats, and the diagnostic and strategist must always be present. An invalid plan is
replaced by a safe default plan (every agent), and the planner's original reply is kept for the
audit trail. The graph then **delegates according to the plan**: to the clarifier if the plan
includes it, otherwise straight to the diagnostic. All routing is plain Python reading validated
data; no routing decision comes from a model's free text.

| The run starts from | Route |
| --- | --- |
| A new report | plan → (clarify) → diagnose → strategize |
| The reporter's answers, or a repair reopened | diagnose → strategize |
| A manager's revision note | strategize only |
| A completed repair to verify | verify only |

![Figure 3.2](diagrams/agent-graph.png)

*Figure 3.2 — The agent graph (LangGraph): five agent nodes and the routing rules between them.*

### 3.5.3 Controlled tools

Agents read data only through **seven read-only tools** on the API. The allow-list is a
**hardcoded dictionary in C#**, so nothing a model outputs can add or describe a capability.
Each agent can use only its own subset (least privilege), and every agent's subset is different.
Tool calls need the shared secret, take only a validated id, and return facts (a record, a
list, or "not found"), never judgements. There is deliberately no tool that approves or raises a
work order. Every tool call, including a rejected one, is recorded.

### 3.5.4 Validation and safe failure

- **Structured output.** Every reply is parsed and validated against a strict Pydantic schema.
  Unknown fields are rejected, and no schema has a free-text message or an approval field.
- **One retry, then safe failure.** An invalid reply is retried exactly once with the validation
  error. After that, the agent returns a recorded *safe failure* rather than an error. Every
  model call has a 30 s timeout.
- **Business rules in C#.** A proposal becomes a work order only if its strategy and estimate are
  valid. The approval gate, the state machine and the answer checks are all deterministic code.
- **Prompt-injection defence.** User-typed text reaches a model only as one JSON-encoded data
  block, which it cannot break out of, and the prompts tell the model it is data. Because a
  model can still be persuaded, nothing an agent produces can move a workflow or approve an
  order.

---

## 3.6 State machine and human approval

The workflow's state changes only through a **fixed transition table in C#** (Figure 3.3),
keyed by *(state, event)*, so that, for example, a second order raised on a report can never be
mistaken for a manager's approval. An illegal transition returns **409** and writes nothing.

![Figure 3.3](diagrams/workflow-state-machine.png)

*Figure 3.3 — The workflow state machine (simplified: the main path and the two human pauses).
Any agent stage can also end in `Failed` with the reason recorded, after which a manager can
still raise a work order.*

**Two human pauses:**

| Pause | Who | Action |
| --- | --- | --- |
| Clarification | The reporter (Flutter) | Answers the bounded questions in one form; the run resumes at diagnosis |
| **Approval (the high-impact action)** | A Facilities Manager (React) | **Approve**, **reject** with a reason, or **request revision** with a note |

**The approval gate.** When the strategist proposes a repair, the runner raises a work order
through the same C# gate a manager's order goes through. An estimate **above Rs 15,000**, or any
**replacement**, waits for a manager. Anything else is approved automatically. A revision
re-runs the strategist alone with the manager's note and resubmits the same order through the
same gate. Each decision is recorded on the audit trail with who made it, and a decision cannot
be made twice (the order is claimed with a conditional update).

**Closing the loop.** After a repair, the reporter is asked whether it held. "Yes" closes the
workflow. "No" sends it back to **diagnosis**, where the agents now see the failed repair in the
service history. Throughout, the report's status follows the workflow, so the reporter always
sees the current stage on the phone.

Figure 3.4 traces one fault through the whole system: a report filed on Flutter, the agents
running in the background, the manager's approval on React, the technician completing the job
on Flutter, and the reporter's answer that closes or reopens the case.

![Figure 3.4](diagrams/e2e-sequence.png)

*Figure 3.4 — One fault end to end: report, plan, clarify, diagnose, propose, approve, repair,
verify and reopen.*

---

## 3.7 Shared state and observability

All workflow state is stored durably in PostgreSQL (the full schema is in Chapter 4, Figure 4.1):

| Required state | Stored in |
| --- | --- |
| Workflow id, objective, current state, final outcome | `AgentWorkflows` |
| The structured plan and each step's status | `AgentWorkflows.PlanJson` (`jsonb`) |
| Each agent run, with its output, timing, retries, validation result and the tokens the provider reported | `AgentSteps` |
| Each tool call and its result | `AgentSteps` (written by the tool router) |
| Errors and safe failures | `AgentSteps.ErrorMessage`, `AgentWorkflows.Outcome` |
| Approval decisions | `WorkOrders` (status, approver, reason, note) and `approval` steps |

No hidden reasoning, passwords, access tokens or personal details in tool results are stored. The
agent's original reply is kept unedited as the audit copy, separate from the working data the
application uses.

**Observability.** The web app's workflow page shows the plan, every agent run and tool call in
order (with outcome, duration, retries and a one-line summary, and the raw data one click
away), every approval decision, and a side-by-side view when a fault has been diagnosed twice.
An **Agent monitoring** page adds the view across all runs: for each agent, its runs, failure
and retry rates, median and p95 latency and token use, tokens per day, an estimated cost at a
configured price, and the slowest and most expensive runs, each linked to its full trace. Every
figure is counted by the API from these same rows.

---

## 3.8 Meeting the minimum agentic workflow

| Specification requirement | How MaintenX meets it |
| --- | --- |
| Domain objective | The report's description |
| Structured multi-step plan | Planner agent, validated in C#, stored as `PlanJson` |
| Distinct agent roles | Five agents with separate responsibilities, schemas and tools |
| Allow-listed tools with validated input and structured output | Seven read-only tools, hardcoded in C#, protected by a shared secret |
| Persisted workflow state | `AgentWorkflows`, `AgentSteps` |
| Deterministic validation | Pydantic schemas, plan rules, the approval gate, the state machine |
| Human approval of a high-impact action | Work orders above Rs 15,000 or replacing equipment |
| Auditable result or safe failure | Every step recorded; failures end in `Failed` with the reason |

---

## 3.9 Known limitations

- **Escalation is not automated.** The verification agent can say `escalate`, but nothing acts on
  it yet. Whether escalation should be a rule or a manager's call is undecided.
- **No "start job" action.** Jobs go straight from approved to completed.
- **Consolidated jobs are advisory.** The strategist can suggest combining visits, but orders are
  not linked automatically.
