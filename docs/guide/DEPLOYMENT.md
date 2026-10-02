# Deployment — free tier, end to end

Everything here is on a free plan. Nothing secret is in the repo: every value below is a
NAME, and the value lives in the host's environment (Render, Vercel, GitHub).

```
Browser ─► Vercel: React build (VITE_API_BASE_URL baked in at build time)
Phone APK ─┐            │
           └──────────► Render "maintenx-api" (Docker, .NET 8) ──► Supabase project A: Postgres (session pooler)
                          │  ▲                                  ──► Supabase project B: Storage "photos"
          POST /run       │  │ /api/internal/tools/*            ──► Google Calendar
          + X-Agent-Secret▼  │ + X-Agent-Secret
                        Render "maintenx-agent" (Python) ──► OpenRouter
GitHub Actions "Deploy" ── EF migrations ──► Supabase project A   (the ONLY route a migration has)
GitHub Actions "Mobile release" ── tag mobile-v* ──► GitHub Release with the APK
```

- The clients talk only to the API. The agent is reached only by the API, over its public
  `onrender.com` URL, with `AGENT_SHARED_SECRET` in both directions. Render's free instances
  do not take private-network traffic, so the secret is the boundary (both sides fail closed).
- The agent holds no database credential: its Render service has none to read.
- Files: `render.yaml` (both Render services), `api/Dockerfile`, `web/vercel.json` (SPA
  rewrite), `.github/workflows/deploy.yml` (migrate, then deploy), `.github/workflows/mobile-release.yml`,
  `.config/dotnet-tools.json` (pins `dotnet-ef` for CI).

## Supabase — two projects, two jobs

| | Project A — **new**, the database | Project B — **existing**, the photos |
|---|---|---|
| Used for | Postgres only (every table) | Storage only (the public `photos` bucket) |
| Region | Southeast Asia (Singapore), beside Render's `singapore` | unchanged — wherever it is |
| API reads it through | `DATABASE_URL` | `SUPABASE_URL`, `SUPABASE_SERVICE_KEY`, `SUPABASE_STORAGE_BUCKET` |
| Changed by this deployment | created, Data API off, migrated by CI | **nothing** |

Splitting them costs nothing: the API already treats the two as unrelated settings. The
database sits next to the API because a request makes several queries; a photo is uploaded
once and then loaded straight from its public URL, so the bucket's distance hardly matters.

### Project A — create the database project

1. **New project** (supabase.com → your organisation → New project).
   - Name: `maintenx-prod`.
   - **Database password: letters and digits only, 24+ characters.** A `;` or `=` breaks
     the Npgsql string below. Keep it in your password manager; it is not shown again (it
     can be reset under Project Settings → Database).
   - **Region: Southeast Asia (Singapore)** — `render.yaml` already says `singapore`. Supabase
     has a Mumbai region but Render does not; what matters is the API-to-database hop.
   - Plan: Free. The free plan allows **two active projects per organisation** — with
     project B that is both of them.
   - If the form offers **security / Data API options**, choose to **not expose the `public`
     schema** (or disable the Data API). Otherwise do it in step 2.
2. **Turn the Data API off** once the project is ready: Project Settings → **Data API** →
   disable it (or remove `public` from "Exposed schemas"). EF creates its tables in `public`
   with no row-level security; with the Data API on, anyone holding the project's anon key
   could read `Users` — password hashes included — over REST. Nothing in MaintenX uses the
   Data API.
3. **Leave the database empty.** Do not create tables, enable extensions or run SQL. CI
   creates the whole schema (step 3 of First deployment). The Security Advisor will flag
   "RLS disabled" on every table afterwards — expected, and harmless with the Data API off.
4. **Copy the session-pooler details**: the **Connect** button at the top of the project →
   **Session pooler**. You need the host, the port (5432), the database (`postgres`) and the
   user (`postgres.<project-ref>`). The dashboard shows a `postgresql://…` URI; **do not use
   it as it is** — Program.cs hands the value straight to Npgsql, which wants key/value form:

   ```
   Host=<pooler host>;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db password>;SSL Mode=Require;Maximum Pool Size=5;Timeout=30;Keepalive=30
   ```

   Copy the host exactly (`aws-0-ap-southeast-1.pooler.supabase.com` or `aws-1-…`).
5. **Store that one string twice, nowhere else**: GitHub → environment `production` → secret
   `PRODUCTION_DATABASE_URL` (First deployment step 2), and Render → `maintenx-api` →
   `DATABASE_URL` (step 4). Never in a file, never in chat.

**Why the session pooler and these settings**
- The direct host `db.<ref>.supabase.co` is **IPv6-only** on the free plan, and neither
  Render nor GitHub's runners have IPv6 — it simply does not connect from either.
- Not the transaction pooler (port 6543): it needs `No Reset On Close=true`, gives up session
  state, and buys nothing here.
- `SSL Mode=Require` — Supabase refuses unencrypted connections. `Maximum Pool Size=5` keeps
  the API inside the free pooler's client limit. `Keepalive=30` stops idle connections being
  dropped silently between requests.
- **Do not add `EnableRetryOnFailure`** to the code: the services open their own transactions
  (completion, report filing, approval decisions, the Serializable booking), and Npgsql's
  retrying strategy throws on every one of them.

**Verify project A** (after the first Deploy run has migrated it):
- Table Editor → `public` shows the MaintenX tables, and `__EFMigrationsHistory` has
  **17 rows** — one per migration in `api/Data/Migrations` (each migration is a `.cs` plus a
  `.Designer.cs`; the snapshot is not one).
- The Data API is off. To prove it, the project's anon key must NOT read a table:

  ```bash
  curl -s "https://<project-a-ref>.supabase.co/rest/v1/Users?select=Email" -H "apikey: <project A anon key>"
  ```

  Anything but a JSON list of users is right (a 404 or an error saying the schema is not
  exposed). A list of emails means step 2 did not take — fix it before going further.

**Rollback**: project A holds nothing but what CI and the seeder wrote, so a broken first
migration is undone by **deleting the project and creating it again** (then update both
copies of the connection string). Later, when it holds real data, fix forward with a new
migration and take a `pg_dump` first — the free plan has no point-in-time recovery.

### Project B — the existing photo bucket, unchanged

- `SUPABASE_URL` = project B's URL (`https://<project-b-ref>.supabase.co`).
- `SUPABASE_SERVICE_KEY` = **the same key that is in your local user-secrets** — the one
  scenarios E7–E13 verified. The API sends it as both `Authorization: Bearer` and `apikey`,
  so a legacy `service_role` key and a newer `sb_secret_…` key both work.
- `SUPABASE_STORAGE_BUCKET` = `photos` (public).
- Do not migrate into project B, and do not point `DATABASE_URL` at it.
- **Verify**: in First deployment step 7, a photo upload returns 201 with a URL on
  `<project-b-ref>.supabase.co/storage/v1/object/public/photos/reports/…`, and the URL opens
  with no sign-in.

### Keeping both projects awake

A free project is **paused after about a week with no activity**, and that now applies to two
projects.
- **A** stays busy when the API is kept warm (step 9): the hourly sweep and timetable sync
  query it. Without the pinger, open the API once every few days.
- **B** sees traffic only when photos are uploaded or viewed. Before a demo, open project B's
  dashboard: if it says **Paused**, press **Restore** (a few minutes). While B is paused,
  uploads return the API's 503 "storage unavailable" and existing photos do not load —
  nothing else breaks.

## Environment variables, by service (names only)

**Render — `maintenx-api`** (declared in `render.yaml`)

| Name | Value / source |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `DATABASE_URL` | secret — **project A**'s session-pooler string |
| `JWT_SECRET` | generated by Render |
| `JWT_ISSUER`, `JWT_AUDIENCE` | plain values in `render.yaml` |
| `AGENT_SHARED_SECRET` | generated by Render (copied to the agent automatically) |
| `AGENT_SERVICE_URL` | the agent's `https://…onrender.com` URL |
| `SUPABASE_URL`, `SUPABASE_SERVICE_KEY` | secret — **project B** (the existing photo project) |
| `SUPABASE_STORAGE_BUCKET` | `photos` |
| `GOOGLE_SERVICE_ACCOUNT_JSON_BASE64`, `GOOGLE_CALENDAR_ID` | secret — same as local |
| `Cors__AllowedOrigins__0` | the Vercel production origin, exact, no trailing slash |
| `SWAGGER_ENABLED` | `true` |
| `SEED_DEMO_DATA` | `true` |
| `Seed__Passwords__Reporter` / `__Technician` / `__FacilitiesManager` / `__Admin` | secret |

`PORT` is set by Render itself; the Dockerfile listens on it. Every other key in
`.env.example` (thresholds, SLA, verification, scheduling) keeps its default unless set.

**Render — `maintenx-agent`**

| Name | Value / source |
|---|---|
| `PYTHON_VERSION` | `3.12.7` |
| `API_BASE_URL` | the API's `https://…onrender.com` URL |
| `AGENT_SHARED_SECRET` | `fromService` → the API's value |
| `LLM_BASE_URL` | `https://openrouter.ai/api/v1` |
| `LLM_API_KEY` | secret — OpenRouter key |
| `LLM_MODEL` | `google/gemini-3.8-flash` (the model every eval was verified on) |

`STUB_MODE` must be **unset**. No `DATABASE_URL`, no `SUPABASE_*` — ever.

**Vercel — web** (Production environment): `VITE_API_BASE_URL` = the API's URL. Read at
build time: changing it needs a redeploy. Unset, the bundle silently points at localhost.

**GitHub — environment `production`** (secrets): `PRODUCTION_DATABASE_URL`,
`RENDER_API_DEPLOY_HOOK_URL`, `RENDER_AGENT_DEPLOY_HOOK_URL`.
**GitHub — repository variable** (not secret): `MOBILE_API_BASE_URL`.

## GitHub — what lives where

| Where (repo → Settings) | Name | Kind | Used by |
|---|---|---|---|
| Environments → `production` | `PRODUCTION_DATABASE_URL` | secret | `deploy.yml` → migrate |
| Environments → `production` | `RENDER_API_DEPLOY_HOOK_URL` | secret | `deploy.yml` → deploy |
| Environments → `production` | `RENDER_AGENT_DEPLOY_HOOK_URL` | secret | `deploy.yml` → deploy |
| Secrets and variables → Actions → **Variables** | `MOBILE_API_BASE_URL` | variable (not secret) | `mobile-release.yml` |

- The `production` environment's **deployment branches** are limited to `main`, so only a
  run from `main` can read the database secret. `deploy.yml` is triggered by `workflow_run`,
  which always runs from `main`, and its jobs also check that the CI run was a **push** — a
  pull request (a fork's included) can never reach production.
- `workflow_run` only fires for a workflow file **already on `main`**, so Deploy does nothing
  until the deployment PR is merged.
- `mobile-release.yml` asks for `contents: write` itself (to create the Release); the repo's
  default workflow permission stays **read**.
- A secret's value cannot be read back once saved — to change one, overwrite it.

## First deployment

Each step names what it needs first. Do them in order.

**1. Supabase (needs nothing).** Create project A and turn its Data API off, exactly as in
"Project A" above (steps 1–4). Project B needs nothing.
- *Verify:* project A is in Singapore and its Data API shows disabled; project B's `photos`
  bucket is listed as public.
- *Rollback:* delete project A.

**2. GitHub environment (needs 1).** Settings → Environments → New `production`, deployment
branches limited to `main`. Add the secret `PRODUCTION_DATABASE_URL`.
- *Verify:* the environment lists one secret.

**3. Merge the deployment PR (needs 2).** CI runs; when it passes, **Deploy** runs:
`migrate` applies every migration to Supabase, then `deploy` fails because the hook secrets do
not exist yet. That failure is expected.
- *Verify:* the `migrate` job is green, and project A passes "Verify project A" above
  (17 rows in `__EFMigrationsHistory`, the anon key reads nothing).
- *Rollback:* delete and recreate project A, update both copies of the connection string,
  fix forward. Never run `dotnet ef` against it from a laptop.

**4. Render Blueprint (needs 3 — the schema must exist before the API's first start).**
Render → New → Blueprint → this repo. It reads `render.yaml`, creates both services and asks
for every `sync: false` value. The two `onrender.com` URLs are `https://<service name>.onrender.com`
unless the name is taken; if Render changes one, correct `AGENT_SERVICE_URL` / `API_BASE_URL`
afterwards. For `Cors__AllowedOrigins__0`, enter the Vercel URL you expect; step 6 corrects it.
Then copy each service's **Deploy Hook** (Settings) into the `production` environment as
`RENDER_API_DEPLOY_HOOK_URL` and `RENDER_AGENT_DEPLOY_HOOK_URL`, and re-run **Deploy** by hand.
- *Verify:*
  - `GET https://<api>/health` and `GET https://<agent>/health` → 200 (allow a minute on the
    first request — that is the cold start).
  - The API log shows the seeder ran, `Timetable sync reads Google Calendar … as …`, and none
    of the "No agent shared secret / No agent service URL / No Supabase Storage" warnings.
  - `https://<api>/swagger` loads; `POST /api/auth/login` as `admin@campus.test` returns a token.
  - `POST https://<agent>/run` with no header → **401**.
  - The re-run Deploy workflow is fully green.
- *Rollback:* service → Events → an earlier deploy → **Rollback**; or **Suspend** the service.

**5. Vercel (needs 4 — the API URL).** Add New → Project → this repo. Root Directory `web`,
framework Vite, Node.js 22.x. Environment variable `VITE_API_BASE_URL` (Production) = the API's
URL. Deploy.
- *Verify:* sign in; open `/assets`, then **reload** the page (the rewrite in `vercel.json`
  must serve it, not a 404); the browser's network tab shows requests going to `onrender.com`,
  not `localhost`.
- *Rollback:* Deployments → an earlier one → **Instant Rollback**.

**6. CORS (needs 5).** Set `Cors__AllowedOrigins__0` on `maintenx-api` to the exact Vercel
production origin (`https://<project>.vercel.app`). Saving restarts the service.
- *Verify:* the web app loads data with no CORS error in the console. Preview deployments
  (`…-git-…vercel.app`) are refused on purpose.
- *Rollback:* restore the previous value.

**7. End-to-end smoke (needs 6).** Per TESTING_GUIDE, against the deployed system:
a report filed (Swagger or the phone) reaches the clarifier and its steps appear on the web's
report page; a FacilitiesManager sees the seeded approval queue, syncs the timetable and finds
slots; a photo upload returns 201 with a `supabase.co` URL; "Run sweep now" on Verifications
answers.
- *Rollback:* per failing stage above.

**8. APK (needs 4).** Set the repository variable `MOBILE_API_BASE_URL` to the API's URL, then:

```bash
git tag mobile-v0.1.0 && git push origin mobile-v0.1.0
```

- *Verify:* the **Mobile release** workflow is green; the Release has `maintenx-mobile-v0.1.0.apk`;
  installed on a phone, it signs in, scans a sticker from `docs/qr/asset-qr-sheet.png` and files
  a report with a photo.
- *Rollback:* delete the Release and the tag; the previous Release still stands.

**9. Keep the API warm (optional, needs 4).** A free external cron (e.g. cron-job.org) calling
`GET https://<api>/health` every 10 minutes. **The API only**: one service awake all month is
~744 of the workspace's 750 free instance hours; two would be ~1,490. Keeping the API awake
also keeps its hourly sweep and timetable sync touching the database, which stops Supabase
pausing the project after a week idle. The agent still sleeps; its cold start fits inside
the API's 360 s agent timeout.

## Every later deployment

Merge to `main` → CI → **Deploy** (migrate, then the Render hooks). Vercel deploys the web
client from the same push on its own.

- Migrations run **before** the new API starts, while the old one is still serving, so a
  migration must be safe for the old code: add, don't drop or rename. A removal takes two
  releases — stop using it, then drop it.
- A deploy hook builds the latest commit on `main`, not necessarily the one CI tested. The
  `deploy-production` concurrency group stops two deploys overlapping; merge one PR at a time.
- Before a migration that rewrites data, take a `pg_dump` (reading the database is fine; only
  migrations are CI-only). The free plan has no point-in-time recovery.
