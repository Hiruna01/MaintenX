# Performance tests

k6 scripts for the Performance Report (Chapter 10). Results of the 4 October 2026 run are in
`docs/report/evidence/performance-2026-10-04/`.

| Script | What it measures |
|---|---|
| `k6/baseline.js` | Each read endpoint alone, one user: the fastest it can answer |
| `k6/load.js` | `VUS` users sending a weighted mix of reads back to back for `DURATION` |
| `k6/login.js` | Sign-in under concurrency (PBKDF2 hashing is slow on purpose) |
| `k6/report-burst.js` | `N` reports filed at the same instant; the API must answer without waiting for the agents |

## Running them

Test a **copy** of the database, not your dev database: the burst files real reports.

```bash
createdb maintenx_perf && pg_dump --no-owner campusfacilities | psql -q maintenx_perf
```

Run the API in Release mode against the copy on port 5199, with the rate limits raised (every
load test signs in through the rate-limited endpoint once, and the burst files more than 10
reports an hour):

```bash
dotnet publish api/CampusFacilities.Api.csproj -c Release -o /tmp/maintenx-api
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://localhost:5199 \
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=maintenx_perf;Username=…;Password=…" \
Jwt__Secret=… Jwt__Issuer=… Jwt__Audience=… Agent__SharedSecret=… Agent__BaseUrl=http://localhost:8001 \
RateLimiting__AuthAttemptsPerMinute=100000 RateLimiting__ReportsPerHour=100000 \
dotnet /tmp/maintenx-api/CampusFacilities.Api.dll
```

Run the agent on port 8001 (`STUB_MODE=true` for a free burst, unset for the real model):

```bash
cd agent && STUB_MODE=true API_BASE_URL=http://localhost:5199 uvicorn main:app --port 8001
```

Then, from `docs/performance/k6/`, with the seed passwords in `MX_PW_REPORTER`,
`MX_PW_TECHNICIAN`, `MX_PW_FACILITIESMANAGER` and `MX_PW_ADMIN`:

```bash
k6 run -e ITERATIONS=100 baseline.js
k6 run -e VUS=50 -e DURATION=60s load.js
k6 run -e VUS=10 -e DURATION=30s login.js
k6 run -e N=20 -e TAG=stub report-burst.js
```

How long each burst report waited for its agents is read from the database afterwards: the
first tool-call step and the last step of each workflow, against the workflow's `CreatedAt`.

## What the numbers are not

- The data is the seeded demo estate (25 reports, 8 assets), so database time is best case.
- k6 runs on the same machine as the API and the database, so they share its CPU.
- The deployed system was only checked with a few unauthenticated requests: it runs on free
  plans that are not meant to be load-tested.
