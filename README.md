# Azure Admin

Internal-style admin app for **teams**, **registered Azure DevOps repositories**, **release tracking**, and **per-user Azure DevOps organizations with PATs**. A **.NET** API and **Angular** SPA talk to a **PostgreSQL** database; optional **Docker Compose** runs the full stack.

**UI-only design brief (no backend):** copy [UI_DESIGN_PROMPT.md](./UI_DESIGN_PROMPT.md) into your design AI.

---

## What it does

- **Authentication** — ASP.NET Core Identity with cookie auth; register, login, logout, and a `me`-style profile for the SPA.
- **Teams** — Hierarchical teams (optional parent) used to group repositories.
- **Registered repositories** — Links an ADO org/project/repo (and optional service alias) to a team for release workflows.
- **Releases** — Draft releases with sprint labels; attach teams; batch-create **dev → master** and **master → prod** pull requests via the Azure DevOps REST API; store PR metadata and optional **commit notes** between branch pairs.
- **Azure DevOps settings** — Per-user organizations (URL slug) and **encrypted PAT** storage (Data Protection + EF), plus a **catalog** API to list projects and Git repos for import flows.

The API calls Azure DevOps with the user’s stored PAT; it does not ship a global service account for ADO.

---

## Tech stack

| Layer | Technology |
|--------|------------|
| API | ASP.NET Core **10**, EF Core, Npgsql, Swagger, Identity + cookies |
| SPA | **Angular 21**, TypeScript, SCSS |
| Database | **PostgreSQL 16** |
| Ops | Docker / Docker Compose; Data Protection keys persisted in the DB |

---

## Repository layout

```text
azure-admin/
├── AzureAdmin.slnx          # Visual Studio / `dotnet` solution (folders + API project)
├── docker-compose.yml       # postgres + backend + frontend
├── README.md                # This file
├── UI_DESIGN_PROMPT.md      # Product/UI brief for design AIs (no backend)
├── backend/
│   └── AzureAdmin.Api/      # Web API, EF migrations, Dockerfile
└── frontend/                # Angular app, Dockerfile, nginx for production image
```

Scratch publish folders such as `_b2` or `_buildcheck-api` are **ignored** (see `.gitignore`); they are not part of the product layout.

---

## Backend (`backend/AzureAdmin.Api`)

Organized by **feature area** (similar to a small modular service):

| Area | Purpose |
|------|---------|
| `Controllers/` | HTTP endpoints grouped in subfolders: `Auth`, `Health`, `Teams`, `Repositories`, `Releases`, `AzureDevOps` |
| `Contracts/` | Request/response DTOs and records (shared `AzureAdmin.Api.Contracts` namespace, split across folders) |
| `Services/AzureDevOps/` | PAT resolution, org management, catalog HTTP calls, Git PR create / status / commits |
| `Services/Releases/` | Batch PR creation, commit-notes hydration |
| `Services/Identity/` | Current user abstraction over `HttpContext` |
| `Configuration/` | Options types (e.g. `AzureDevOpsOptions`) |
| `Common/` | Shared helpers (e.g. JSON defaults) |
| `DependencyInjection/` | `AddApplicationServices` registration |
| `Data/` | `ApplicationDbContext` |
| `Models/` | EF entities |
| `Migrations/` | EF Core migrations |

Entry point: `Program.cs` (middleware, Identity, CORS, DB migrate on startup in development patterns, etc.).

---

## Frontend (`frontend`)

Standard Angular **standalone** style app: `src/app/` with **pages** (dashboard, login, register, teams, repositories, Azure organizations, releases), **auth** guards/interceptors, and a **shell** layout. Dev proxy targets the API (see `proxy.conf.json`).

---

## Configuration

**API** (`appsettings.json` / environment):

- `ConnectionStrings:DefaultConnection` — Npgsql connection string (required).
- `AzureDevOps:ApiVersion` — REST API version segment (default commonly `7.1`).

**Secrets** — Use local overrides or environment variables; see `.gitignore` for patterns like `appsettings.*.local.json` and `.env`.

---

## Running the stack

### Docker Compose

From the repo root:

```bash
docker compose up --build
```

Typical ports (see `docker-compose.yml`):

- Frontend: **http://localhost:4200**
- API: **http://localhost:5063**
- Postgres: **localhost:5432** (dev credentials in compose file only)

### Local development (without Docker for the API)

1. Start PostgreSQL and set `ConnectionStrings__DefaultConnection` (or `appsettings.Development.json`).
2. API: `cd backend/AzureAdmin.Api && dotnet run`
3. SPA: `cd frontend && npm install && npm start` (uses dev server + proxy to the API URL configured for your machine).

---

## Database

EF Core migrations live under `backend/AzureAdmin.Api/Migrations/`. On startup the API applies migrations (see `Program.cs` for the exact behavior in your environment).
