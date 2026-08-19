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
