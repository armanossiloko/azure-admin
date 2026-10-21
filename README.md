# Azure Admin

Admin app for **teams**, **registered Azure DevOps repositories**, **release tracking**, and **per-user Azure DevOps organizations with PATs**. A **.NET** API and **Angular** SPA use **PostgreSQL**; **Docker Compose** runs Postgres, the API, and an nginx frontend that proxies `/api` to the backend.

## Features

- **Authentication** — Keycloak OpenID Connect with cookie session; login/logout and current-user profile for the SPA.
- **Teams** — Hierarchical teams (optional parent) used to group repositories.
- **Registered repositories** — Links an ADO org/project/repo (and optional service alias) to a team for release workflows. A repository can be marked **decommissioned** so later release batches skip it and do not open pull requests for that service.
- **Releases** — Draft releases with sprint labels; attach teams; batch-create **dev → master** and **master → prod** pull requests via the Azure DevOps REST API; store PR metadata and optional **commit notes** between branch pairs.
- **Azure DevOps settings** — Per-user organizations (URL slug) and **encrypted PAT** storage (Data Protection + EF), plus a **catalog** API to list projects and Git repos for import flows.
- **Chat notifications** — Optional Teams, Slack, or Discord incoming webhooks with a built-in template and per-event toggles (dev → master / master → prod, created or merged).
- **Background status sync** — Active pull requests are re-checked against Azure DevOps every few minutes (configurable, `PullRequestSync__*`), so merges, notifications, and dashboard counts stay current without manual refreshes. Azure DevOps calls retry on throttling and transient failures.

The API calls Azure DevOps with each user’s stored PAT; there is no global ADO service account.

## Container image

Published to GitHub Container Registry: [`ghcr.io/armanossiloko/azure-admin`](https://github.com/armanossiloko/azure-admin/pkgs/container/azure-admin)

```bash
docker pull ghcr.io/armanossiloko/azure-admin:latest
```

Tags: `latest`, semantic versions (`1.2.3`), and `sha-<short>` from CI. Helm chart: see [`charts/azure-admin`](charts/azure-admin/README.md).

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
├── src/app/api/                       # API layer: generated client + error helpers
├── openapi/AzureAdmin.API.json        # OpenAPI contract, written by the backend build
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

Optional: `PullRequestSync__Enabled`, `PullRequestSync__IntervalMinutes` (background PR status sync; see [docs/setup.md](docs/setup.md)).

API errors are JSON problem details with a top-level `message` (RFC 7807 fields plus `message` and `traceId`).

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

### API contract and generated client

The Angular app does not hand-write HTTP calls. Every backend build exports the OpenAPI document to
`openapi/AzureAdmin.API.json` and regenerates the typed client in `src/app/api/generated/`
(one `*Api` service per controller plus the model types), so controllers are the single source of truth.

- Change a controller or DTO → `dotnet build` the API → commit the regenerated `openapi/` and `src/app/api/generated/`.
- `npm run api:generate` regenerates the client from the committed contract without building the backend.
- `dotnet build -p:GenerateApiContract=false` skips the export (used by the Docker images, where no npm is available).
- CI fails when the committed contract or client is stale.

Components import from `src/app/api` (never from `generated/` directly) and use the promise-based
`*Api` methods, for example `releasesApi.releasesGet({ id })`.

Images (also built by Compose):

```bash
docker build -f Dockerfile.backend .
docker build -f Dockerfile.frontend .
```

## Database

EF Core migrations are under `src-backend/AzureAdmin.API/Migrations/`. The API applies migrations on startup.

## Docs

- [Setup guide](docs/setup.md) — Keycloak, ADO PAT scopes, release workflow, org switcher
- [Feature gaps & backlog](docs/feature-gap-and-backlog.md)
