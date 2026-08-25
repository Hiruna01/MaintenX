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

## TESTING

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

```
web/src/components/                    shared reusable UI
web/src/features/<name>/components/
web/src/features/<name>/hooks/
web/src/features/<name>/services/
web/src/features/<name>/pages/
web/src/routes/
```

- **JavaScript (`.jsx`), not TypeScript.**
- **Data fetching goes through a `useFetch` hook** returning `{ data, isLoading, error }`,
  and **every page renders all three states**. A blank screen while loading is a bug.
- **Search inputs use a `useDebounce` hook.**
- **Forms use controlled inputs** with a `validate()` function returning per-field errors.
- **State:** `useState` locally; **Context** for app-wide auth/session.
  **No Redux, no Zustand, no TanStack Query — this is a locked ADR decision.**

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
