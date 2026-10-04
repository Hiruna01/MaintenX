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

**Added 3 October 2026** (`LIVE_EVALS_2026-10-03.md`): the live evals again, now with the tokens the provider reported (run 1, 18 / 18); three new **indirect** prompt-injection cases, where the instruction arrives inside a technician's note read through a tool (run 2, 21 / 21); every injection case repeated to **5 runs each (45 / 45)**; and a **regression check** showing a bad prompt edit caught by the evals (3 / 3 runs) and a deleted rule caught by CI. The suites were re-run the same day, after the token tracking, the indirect-injection tests and the rate limits were added:

| Layer | Result | Evidence file |
|---|---|---|
| Backend, SQLite mode | **770 / 770 passed** (34 s) | `api-tests-sqlite-2026-10-03.txt` |
| Backend, **PostgreSQL** mode (all 18 migrations applied) | **770 / 770 passed** (53 s) | `api-tests-postgres-2026-10-03.txt` |
| Agent service (deterministic) | **259 / 259 passed** | `agent-tests-2026-10-03.txt` |
| React web client | **60 / 60 passed** | `web-tests-2026-10-03.txt` |
| Flutter mobile client | **No issues; 114 / 114 passed** | `mobile-tests-2026-10-03.txt` |

**1,203 automated tests** in all. The 30 September files above are kept unchanged as the earlier record.

**Added 4 October 2026**, for the Agentic AI Evaluation Report (Chapter 9):

| What | Result | Evidence file |
|---|---|---|
| **Safe failure, live**: all 21 evals run with a deliberately invalid `LLM_API_KEY` | **22 / 22 calls ended as safe failures** (401, retried once, no output, reason recorded, 0.3–0.4 s); every eval failed on its `status is ok` assertion, none on an exception; the key appears nowhere | `live-evals-2026-10-04-safe-failure-*` |
| **Live end-to-end workflows** read back from the local database | #18 (30 Sep): planner → clarifier → diagnostic → strategist → approval required → manager approved → completed → reporter yes → verification agent `confirm` → Closed. #20 (1 Oct): agent service down → `CallFailed`, workflow `Failed` with the reason. #22 (3 Oct): under threshold, auto-approved, 10,921 tokens | `live-workflows-2026-10-04.txt` |

**Environment:** .NET SDK 8.0.423 · Node 26.5.1 · Python 3.14.6 · Flutter 3.47.2 (stable) · PostgreSQL 18 (local) and PostgreSQL 16 (CI service container) · LLM `google/gemini-3.8-flash`.

**CI** (`.github/workflows/ci.yml`, every push and PR to `main`): api (build, then xUnit on a PostgreSQL 16 container with migrations), agent (pytest, `STUB_MODE`), web (lint, `npm test`, build) and mobile (`flutter analyze`, `flutter test`). The live evals are deliberately not in CI, because they cost money.

**Performance, 4 October 2026** (`performance-2026-10-04/`, scripts in `docs/performance/`), for the Performance Report (Chapter 10). Release-build API against a copy of the seeded database, k6 2.3.0 on the same Apple M5 laptop:

| What | Result | Files |
|---|---|---|
| Baseline, one user, 100 requests per endpoint | median 0.1–4.2 ms, 0 failures | `baseline-summary.json` |
| Concurrency 1 / 10 / 25 / 50 / 100 / 200 users, 60 s each | peak ~6,000 req/s; **0 failures of 1,844,476**; p95 124 ms at 200 users | `load-*vus-summary.json` |
| Sign-in at 1 / 10 / 50 users | ~30 ms each alone, ~230 /s max, 0 failures | `login-*vus-summary.json` |
| Rate limits at their real values | 11th sign-in and 11th report → 429 in under 2.2 ms | `rate-limit-check.txt` |
| Database: EXPLAIN ANALYZE, with and without sequential scans | 0.006–0.10 ms; every lookup can use its index except the asset list sorted by name | `explain-analyze*.txt` |
| Database under 25-user load (query logging on) | 343,068 queries: p50 1 ms, p95 5 ms, p99 11 ms; 25% are the session check | `db-command-times-25vus.json` |
| Report burst, stub (20) and real model (5) | all 201 within 139 ms; stub queue drained in 337 ms; real: 5th report's questions after 89 s | `burst-*` |
| Deployed check (curl) | cold start API 33.5 s, agent 42.6 s; warm ~250 ms | `deployed-check.txt` |

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
