# Azure Admin — setup guide

This guide covers Keycloak, Azure DevOps PATs, and the release workflow beyond the short [README](../README.md) and env examples.

## Prerequisites

- PostgreSQL 16+ (or Docker Compose)
- Keycloak realm with a confidential client for the API
- An Azure DevOps organization and personal access token (PAT) per user who will create PRs

## Keycloak

### Local Compose

Dev Compose can start Keycloak with a preloaded realm. See [`docker/dev/README.md`](../docker/dev/README.md) and `.env.dev.example`.

Typical local client settings:

| Setting | Value |
|---------|--------|
| Client ID | `azure-admin` (or `KEYCLOAK_CLIENT_ID`) |
| Access type | Confidential |
| Valid redirect URIs | `http://localhost:8080/signin-oidc`, `http://localhost:4200/signin-oidc` (SPA proxy) |
| Post-logout redirect | App origin (e.g. `http://localhost:8080/`) |

API configuration uses `Keycloak__Authority` (full realm URL), `Keycloak__ClientId`, and `Keycloak__ClientSecret`. Cookie auth is established after the OIDC code flow; the SPA calls `/api/auth/me` with credentials.

### Production notes

- Set `KEYCLOAK_REQUIRE_HTTPS_METADATA=true`.
- Register every public app origin’s `/signin-oidc` (and logout redirect) on the client.
- Users are provisioned into the app database on first successful login (`KeycloakUserProvisioner`).

## Azure DevOps PAT

Each logged-in user adds organizations under **Settings → Azure organizations** and stores an encrypted PAT. The API never uses a shared service account.

### Recommended scopes

Minimum scopes for catalog import, PR batching, complete-batch, and commit notes:

| Scope | Why |
|-------|-----|
| **Code (Read & write)** | Create PRs, read commits/diffs between branches, complete (merge) PRs |
| **Project and Team (Read)** | List projects for catalog import |
| **Identity (Read)** (optional) | Display names when present in ADO responses |

Code **Read** alone is not enough for opening or completing pull requests.

### Expiration

Set the PAT expiry date when saving credentials. The app raises **in-app** notifications when a PAT is expired or within 14 days (toggle under **Account settings**). There is no email digest.

## First-run checklist

1. Sign in via Keycloak.
2. Add an Azure DevOps organization + PAT.
3. Create a team (optional parent).
4. Register or import repositories into that team.
5. Create a draft release, enroll teams via a PR batch, open Dev → Master (and later Master → Prod).

The dashboard onboarding checklist mirrors these steps.

## Release workflow

1. **Draft** — create a release (title + optional sprint). Status starts as `Draft`.
2. **Start / Active** — use **Start release**, or open the first PR batch (auto-promotes to `Active`).
3. **Phases** — for each team, batch-create PRs:
   - **Prod → Dev** when prod is ahead of dev (back-merge).
   - **Dev → Master** for promotion into master.
   - **Master → Prod** only after the matching Dev → Master PR is **Completed** (gate).
4. **Status sync** — open a release detail page or click **Check status** to refresh ADO PR status.
5. **Complete** — **Complete all (rebase)** merges open PRs in a phase via ADO.
6. **Close / Archive** — **Close release** → `Completed` (blocks further batches); **Archive** → `Archived`.

Commit notes can be refreshed from ADO and exported as markdown. Conventional-commit grouping and Jira link extraction are controlled in global **Settings**.

## Organization switcher

The sidebar organization filters repositories, teams (with repos in that org), releases (derived from those repos/teams), dashboard stats, and global search. Releases themselves have no `OrganizationId` column; empty drafts (no teams/PRs yet) still appear under any selected org so new work is visible.

## Local development quick start

```bash
# API (default http://localhost:5063)
cd src-backend/AzureAdmin.API && dotnet run

# SPA (http://localhost:4200, proxies /api)
npm install && npm start
```

Or: `docker compose -f docker-compose.dev.yml --env-file .env.dev up --build`.

## Tests

```bash
dotnet test src-backend/AzureAdmin.API.Tests/AzureAdmin.API.Tests.csproj
npm test   # Angular unit tests (Karma/Vitest per angular.json)
```

## Related docs

- [README](../README.md) — stack, Compose ports, image publish
- [feature-gap-and-backlog.md](feature-gap-and-backlog.md) — product gaps and ideas
