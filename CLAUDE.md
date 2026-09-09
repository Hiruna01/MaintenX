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
- Test database is **SQLite in-memory, not the EF Core in-memory provider.** The EF
  in-memory provider does not enforce unique indexes/constraints, so a test like
  "duplicate email returns 409" would pass even if the unique index were deleted. SQLite
  enforces it for real.
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
