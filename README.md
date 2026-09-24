# MaintenX

> Stub — fill in the placeholders marked _TODO_ as the project takes shape.

## Overview

_TODO: one paragraph — what the system does, who uses it, and the problem it solves._

MaintenX is a maintenance management system built for the SE3090 module. It consists of an
ASP.NET Core Web API, a React web client, a Flutter mobile client, and a Python agent
service that assists with non-deterministic tasks.

Two architectural rules shape everything else:

- All deterministic business rules (SLA clocks, cost thresholds, timetable conflicts,
  technician availability, failure counts, warranty dates, approval routing) live in the
  C# API — never in an AI prompt.
- The web and mobile clients talk **only** to the API. The agent service is called by the
  API, holds no database credentials, and reads data only through allow-listed HTTP tool
  calls back to the API.

Development conventions for every part of this repo are in [CLAUDE.md](CLAUDE.md).

## Stack

| Layer    | Technology                                   | Folder   |
| -------- | -------------------------------------------- | -------- |
| API      | ASP.NET Core Web API (.NET 8), EF Core        | `api/`   |
| Agent    | Python 3.11+, FastAPI                         | `agent/` |
| Web      | React 18 + Vite, JavaScript (not TypeScript)  | `web/`   |
| Mobile   | Flutter                                       | `mobile/`|
| Database | PostgreSQL (Supabase)                         | —        |
| Auth     | JWT bearer tokens issued by the API           | —        |

Docs and ADRs live in [`docs/`](docs/). CI workflows live in `.github/workflows/`.

## Local setup

Prerequisites:

- .NET 8 SDK
- Node.js 20+
- Python 3.11+
- Flutter SDK (stable channel)
- PostgreSQL, or a Supabase project

```bash
git clone <repo-url>
cd MaintenX
cp .env.example .env
```

Then fill in `.env`. It is git-ignored — never commit it.

## Environment variables

All variables are listed in [`.env.example`](.env.example) with empty values.

| Variable               | Used by | Purpose                                              |
| ---------------------- | ------- | ---------------------------------------------------- |
| `DATABASE_URL`         | api     | Postgres/Supabase connection string for EF Core       |
| `JWT_SECRET`           | api     | Symmetric signing key for issued JWTs (32+ chars)     |
| `JWT_ISSUER`           | api     | `iss` claim stamped and validated                     |
| `JWT_AUDIENCE`         | api     | `aud` claim expected from the clients                 |
| `AGENT_SERVICE_URL`    | api     | Base URL of the FastAPI agent service                 |
| `AGENT_SHARED_SECRET`  | api, agent | Shared secret authenticating API → agent calls     |
| `LLM_BASE_URL`         | agent   | LLM provider base URL                                 |
| `LLM_API_KEY`          | agent   | LLM provider API key — agent service only             |
| `LLM_MODEL`            | agent   | Model identifier requested by the agent               |
| `SUPABASE_URL`         | api     | Supabase project URL                                  |
| `SUPABASE_SERVICE_KEY` | api     | Supabase service role key — server-side only          |
| `SUPABASE_STORAGE_BUCKET` | api  | Public Storage bucket for photos (default `photos`)   |
| `GOOGLE_SERVICE_ACCOUNT_JSON_BASE64` | api | Base64 of the timetable service account key — server-side only |
| `GOOGLE_CALENDAR_ID`   | api     | The "Campus Timetable" Google Calendar's ID            |

Never place `SUPABASE_SERVICE_KEY`, `GOOGLE_SERVICE_ACCOUNT_JSON_BASE64`, `LLM_API_KEY`, or
`JWT_SECRET` in the web or mobile client.

## First-time API setup

`appsettings.Development.json` is git-ignored, so a fresh clone has no database password,
no signing key and no seed passwords. Everyone sets their own — the repo commits the
*shape* of the configuration, never the values.

Use `dotnet user-secrets`. It stores values in your home directory, outside the repository,
so they cannot be committed even by accident. Run these once after cloning, substituting
your own values:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=campusfacilities;Username=postgres;Password=YOUR_POSTGRES_PASSWORD" --project api
```

```bash
dotnet user-secrets set "Jwt:Secret" "a-long-random-string-of-at-least-32-characters" --project api
dotnet user-secrets set "Jwt:Issuer" "CampusFacilities.Api" --project api
dotnet user-secrets set "Jwt:Audience" "CampusFacilities.Clients" --project api
```

The four demo accounts are only created if you give them passwords. Any value works
locally; a blank one simply skips that user:

```bash
dotnet user-secrets set "Seed:Passwords:Reporter" "DevPass123" --project api
dotnet user-secrets set "Seed:Passwords:Technician" "DevPass123" --project api
dotnet user-secrets set "Seed:Passwords:FacilitiesManager" "DevPass123" --project api
dotnet user-secrets set "Seed:Passwords:Admin" "DevPass123" --project api
```

Not a secret, but the API rejects browser calls from the React dev server without it:

```bash
dotnet user-secrets set "Cors:AllowedOrigins:0" "http://localhost:5173" --project api
```

Photo uploads go to Supabase Storage. Without these the API still starts, but
`POST /api/reports/{id}/photo` returns 503. Create a **public** bucket named `photos` in the
Supabase dashboard first (Storage → New bucket), then:

```bash
dotnet user-secrets set "Supabase:Url" "https://YOUR_PROJECT_REF.supabase.co" --project api
dotnet user-secrets set "Supabase:ServiceKey" "YOUR_SERVICE_ROLE_KEY" --project api
```

The maintenance slot finder works around classes mirrored from a Google Calendar. Without
these the API still starts, but the timetable never syncs and every room looks free. Setup
is done once, in Google, by whoever owns the demo calendar — there is no OAuth consent
screen and no user login, only a service account:

1. In the [Google Cloud console](https://console.cloud.google.com/), create a project and
   enable the **Google Calendar API** (APIs & Services → Library).
2. IAM & Admin → Service Accounts → **Create service account** (no roles needed). Open it →
   Keys → Add key → JSON, and download the file. **Never commit it** — keep it outside the
   repository, and delete it once it is in user-secrets.
3. In an ordinary Google account, create a calendar named **Campus Timetable**.
4. Import [`docs/timetable/campus-timetable.ics`](docs/timetable/campus-timetable.ics) into
   it (Settings → Import & export → Import, and choose that calendar): 15 weekly lectures
   across the seeded rooms, each event's **location** set to a room code such as `MAB-101`.
   That code is how an event is mapped to a room — a location matching no room is skipped.
5. The calendar's Settings → Share with specific people → add the service account's email
   (`…@….iam.gserviceaccount.com`) with **See all event details**. Copy the **Calendar ID**
   from Integrate calendar on the same page.

```bash
dotnet user-secrets set "Google:ServiceAccountJsonBase64" "$(base64 -i path/to/key.json)" --project api
dotnet user-secrets set "Google:CalendarId" "YOUR_CALENDAR_ID@group.calendar.google.com" --project api
```

`base64 -i` is the macOS form; on Linux use `base64 -w0 key.json`, and in PowerShell
`[Convert]::ToBase64String([IO.File]::ReadAllBytes("key.json"))`.

On startup the API logs which calendar it reads and as which service account email; on
Render set the same two values as `Google__ServiceAccountJsonBase64` and `Google__CalendarId`.
It syncs at startup and hourly, and a FacilitiesManager can sync now with
`POST /api/timetable/sync`. If that answers `"degraded": true` with `"failureReason":
"Rejected"`, the calendar is usually not shared with the service account.

Check what you have set at any time — this reads the local store, not the repo:

```bash
dotnet user-secrets list --project api
```

Then create the schema and start the API. Seeding happens automatically on startup in
Development and is idempotent, so restarting never duplicates data:

```bash
dotnet ef database update --project api
```

```bash
dotnet run --project api
```

You should see four `Seeding demo user ... with role ...` lines on the first run and none
on later runs. The demo accounts are `reporter@`, `technician@`, `manager@` and `admin@`
`campus.test`, each with the password you set above.

### Other ways to supply the same values

Configuration is layered, each level overriding the one before:

```
appsettings.json  <  appsettings.Development.json  <  user secrets  <  environment variables
```

So any of these work. Environment variables use `__` (double underscore) where the config
key has a `:` — that form is what [`.env.example`](.env.example) documents and what CI uses:

```bash
export Seed__Passwords__Reporter="DevPass123"
```

The API also accepts the flat `DATABASE_URL`, `JWT_SECRET`, `JWT_ISSUER` and `JWT_AUDIENCE`
names from `.env.example` as fallbacks, so either naming style is fine.

## Running each service

_TODO: confirm each command once the projects are scaffolded._

**API** — `http://localhost:5000`

```bash
dotnet run --project api
```

**Agent service** — `http://localhost:8000`

```bash
uvicorn app.main:app --reload --port 8000
```

**Web client** — `http://localhost:5173`

```bash
npm run dev --prefix web
```

**Mobile client**

```bash
flutter run
```

Start order for a full local run: database → API → agent service → web/mobile.

## Team

| Name | Registration no. | Role |
| ---- | ---------------- | ---- |
| _TODO_ | _TODO_ | _TODO_ |
| _TODO_ | _TODO_ | _TODO_ |
| _TODO_ | _TODO_ | _TODO_ |
| _TODO_ | _TODO_ | _TODO_ |

SLIIT — Year 3, Semester 1 — Software Engineering Fundamentals (SE3090).
