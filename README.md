# Azure Admin

Admin app for **teams**, **registered Azure DevOps repositories**, **release tracking**, and **per-user Azure DevOps organizations with PATs**. A **.NET** API and **Angular** SPA use **PostgreSQL**; **Docker Compose** runs Postgres, the API, and an nginx frontend that proxies `/api` to the backend.

## Features

- **Authentication** — Keycloak OpenID Connect with cookie session; login/logout and current-user profile for the SPA.
- **Teams** — Hierarchical teams (optional parent) used to group repositories.
- **Registered repositories** — Links an ADO org/project/repo (and optional service alias) to a team for release workflows.
- **Releases** — Draft releases with sprint labels; attach teams; batch-create **dev → master** and **master → prod** pull requests via the Azure DevOps REST API; store PR metadata and optional **commit notes** between branch pairs.
- **Azure DevOps settings** — Per-user organizations (URL slug) and **encrypted PAT** storage (Data Protection + EF), plus a **catalog** API to list projects and Git repos for import flows.

The API calls Azure DevOps with each user’s stored PAT; there is no global ADO service account.

## Stack

| Layer | Technology |
|--------|------------|
| API | ASP.NET Core **10**, EF Core, Npgsql, Swagger |
| SPA | **Angular 21**, TypeScript, SCSS |
| Database | **PostgreSQL 16** |
| Auth | Keycloak (OIDC) + cookies |
| Ops | Docker / Docker Compose |

## Layout

```text
azure-admin/
├── package.json, angular.json, src/   # Angular SPA
├── Dockerfile.backend                 # ASP.NET Core API
├── Dockerfile.frontend                # Angular SPA (nginx)
├── nginx.frontend.conf
├── docker-compose.yml
├── src-backend/AzureAdmin.API/        # .NET API, migrations
└── AzureAdmin.slnx
```

## Configuration

Copy `.env.example` to `.env` for Docker Compose (Postgres, Keycloak, Azure DevOps API version).

For local API runs, set `Postgres` and `Keycloak` in `src-backend/AzureAdmin.API/appsettings.json` or via environment variables (`Postgres__Host`, `Keycloak__Authority`, etc.). See `.gitignore` for local secret patterns (`appsettings.*.local.json`, `.env`).

## Run

### Docker Compose

```bash
docker compose up --build
```

- App (nginx → API): **http://localhost:8080**
- Postgres: **localhost:5432** (credentials from `.env`)

Register **http://localhost:8080/signin-oidc** (and your logout redirect URL) in Keycloak when using Compose.

### Local development

1. PostgreSQL running; configure `Postgres` (and `Keycloak`) for the API.
2. API: `cd src-backend/AzureAdmin.API && dotnet run` (default **http://localhost:5063**).
3. SPA: `npm install && npm start` from the repo root (**http://localhost:4200**, proxies `/api` to the API via `proxy.conf.json`).

Images (also built by Compose):

```bash
docker build -f Dockerfile.backend .
docker build -f Dockerfile.frontend .
```

## Database

EF Core migrations are under `src-backend/AzureAdmin.API/Migrations/`. The API applies migrations on startup.
