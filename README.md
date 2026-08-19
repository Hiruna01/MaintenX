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

Never place `SUPABASE_SERVICE_KEY`, `LLM_API_KEY`, or `JWT_SECRET` in the web or mobile
client. Local API secrets belong in `appsettings.Development.json`, which is git-ignored.

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
