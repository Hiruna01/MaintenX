# 5. API, React and Flutter Design

## 5.1 ASP.NET Core Web API

### 5.1.1 Structure

The API is a single ASP.NET Core (.NET 8) project, organised **by layer**:

| Layer | Responsibility |
|---|---|
| **Controllers** | Thin: receive the request, call one service, return the right status code. No business logic |
| **Services** | An interface and implementation per service (e.g. `IWorkOrderService` / `WorkOrderService`), injected through the constructor and registered `AddScoped`. All business rules live here |
| **DTOs** | `record` types. Input DTOs carry DataAnnotations validation and never an `Id`. Entities are never returned directly |
| **Models / Data** | EF Core entities, `AppDbContext`, migrations and seed data |
| **Middleware** | Global exception handling (ProblemDetails responses) and the agent shared-secret filter |

The project deliberately uses no repository layer, MediatR, AutoMapper or result wrapper.
Services use `DbContext` directly, which keeps the code simple to trace from controller to
database.

### 5.1.2 REST conventions

- Routes follow `api/[controller]`, with nouns for resources and verbs only for business actions
  (`/approve`, `/complete`, `/schedule`).
- Every action is `async` and accepts a `CancellationToken`.
- Lists use one shared `PagedResult<T>` with `page` and `pageSize`, plus search, exact filters and
  sort options. Every sort has an `Id` tiebreak, so paging is stable.
- Enums are sent and accepted as **names** (`"FacilitiesManager"`, `"AwaitingApproval"`), never
  numbers.

| Situation | Status code |
|---|---|
| Read | 200 |
| Create | 201 (`CreatedAtAction`) |
| Workflow started (work continues in the background) | 202 |
| Update, delete or action | 204 |
| Validation failure | 400 |
| No valid token | **401** |
| Wrong role, or someone else's record | **403** |
| Not found | 404 |
| Conflict with the current state (duplicate, already decided, illegal transition) | 409 |
| Too many sign-in attempts, or too many reports in an hour | 429, with `Retry-After` |

### 5.1.3 Main endpoints

There are 65 endpoints across 13 controllers, plus `/health`. The key ones per component:

| Component | Endpoint | Purpose | Role |
|---|---|---|---|
| **A. Assets** | `GET /api/assets` | Search, filter, sort, page the registry | Any signed-in user |
| | `GET /api/assets/by-tag/{tag}` | QR lookup | Any |
| | `GET /api/assets/{id}/failure-summary` | Repeat failure, warranty, failure counts | Any |
| | `POST` / `PUT` / `DELETE /api/assets/{id}` | Register, edit, **retire** (not delete) | Admin |
| **B. Reports** | `POST /api/reports` | File a report (starts the agent workflow) | Any |
| | `POST /api/reports/{id}/photo` | Attach a photo (validated, metadata removed) | The reporter |
| | `GET` / `POST /api/reports/{id}/clarifications` | Read and answer the agent's questions | The reporter |
| | `PATCH /api/reports/{id}/status` | Move along the report lifecycle | Facilities Manager |
| **C. Work orders** | `GET /api/workorders/approvals` | The approval queue, with everything needed to decide | Facilities Manager |
| | `POST /api/workorders/{id}/approve` · `/reject` · `/request-revision` | Human approval | Facilities Manager |
| | `GET /api/workorders/slots/available` | Free slots avoiding classes and clashes | Facilities Manager |
| | `POST /api/workorders/{id}/schedule` | Book a slot (re-checked) | Facilities Manager |
| | `POST /api/workorders/{id}/complete` | Complete the job (one transaction) | Assigned technician |
| **D. Verification** | `GET /api/verifications` | A reporter's checks, or all for a manager | Any (scoped) |
| | `POST /api/verifications/{id}/confirm` | "Is the problem fixed?" yes or no | The reporter |
| | `GET /api/analytics/metrics` | Reopen rates, clarification, repeat failures | Manager, Admin |
| **Agent workflow** | `GET /api/analytics/agents` | Per-agent runs, failures, retries, latency, tokens and estimated cost | Manager, Admin |
| | `POST /api/workflows`, `GET /api/workflows/{id}` | Start a run; read its state, plan and steps | Manager, Admin |
| | `POST /api/workflows/verification-sweep` | Run the verification sweep now | Facilities Manager |
| **Auth and users** | `POST /api/auth/register` · `/login` | Sign up (as a Reporter) and sign in | Anonymous |
| | `/api/users` (CRUD, deactivate, reset password) | Account management | Admin |

### 5.1.4 Security and quality

- **Authentication:** JWT bearer tokens with a 12-hour lifetime. The signing key, issuer and
  audience come from configuration. Passwords are hashed with ASP.NET Core's `PasswordHasher`.
- **Authorisation:** one policy per role, generated from the `Role` enum, and a fallback policy
  that makes every endpoint require sign-in unless it is explicitly marked anonymous. Ownership
  rules (for example, "only the reporter may answer") are checked in the services. Every request
  re-checks that the account is still active and still holds the role in its token.
- **Validation:** DataAnnotations on every input DTO (400 automatically), plus business checks in
  the services (409 for state conflicts). Money uses the decimal form of `[Range]`.
- **Errors and logging:** a global middleware returns consistent ProblemDetails. Serilog logs
  method, path, status and duration, never request bodies, so no password reaches a log.
- **Rate limiting:** ASP.NET Core's rate limiter allows 10 sign-in or registration attempts a
  minute per client address and 10 reports an hour per user, answering 429 with `Retry-After`.
- **CORS** allows only the configured web origin. **Swagger/OpenAPI** documents every endpoint
  and supports testing with a bearer token.

## 5.2 React web application

### 5.2.1 Purpose and structure

The web app is the **management side** of MaintenX, used mainly by Facilities Managers and
Admins. It is built with React 18 and Vite, in JavaScript, using functional components and hooks.
Code is organised **by feature**, and each feature separates UI, hooks and API services:

```
web/src/
  components/ui/      shared design system (buttons, panels, pills, slide-overs, states)
  components/shell/   app shell and role-based sidebar
  hooks/              useFetch, useDebounce
  services/           apiClient (base URL + JWT), tokenStore
  routes/             AppRoutes, ProtectedRoute, not-found / not-authorised
  features/<name>/    components/ hooks/ services/ pages/   (assets, reports, workorders,
                      verification, workflows, estate, users, dashboard, auth)
```

### 5.2.2 State management, data fetching and API integration

- **State:** `useState` for local state, and the **Context API** (`AuthProvider`) for the
  signed-in user. Redux and Zustand were not needed (see the ADR): server data is fetched per
  page, and the only global state is the session.
- **Data fetching:** components never call `fetch`. Feature services call the API through one
  `apiClient`, which adds the JWT. Pages read data through a `useFetch` hook that returns
  `{ data, isLoading, error }` and ignores responses that arrive after the component has
  unmounted.
- **Session handling:** a 401 on a request that carried a token signs the user out with a
  "session expired" message. A 401 at login is shown as "could not sign in".
- **The client computes no business rules.** Approval thresholds, warranty, SLA status and all
  counts and rates come from the API; the client only formats and colours them.

### 5.2.3 Routing and role-based navigation

React Router defines every route. `ProtectedRoute` requires sign-in and checks the role, using
role lists that mirror the API's policies exactly. The sidebar shows only the links a role can
use. If a user opens a page for another role, they see a clear "not authorised" page, never a
blank screen. Forms opened as slide-over panels (register or edit an asset, manage a user) are
also routes, so their URLs work directly.

### 5.2.4 Main screens

| Screen | Role | What it does |
|---|---|---|
| Dashboard | All (role-aware) | Approval queue, metrics, work orders, latest reports, repeat failures |
| Reports, report detail | Manager, Admin | Intake queue with search and filters; the agent's reasoning trail; raise a work order when the agents could not |
| **Approvals** | Facilities Manager | One card per order: the report, cost against the threshold, the agent's diagnosis and proposal, and the asset's history. **Approve / reject / request revision** |
| Work orders, work order detail | Technician, Manager, Admin | Dispatch board; assign, find a free slot, book; SLA status |
| Workflows, workflow detail | Manager, Admin | The plan, every agent run (with its time, retries and tokens) and tool call, approval decisions, and compared diagnoses |
| Assets, asset detail | All (edit: Admin) | Registry, QR label printing, service history, failure summary |
| Buildings & rooms, Users | Admin | Estate and account management |
| Verifications | Reporter, Manager, Admin | Repair checks: the technician's note, the reporter's answer and the agent's opinion |
| Metrics | Manager, Admin | Confirmation and reopen rates, trends, repeat-failure assets |
| **Agent monitoring** | Manager, Admin | Each agent's runs, failure and retry rates, median and p95 latency, tokens per day, estimated cost, and the slowest and most expensive runs linked to their traces |

### 5.2.5 Forms, lists and UI states

- **Forms** use controlled inputs and a `validate()` function returning per-field errors. The
  limits mirror the API's DTOs.
- **Lists** search on the server (debounced input), filter by status tabs whose counts come from
  the API, sort using only the API's options, and page with a shared `Pager`.
- **Every page** shows skeleton loading, empty, error and success states.
- **Design:** a shared design system (`components/ui`) with Radix UI for accessible dialogs and
  pickers, Lucide icons and Recharts for the metrics charts. Layouts are responsive to phone
  width, with visible focus rings and support for reduced motion.

## 5.3 Flutter mobile application

### 5.3.1 Purpose and structure

The mobile app is the **field side** of MaintenX: Reporters report faults and confirm repairs,
and Technicians work their jobs. It targets Android and iOS and uses the same API as the web app.

```
mobile/lib/
  core/       ApiClient, TokenStorage, env (API base URL), providers, app theme
  router/     go_router configuration with the single redirect guard
  widgets/    reusable widgets: LoadingView, EmptyView, ErrorView, form fields, cards, StatusPill
  features/<name>/   screens, an API class and models   (auth, home, reports, assets,
                     workorders, verification)
```

**Screens never make HTTP calls.** A screen calls a feature API class (`ReportsApi`,
`WorkOrdersApi`, …), which calls the single `ApiClient`.

### 5.3.2 State management, routing and secure storage

- **State:** **Riverpod** providers for app-wide state (the session, the API client), and local
  widget state for forms. No code generation.
- **Routing:** **go_router** with one `redirect` that guards every route. A signed-out user can
  reach only login and register, and a signed-in user is redirected away from them.
- **Secure token storage:** the JWT is kept in **`flutter_secure_storage`** (iOS Keychain,
  Android encrypted storage), never in plain `SharedPreferences`.
- **Session expiry:** when a request with a token returns 401, `ApiClient` clears the stored
  token. The session listens to that storage, so the router sends the user to login. No screen
  has to handle it.
- **Configuration:** the API base URL is a compile-time `--dart-define`, so no secrets ship in the
  app.

### 5.3.3 Screens

| Screen | Role | What it does |
|---|---|---|
| Login, Register | Everyone | Sign in; self-register (always as a Reporter) |
| Home | All (role-aware) | Greeting, counts and shortcuts for the user's role |
| **Report a fault** | Reporter | Description, room picker, **QR scan** to identify the asset, optional **photo** |
| My reports | Reporter | Search and filter own reports; the current stage of each in plain words |
| **Clarification form** | Reporter | The agent's questions as bounded controls (yes/no toggle, picker, short text); waits while the agents work |
| Scan asset, asset detail | All | Read a QR sticker; see the asset and its service history |
| My jobs, job detail | Technician | Assigned work orders, the asset, the room, the agent's diagnosis |
| **Complete job** | Technician | Outcome, actual cost, resolution note, optional photo |
| Pending confirmations, **Confirm fix** | Reporter | "Is the problem fixed?" yes or no, with an optional comment |

### 5.3.4 Device features, forms and states

- **QR scanning** (`mobile_scanner`) reads an asset's sticker. An unknown tag, permission denied
  and no camera are each handled, with "type the tag instead" as a fallback.
- **Camera and gallery** (`image_picker`): photos are resized and checked for type and size
  before upload. A failed upload never loses the report or the job.
- **Forms** use a `validate()` function returning per-field errors, the same pattern as the web
  app. **There is no chat interface:** every answer to the agent is a bounded control.
- **Every screen** shows loading, empty, error and success states, and the layouts are
  responsive.

## 5.4 One API, two clients

Both clients sign in against the same API, receive the same JWT, and are bound by the same role
policies and business rules. Their purposes differ: the phone is for people **at the equipment**,
and the web app for people **making decisions about it**. They meet in the main workflow: a
report filed on the phone is approved on the web, repaired on the phone, and confirmed by the
reporter on the phone, with its status visible to everyone at each step.
