# Test evidence — 30 September 2026

Every file in this folder was produced on 30 September 2026 from `main` at `dca3c36` (with the SLA and approval work of PR #101 merged), plus this change's React tests and eval recorder, on a MacBook Air (macOS). Nothing was edited by hand after it was generated. The log filters only removed ASP.NET's "Failed to determine the https port" host warnings, which are not test results.

## Summary

| Layer | Tool | Result | Evidence file |
|---|---|---|---|
| Backend (API + database), SQLite mode | xUnit, `WebApplicationFactory` | **755 / 755 passed** (33 s) | `api-tests-sqlite-2026-09-30.txt` |
| Backend (API + database), **PostgreSQL** mode — runs every EF migration | xUnit against local PostgreSQL 18 | **755 / 755 passed** (48 s) | `api-tests-postgres-2026-09-30.txt` |
| Agent service (deterministic, `STUB_MODE`, sockets blocked) | pytest | **243 / 243 passed** | `agent-tests-2026-09-30.txt` |
| **Agent live evaluation** (real LLM) | pytest, `RUN_LIVE_EVALS=1` | **18 / 18 passed**, 0 retries, 0 safe failures | `LIVE_EVALS_2026-09-30.md` + `live-evals-2026-09-30-*` |
| React web client | Vitest 5 + Testing Library, jsdom | **47 / 47 passed** | `web-tests-2026-09-30.txt` |
| Flutter mobile client | `flutter analyze`, `flutter test` | **No issues; 114 / 114 passed** | `mobile-tests-2026-09-30.txt` |

**Environment:** .NET SDK 8.0.423 · Node 26.5.1 · Python 3.14.6 · Flutter 3.47.2 (stable) · PostgreSQL 18 (local) and PostgreSQL 16 (CI service container) · LLM `google/gemini-3.8-flash`.

**CI** (`.github/workflows/ci.yml`, every push and PR to `main`): api (build, then xUnit on a PostgreSQL 16 container with migrations), agent (pytest, `STUB_MODE`), web (lint, `npm test`, build) and mobile (`flutter analyze`, `flutter test`). The live evals are deliberately not in CI, because they cost money.

## Reproduce

From the repository root:

```bash
dotnet test api.Tests/api.Tests.csproj
```
```bash
TEST_DATABASE_URL="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD" dotnet test api.Tests/api.Tests.csproj
```
```bash
cd agent && STUB_MODE=1 pytest -v
```
```bash
cd agent && RUN_LIVE_EVALS=1 pytest evals/ -v
```
```bash
cd web && npm test
```
```bash
cd mobile && flutter analyze && flutter test
```

## Coverage against the spec's testing requirements (§12)

| §12 area | Required evidence | Where it is |
|---|---|---|
| **Backend** | Unit, service-layer, validation, auth/authz, controller, API integration | 755 xUnit tests. Pure rules: `SlotRulesTests`, `PlanRulesTests`, `SlaTests`. Services: `VerificationTests`, `VerificationSweepTests`. Auth: `AuthTests`, `UserManagementTests` (401 vs 403, fallback policy). Endpoints through the real pipeline: `WorkOrderEndpointTests`, `ReportTests`, `AssetTests` and others. |
| **Database** | PostgreSQL integration, constraints, migrations, transactions | The PostgreSQL run applies every migration to a fresh database per test class. Constraints: `WorkOrderTests` (CHECK constraints, unique `ExternalEventId`, exact decimals). Transactions: `Complete_WhenAWriteFailsAfterTheFirstSave_LeavesNothingBehind`, `CreateReport_WhenItsWorkflowCannotBeSaved_FilesNothing…`, `ADecisionThatLosesTheRace_WritesNothing`. |
| **React** | Component, form validation, protected route, API integration, error states | `SlaPill.test.jsx` (component); `LoginPage.test.jsx`, `workOrderValidation.test.js` (forms); `ProtectedRoute.test.jsx`, `Sidebar.test.jsx` (routes and role navigation); `useFetch.test.jsx`, `apiClient.test.js` (API integration); `WorkflowsPage.test.jsx` (loading, error, empty and data states). |
| **Flutter** | Unit, widget, form validation, navigation, API integration | 114 tests in `mobile/test/`: registration, reports and the clarification form, asset scan (camera permission denied, unknown tag, no network), work orders and completion, verification. |
| **End to end** | One full client → API → database → agent workflow | `WorkflowEndToEndTests` (API suite): report → clarification → gate → sweep → reporter answer → Closed or re-diagnosed, with every state asserted. It uses a scripted agent; the live agent behaviour is covered by the evals. |
| **Agent evaluation** | Golden case, planning, tools, structured output, validation, business rules, approval, injection, recovery, safe failure | `LIVE_EVALS_2026-09-30.md`, including its own coverage table. |
| **Performance** | Concurrency, response time, success rate, DB and agent latency | **Not in this folder.** Agent latency per call is recorded above (4.8–20.4 s, median 9.7 s); a load test of the API is still to be done. |

## "Tests that can fail": verified by breaking the rule

A test that has never failed may not test anything. For these, the rule was broken on purpose, the test was run and seen to fail, and the code was restored byte-identical.

| Broken on purpose | Tests that failed |
|---|---|
| The approval "claim" removed (always wins) | 3: `ApprovalTests.ADecisionThatLosesTheRace_WritesNothing` (approve, reject, revision) |
| SLA overdue check `>` changed to `>=` | 4: `SlaTests` boundary cases |
| SLA not started on manager approval | 1: `SlaTests.AnOrderWaitingOnAManager_…` |
| React: role check removed from `ProtectedRoute` | 2 |
| React: role filter removed from `Sidebar` | 4 |
| React: `validate()` skipped on the login form | 2 |
| React: a 401 no longer ends the session | 1 |
| React: the loading skeleton removed | 1 |

CLAUDE.md records the same exercise for many earlier API rules ("verified to fail with …").
