# MaintenX — manual end-to-end testing guide

A step-by-step script for running the whole system locally and walking one fault from
**report → AI → approval → repair → verification**, including the "repair failed" loop.
New to the system? Read [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md) first.

> **Walked through for real on 2026-09-30** on macOS, with local PostgreSQL and a live model
> (`google/gemini-3.8-flash` via OpenRouter). Every expected result below is what actually
> came back. Record IDs (`#10`, `#8`…) will differ on your machine.

---

## 0. What you need

| Tool | Check it with |
|---|---|
| .NET 8 SDK + EF tool | `dotnet --version`, `dotnet ef --version` (install: `dotnet tool install --global dotnet-ef`) |
| Node.js 20+ | `node --version` |
| Python 3.11+ | `python3 --version` |
| Flutter (stable) + an emulator/simulator | `flutter doctor` |
| PostgreSQL running locally | `psql --version` |
| An LLM API key (OpenRouter or similar) | — you can use `STUB_MODE=true` without one, but answers will be fake |

**Optional for the main walkthrough:** Supabase (photo uploads return a clear 503 without it) and
Google Calendar (the timetable sync reports `NotConfigured`, and the slot finder still works).
Both are needed for [Scenario E](#5c-scenario-e--third-party-integrations-google-calendar-supabase),
which checks the two integrations against the real services.

---

## 1. One-time setup

### 1.1 Root `.env` (read by the agent service)

```bash
cp .env.example .env
```

Fill in at least these values:

```
API_BASE_URL=http://localhost:5138
AGENT_SHARED_SECRET=<any long random string>
LLM_BASE_URL=https://openrouter.ai/api/v1
LLM_API_KEY=<your key>
LLM_MODEL=google/gemini-3.8-flash
```

### 1.2 API user-secrets (the API does **not** read `.env`)

Run each of these from the repo root:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=campusfacilities;Username=postgres;Password=<your pg password>" --project api
```

```bash
dotnet user-secrets set "Jwt:Secret" "<32+ random characters>" --project api
```

```bash
dotnet user-secrets set "Jwt:Issuer" "CampusFacilities.Api" --project api
```

```bash
dotnet user-secrets set "Jwt:Audience" "CampusFacilities.Clients" --project api
```

```bash
dotnet user-secrets set "Agent:BaseUrl" "http://localhost:8000" --project api
```

```bash
dotnet user-secrets set "Agent:SharedSecret" "<the SAME value as AGENT_SHARED_SECRET>" --project api
```

```bash
dotnet user-secrets set "Cors:AllowedOrigins:0" "http://localhost:5173" --project api
```

**Demo accounts.** Without these, no demo users are created. Use one password for all four
if you like:

```bash
dotnet user-secrets set "Seed:Passwords:Reporter" "<your seed password>" --project api
```

```bash
dotnet user-secrets set "Seed:Passwords:Technician" "<your seed password>" --project api
```

```bash
dotnet user-secrets set "Seed:Passwords:FacilitiesManager" "<your seed password>" --project api
```

```bash
dotnet user-secrets set "Seed:Passwords:Admin" "<your seed password>" --project api
```

### 1.3 Database

```bash
createdb -U postgres campusfacilities
```

```bash
dotnet ef database update --project api
```

> ⚠️ **Run `dotnet ef database update` again after every `git pull`** that adds a migration.
> A database that is behind the code causes confusing 500 errors.

> 💡 **For a clean demo, use a fresh database.** The seeder never overwrites existing rows, so
> an older dev database keeps old, half-finished runs, and checks seeded before the
> verification-agent work have no workflow. To start clean, create a new database
> (e.g. `maintenx_demo`), point `ConnectionStrings:DefaultConnection` at it, and run
> `dotnet ef database update`.

### 1.4 Install dependencies

```bash
cd agent && python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
```

```bash
cd web && npm install
```

```bash
cd mobile && flutter pub get
```

---

## 2. Start everything (4 terminals)

Start them in this order. **Terminal 1 — agent service** (from `agent/`, *not* `uvicorn app.main:app`):

```bash
cd agent && .venv/bin/uvicorn main:app --port 8000
```

**Terminal 2 — API:**

```bash
dotnet run --project api --launch-profile http
```

**Terminal 3 — web:**

```bash
cd web && npm run dev
```

**Terminal 4 — phone app** (iOS simulator; on an Android emulator use `http://10.0.2.2:5138`):

```bash
cd mobile && flutter run --dart-define=API_BASE_URL=http://localhost:5138
```

(To test against the deployed system instead, skip terminals 1–3 and use
`--dart-define=API_BASE_URL=https://maintenx-api.onrender.com` and the web client at
https://mainten-x-gray.vercel.app — see `docs/guide/DEPLOYMENT.md`.)

### ✅ Check that it's alive

```bash
curl http://localhost:5138/health
```

```bash
curl http://localhost:8000/health
```

| Check | Expected |
|---|---|
| API `/health` | `{"status":"healthy",…}` |
| Agent `/health` | `"status":"healthy"`, `"stub_mode":false`, your model name |
| API console | `Seeding demo user reporter@campus.test…` (first start only) and a line `Verification sweep: … asked the reporter` |
| Web | http://localhost:5173 shows the landing page, and **Sign in** opens the login page |
| Swagger | http://localhost:5138/swagger lists every endpoint |

### Demo accounts

| Role | Email | Use it on |
|---|---|---|
| Reporter | `reporter@campus.test` | 📱 phone |
| Technician | `technician@campus.test` | 📱 phone or 💻 web |
| Facilities Manager | `manager@campus.test` | 💻 web |
| Admin | `admin@campus.test` | 💻 web |

Password: the seed password you set in step 1.2.

> **No phone handy?** Every phone action below also works in Swagger: call `POST /api/auth/login`,
> click **Authorize**, paste `Bearer <token>`, then call the endpoint named in the step.

---

## 3. Scenario A — the full happy path (≈ 10 minutes)

Keep a second browser window signed in as the **manager** at
http://localhost:5173/workflows. You can watch the run move there as it happens.

### A1. 📱 Reporter files a fault

1. Sign in on the phone as `reporter@campus.test`.
2. Tap **Report a fault**.
3. *(Optional but recommended)* Tap **Scan the equipment's sticker** and scan `PRJ-MAB101-01` from
   `docs/qr/asset-qr-sheet.png`. The room fills in by itself. On a simulator, use
   **Type the tag instead**.
4. Room: **Lecture Hall A (MAB-101)**. Description (keep it a bit vague so the AI asks
   something): `The projector in the lecture hall keeps switching off in the middle of lectures.`
5. Tap **Submit report**.

**Expected:** the app goes straight to a waiting screen and polls every 2 s. After about
**20 seconds** a form appears with **1–2 questions** (in our run: *"Is a warning light
flashing?"* and *"Any burning smell or smoke?"*, both Yes/No).

*(Swagger equivalent: `POST /api/reports` with `{"description": "...", "roomId": 1, "assetId": 1}` → **201**.)*

### A2. 💻 Watch the planner and clarifier (manager)

Open **Workflows**, then the newest run.

**Expected:**
- State **`AwaitingClarification`** (human pause 1).
- **Plan panel:** source *planner*, steps `clarifier → diagnostic → strategist`, and a
  one-line rationale.
- **Audit trail:** the planner step, the clarifier's tool calls (`get_room`, `get_asset`),
  then the clarifier's run with its questions. Click **Show raw** to see the exact JSON.

### A3. 📱 Reporter answers the questions

Pick an answer for each question (for example Yes, then No) and tap **Submit answers**.

**Expected:**
- The form is **only** toggles, pickers or short text boxes. There is no chat or message thread.
- Submitting again is refused. The API returns **409** "Report is not awaiting clarification".
- The workflow goes to `Diagnosing`. After **30–60 seconds** it reaches
  **`AwaitingManagerApproval`**.
- On the phone, **My reports** shows the stage move: *Waiting on you* → *Being reviewed* →
  *Awaiting approval*.

### A4. 💻 Read what the AI concluded

Open **Reports**, then the report, and scroll to **Agent reasoning** (or open the workflow page).

**Expected (from our run):**
- **Diagnostic:** *"Overheating due to failing cooling fan and weak fan bearing"*, high
  confidence, citing the dated visits **2026-05-12, 2026-07-03 and 2026-09-02**, and the word
  "compressor" does **not** appear. This is the planted pattern being found. ✅
- **Strategist:** `escalate_replacement` with an estimate (ours: Rs 185,000).
- **Approval** step: `ApprovalRequired`, "Raised by the workflow runner…".

### A5. 💻 Manager approves

Sign in as `manager@campus.test` and open **Approvals**.

**Expected:** 3 cards (the 2 seeded ones plus yours). Your card shows the report text, the
sentence **"Rs 185,000 — above the Rs 15,000 approval threshold"** with the note that it
replaces equipment, the proposal, the diagnosis, and the asset's history.

Click **Approve**, then **Confirm approval**.

**Expected:** "Work order #N approved. It can now be assigned and booked." The queue drops to 2
cards, and the workflow is now `WorkOrderRaised`.

### A6. 💻 Manager assigns and books a visit

Open **Work orders**, then your order (or use **Full order** on the card).

1. **Assignment** → *Assign to*: **Demo Technician** → **Assign**.
   The page shows "Technician assigned. Now find a time and book the visit."
2. **Find a time** → **Find free slots** (the default dates are fine).
   You get up to 20 slots, grouped by day, all inside 08:00–17:00 campus time.
3. Click **Book** on any slot.
   The page shows "Visit booked for …", and the progress rail marks *Visit booked*.

### A7. 📱 Technician completes the job

Sign in on the phone as `technician@campus.test` (or on the web and open the same order).

1. **My jobs** → the job → **Complete job**.
2. Outcome **Resolved** (note there is **no default**, so you must choose one), actual cost
   `180000`, and a note such as `Replaced projector unit with new EB-990U, tested 1 hour, no shutdown.`
3. Submit.

**Expected:** 204 / success. The workflow is **`Completed`**, and a new line appears in the
asset's service history. Try these rule checks as well:

| Try | Expected |
|---|---|
| Note `done` (under 20 chars) | **400**, "ResolutionNote … minimum length of 20" |
| Complete it as the **manager** | **403**, because only the assigned technician may complete it |

### A8. ⏩ Fast-forward 5 days (demo only)

The reporter is asked **5 days** after completion, on purpose. To demo that now, move the
dates back **in your dev database** (this changes test data, not the code):

```bash
psql -U postgres -d campusfacilities
```

```sql
-- replace 10 with your work order id, 8 with your workflow id
UPDATE "VerificationChecks" SET "DueAt" = now() - interval '1 hour' WHERE "WorkOrderId" = 10;
UPDATE "AgentWorkflows"     SET "CompletedAt" = now() - interval '6 days' WHERE "Id" = 8;
```

Then as the manager, open **Verification** and click **Run sweep now**.

**Expected:** a sentence built from `{"processed":…, "workflowsAwaitingVerification":1, "askedReporter":1, …}`.
The workflow is now `AwaitingVerification`.

### A9. 📱 Reporter confirms the fix

On the phone as the reporter: Home → **Confirm repairs** → your check.
**Is the problem fixed?** → **Yes** → **Submit answer**.

**Expected:**
- The check goes to **Confirmed** and the workflow to **`Closed`**. The report reads **Closed**.
- A few seconds later the **Verification agent** adds its opinion (confirm / reopen /
  escalate), labelled *advice*. It never changes the status.
- If the manager tries to answer, they get **403**. Only the reporter who filed it may answer.

🎉 **That is one fault's complete life.** Want a quicker "Yes" demo? The seed already has 3
checks waiting on the reporter (Computer Lab 1 AC, and others) right after the first start.

---

## 4. Scenario B — the repair did NOT hold (reopen loop)

Repeat A1–A8 (or continue from A8 before answering). In A9, answer instead:

**No, still broken**, with the comment `It switched off again today after about 40 minutes, fan noise is back.`

**Expected (from our run):**

| What | Result |
|---|---|
| Check status | **Reopened** |
| Workflow | `AwaitingVerification` → **`Diagnosing`**, then after about 80 s **`WorkOrderRaised`** (or `AwaitingManagerApproval` if the new estimate is above the threshold) |
| Verification agent | **`escalate`**, because the history now shows 4 visits for the same fault |
| Audit trail | A **second** diagnostic and strategist step beside the first. Both diagnoses stay, and you can compare them side by side on the workflow page |
| New proposal | Ours: `inspect_first`, under the threshold, so it was **auto-approved** (step `AutoApproved`, nobody decided) |
| Report stage | *Repair planned* |

---

## 5. Scenario C — quick rule checks (pick any)

| # | Do this | Expected |
|---|---|---|
| C1 | Open `http://localhost:5138/api/assets` in a browser (no token) | **401** |
| C2 | As the Reporter, `POST /api/assets` in Swagger | **403** (reads are for everyone, writes are Admin only) |
| C3 | Web as manager, **Approvals** → **Reject** with no reason | Blocked. A reason is required. With a reason, the report closes and the reporter sees *Not going ahead* |
| C4 | **Approvals** → **Request revision** with a note | The order goes back to Draft. The strategist re-runs with your note and resubmits the **same** order |
| C5 | Web as **Admin** → open **Approvals** | The link isn't offered. Visiting `/approvals` shows "not authorised" (an Admin is not a manager) |
| C6 | Phone as reporter → report with a detailed description, e.g. `Projector in MAB-102 is completely dead, no power light at all, since this morning. No smell or smoke.` | The planner may skip the clarifier ("needs no questions"), so there's no pause 1 and it goes straight to diagnosis |
| C7 | Stop the agent service (Ctrl+C), then file a report | The workflow ends in **`Failed`** with the reason. The report page offers the manager **Raise work order**, and the workflow page offers **Run the agents again** |
| C8 | Web **Metrics** (manager or Admin) | Reopen rate, clarification figures, repeat failures. Every number comes from the API, and a month with no data shows as a gap |
| C9 | Web **Assets** → `PRJ-MAB101-01` | Service history oldest-first with every note in full, a failure summary showing *repeat failure*, the warranty pill, and **Print label** |
| C10 | Web **Agent monitoring** (manager or Admin), after a report has run through the agents | Each agent's runs, failures, retries, median and p95 latency and tokens. Runs from stub mode, or from before token tracking, say "not reported", never 0. With an LLM price set (README, environment variables) each run shows an estimated cost |
| C11 | Run `for i in $(seq 1 11); do curl -s -o /dev/null -w "%{http_code} " -X POST localhost:5138/api/auth/login -H 'Content-Type: application/json' -d '{"email":"nobody@campus.test","password":"WrongPass1"}'; done` | Ten **401**s, then **429**. The 429 carries `Retry-After` and says when to try again. Wait a minute before signing in for real from the same machine |

---

## 5b. Scenario D — user management (Admin, web)

Sign in as `admin@campus.test` and open **Users** (in the Estate group of the sidebar).

| # | Do this | Expected |
|---|---|---|
| D1 | Open **Users** | Every account, each with a role pill and **Active**. The tabs show counts (All / Active / Deactivated). Search matches a name or an email |
| D2 | **Create account**: name, email `tech2@campus.test`, role **Technician**, password twice → **Create account** | The panel switches to the new account with "Account created…". The list behind updates. In a private window, `tech2@campus.test` can sign in |
| D3 | Create again with the same email in CAPITALS | "Another account already uses 'tech2@campus.test'." under Email (**409**) |
| D4 | Open `tech2`, change the name and role to **Reporter** → **Save changes** | The panel closes and the row shows the new name and role |
| D5 | Sign in as `tech2` in a private window. As the Admin, **Deactivate → Confirm**. Then click anything in the private window | The private window is signed out with "Session expired" (**the old token is refused on its next request**). Signing in again says the email or password is wrong |
| D6 | **Reactivate → Confirm** | They can sign in again |
| D7 | **Reset password** → a new password twice → **Set password** | The old password stops working and the new one works |
| D8 | Open **your own** account (marked "(you)") | The role is locked and there's no Deactivate button. The API would refuse both with 409 |
| D9 | Open **Demo Technician** while they have an assigned job (e.g. after step A6), then **Deactivate** | Refused: "Technician still has work assigned… Reassign those orders first." The panel shows the live-job count |
| D10 | As the **manager**, open a work order → *Assign to* | A deactivated technician isn't in the list |
| D11 | As the **manager**, go to `/users` | "Not authorised". The **Users** link isn't in the manager's sidebar |

---

## 5c. Scenario E — third-party integrations (Google Calendar, Supabase)

> **Walked through against the real services on 2026-09-30**: Google Calendar API v3 with the
> "Campus Timetable" calendar, and a Supabase Storage bucket `photos`. The results below are
> what came back. Both calls go through the API only; neither client talks to Google or
> writes to Supabase.

**Setup, once.** Follow the README's *Google Calendar* and *Supabase* sections: the calendar
imported from `docs/timetable/campus-timetable.ics` and **shared with the service account's
email** ("See all event details"), the Calendar API enabled, and a **public** bucket. Then:

```bash
dotnet user-secrets set "Google:ServiceAccountJsonBase64" "$(base64 -i path/to/key.json)" --project api
```

```bash
dotnet user-secrets set "Google:CalendarId" "YOUR_CALENDAR_ID@group.calendar.google.com" --project api
```

```bash
dotnet user-secrets set "Supabase:Url" "https://YOUR_PROJECT_REF.supabase.co" --project api
```

```bash
dotnet user-secrets set "Supabase:ServiceKey" "YOUR_SERVICE_ROLE_KEY" --project api
```

**Restart the API afterwards.** User-secrets are read at startup, so an API started before you
set them still runs without them. Delete the downloaded key file once it is in user-secrets.

### Google Calendar — the timetable

| # | Do this | Expected (what we got) |
|---|---|---|
| E1 | Start the API and read its log | `Timetable sync reads Google Calendar … as …@….iam.gserviceaccount.com`, then **`Timetable sync: 232 class(es) synced, 0 removed, 0 skipped.`** That's 15 weekly lectures over the 180-day window. `skipped` > 0 means an event's location matched no room code |
| E2 | Web as manager → any live work order → **Sync timetable now** (or `POST /api/timetable/sync`) | **200**, `degraded: false`, `syncedCount: 232`, `lastSyncedAt` set, `cacheAgeMinutes: 0` |
| E3 | The same call with no token / as Admin / as Technician | **401** / **403** / **403**. It's for the FacilitiesManager only |
| E4 | Sync again | `syncedCount` is still 232: the same events are updated, never duplicated |
| E5 | Slot finder for `PRJ-MAB101-01`, 60 min, **Mon 5 Oct 2026** (`GET /api/workorders/slots/available?assetId=1&durationMinutes=60&fromDate=2026-10-05&toDate=2026-10-05`) | MAB-101 has lectures at 08:30–10:30 and 13:00–15:00 Colombo time, blocked with a 15-minute buffer on each side. Offered: **11:00, 11:30, 15:30, 16:00** and nothing else |
| E6 | Restart the API with a wrong calendar ID: `Google__CalendarId=does-not-exist@group.calendar.google.com dotnet run --project api` (the env var overrides user-secrets for that run only), then sync | **200** with `degraded: true`, `failureReason: "Rejected"`, `lastSyncedAt` unchanged. The log warns that a 404 usually means "not shared with the service account". **The slot finder still offers exactly E5's slots**, from the stored timetable |

### Supabase Storage — photos

Use a report filed by `reporter@campus.test`. Only the reporter may attach a photo to it.

| # | Do this | Expected (what we got) |
|---|---|---|
| E7 | As the reporter, attach a JPEG taken on a phone (or any JPEG carrying GPS EXIF) — `POST /api/reports/{id}/photo`, form field `photo` | **201** `{ photoUrl }`. The URL is `…/storage/v1/object/public/photos/reports/{id}/<guid>.jpg`: a server-generated name, never the uploaded one |
| E8 | Open that URL with no credentials | **200** `image/jpeg`. The report page on the web shows it |
| E9 | Download it and check its metadata (`exiftool file.jpg`, or Preview → Tools → Show Inspector) | **No EXIF, no GPS, no camera make/model, no comment.** The picture itself is unchanged, pixel for pixel: the API removes metadata segments without re-encoding |
| E10 | The same with a PNG carrying a text chunk (e.g. a `Location`) | **201**, and the stored PNG has no text chunks |
| E11 | As the manager, upload to the reporter's report / with no token | **403** / **401** |
| E12 | A text file sent as `image/jpeg` | **400** "The file is not a valid image/jpeg image." Nothing is uploaded |
| E13 | Restart the API with a wrong key (`Supabase__ServiceKey=wrong-key dotnet run --project api`) and upload | **503** "Photo storage is unavailable … The report itself is unchanged." The report's `photoUrl` is the same as before, and the log shows Supabase's refusal but not the key |

The completion photo (`POST /api/workorders/{id}/photo`, the assigned Technician on a live
order) goes through the same `IFileStorageService` and the same `ImageUploadRules`. To see it
live, do step A7 with a photo attached.

---

## 6. What "correct" looks like, at a glance

| After step | Workflow state | Report status | Reporter sees |
|---|---|---|---|
| A1 filed | `Submitted` | Submitted | Being reviewed |
| Questions ready | `AwaitingClarification` | AwaitingClarification | **Waiting on you** |
| A3 answered | `Diagnosing` → `Strategizing` | Clarified → Diagnosed | Being reviewed |
| Order raised, above threshold | `AwaitingManagerApproval` | WorkOrderRaised | Awaiting approval |
| A5 approved | `WorkOrderRaised` | WorkOrderRaised | Repair planned |
| A7 completed | `Completed` | WorkOrderRaised | Repaired |
| A8 sweep | `AwaitingVerification` | WorkOrderRaised | Repaired |
| A9 "Yes" | `Closed` | Closed | Closed |
| B "No" | `Diagnosing` → … | WorkOrderRaised | Repair planned |

---

## 7. Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Workflow goes `Failed` with "Operation timed out (localhost:8000)" | The agent was started wrongly (e.g. `uvicorn app.main:app --reload` keeps the port bound after crashing). Stop it, then run `uvicorn main:app` **from `agent/`**. |
| Workflow `Failed`, "401" in the agent log | `AGENT_SHARED_SECRET` (in `.env`) ≠ `Agent:SharedSecret` (user-secrets). They must match. |
| Workflow `Failed` on a slow model | `Agent:TimeoutSeconds` is too low. The default is 360, so don't set it below that. |
| Can't sign in, "no such user" | Seed passwords weren't set **before** the first start. Set them (step 1.2) and restart the API. |
| Random 500s after a `git pull` | Pending migration. Run `dotnet ef database update --project api`. |
| Web shows network/CORS errors | `Cors:AllowedOrigins:0` must be `http://localhost:5173`. |
| Android app "can't reach API" | Use `http://10.0.2.2:5138`, not `localhost`, on the emulator. |
| Reporter's "Confirm repairs" is empty | Normal. The check isn't due for 5 days. Use step A8. |
| Clarifier questions never arrive (phone waits 3 min) | Check the agent terminal for LLM errors (402 = out of credit) and the workflow page for the reason. |
| Photo upload says "storage unavailable" (503) | Supabase isn't configured, the service key is wrong (the log shows Supabase's refusal), or the bucket doesn't exist. Without Supabase, continue without a photo. |
| Photo uploads (201) but the URL gives 400/404 in a browser | The bucket isn't **public**. The stored URL is the object's public URL. |
| "Sync timetable now" says `NotConfigured` | Google Calendar isn't configured, or the API was started **before** you set the user-secrets. Restart it. Unconfigured, the slot finder still works, but the cached timetable is empty. |
| Sync says `Rejected` | Usually the calendar isn't shared with the service account's email (the startup log prints that email), the Calendar ID is wrong, or the Calendar API isn't enabled in the Cloud project. |
| Sync says `AuthenticationFailed` | The key was deleted or disabled in Google Cloud. Create a new key and set the secret again. |
| Sync succeeds but `skippedCount` > 0 | An event's location isn't a room code (`MAB-101` etc.). Check the API log for the skipped event. |
| A user was signed out while working | An Admin deactivated them or changed their role. That takes effect on the next request, by design |
| `mobile/README.md` says the platform folders aren't in the repo | That line is out of date. `android/` and `ios/` are committed, so you don't need to run `flutter create`. |

---

## 8. Automated tests (for reference)

The same rules are pinned by automated tests, which CI runs on every push:

```bash
dotnet test api.Tests
```

```bash
cd agent && .venv/bin/pytest
```

```bash
cd web && npm run lint && npm run build
```

```bash
cd mobile && flutter analyze && flutter test
```

Live model evals cost money and are never run by CI. Record the replies and token counts as
evidence with:

```bash
cd agent && EVAL_RECORD_PATH=../docs/report/evidence/live-evals-$(date +%F)-replies.jsonl RUN_LIVE_EVALS=1 .venv/bin/pytest evals/ -v
```
