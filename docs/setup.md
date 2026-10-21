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

## Chat notifications (Teams, Slack, Discord)

Azure Admin can POST one summary message for **dev → master created**, **dev → master merged**, **master → prod created**, and **master → prod merged**. Chat apps never talk to Azure DevOps; you do not register ADO service hooks.

1. Create an incoming webhook in the destination (Teams: channel **… → Workflows → “Send webhook alerts to a channel”**; Slack: Incoming Webhooks; Discord: Channel → Integrations → Webhooks). Azure Admin POSTs the same kind of JSON Discord uses: `{ "text": "…" }` for Teams/Slack, `{ "content": "…" }` for Discord.
2. Under **Settings → Chat notifications**, add the destination, paste the URL, and toggle the events you want.
3. The built-in template is shown on that page. Optionally customize it with placeholders such as `{{pr_count}}`, `{{phase}}`, `{{team_count}}`, and `{{pr_list}}` (`- name - team`).
4. Click **Send test** to post a dummy "created" or "merged" message. In the add/edit form it uses the unsaved URL and templates, so you can check that Teams, Slack, or Discord accepts the URL before saving. On a saved destination it uses the stored settings (paused destinations too). If the chat service rejects the message, its HTTP status and response text are shown.

The webhook URL is encrypted at rest and only a masked preview is shown after save. Delivery failures are recorded on the destination and never fail PR creation.

## Background pull request status sync

The API re-checks every still-active pull request on Draft/Active releases against Azure DevOps on a timer (default every 5 minutes, first run 30 s after startup). Merged PRs therefore show up, in-app "PR merged" notifications and phase-complete chat messages fire, and dashboard counts stay current without anyone opening the release page or clicking **Check status**.

PATs are per user, so each PR is checked with the PAT of the user who opened it; for PRs opened before this feature (or when that user's PAT expired) the sync uses whichever user most recently saved a valid PAT for that organization. PRs in an organization nobody holds a PAT for keep their last known status until someone refreshes manually.

Configuration (`PullRequestSync` section; env vars `PullRequestSync__*`):

| Key | Default | Notes |
|-----|---------|-------|
| `Enabled` | `true` | Set `false` to go back to manual refresh only |
| `IntervalMinutes` | `5` | 1 to 1440 |
| `InitialDelaySeconds` | `30` | Lets migrations finish before the first run |
| `MaxConcurrency` | `4` | Concurrent Azure DevOps lookups per release |

With Helm, pass these through `extraEnv` (for example `PullRequestSync__IntervalMinutes: "10"`). Run a single replica, as the chart defaults to; every replica would poll independently.

## Azure DevOps call resilience

All calls to `dev.azure.com` go through two named HTTP clients. Reads retry up to three times with exponential backoff on 5xx, 408, and 429 (honouring `Retry-After`), with a 30 s per-attempt and 120 s total timeout plus a circuit breaker. Writes (create PR, complete PR, delete branch) are retried only on 429/503, which Azure DevOps returns before processing the request, so a retry can never open a duplicate pull request.

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
4. **Status sync** — refreshed automatically in the background (see above); open a release detail page or click **Check status** for an immediate refresh.
5. **Complete** — **Complete all (rebase)** merges open PRs in a phase via ADO.
6. **Close / Archive** — **Close release** → `Completed` (blocks further batches); **Archive** → `Archived`.

Commit notes can be refreshed from ADO and exported as markdown. Conventional-commit grouping and Jira link extraction are controlled in global **Settings**.

Each release records who created it and when it was last edited (shown on the list and detail pages; releases created before this was tracked show no creator). Edits are guarded by a concurrency stamp: if someone else changed the release after you opened it, your save is rejected with **409 Conflict**, the page reloads their version, and you can reapply your change. API clients opt in by sending the `concurrencyStamp` they read back in `PATCH /api/releases/{id}`; requests without it are applied as before.

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

## API contract and Angular client

Building the API (`dotnet build`, `dotnet run`, `dotnet test`) runs two extra targets:

1. `ExportOpenApiContract` runs the API in `export-openapi` mode (no Kestrel, no migrations, no
   background services) and writes `openapi/AzureAdmin.API.json`. The file only changes when the
   contract does, so rebuilds are no-ops for git.
2. `GenerateAngularApiClient` runs `npm run api:generate` (ng-openapi-gen, configured in
   `ng-openapi-gen.json`) and rewrites `src/app/api/generated/`. If `node_modules` is missing the
   build warns instead of failing, so `npm ci` once before building the backend.

Both outputs are committed. The CI test job rebuilds and fails on any diff under `openapi/` or
`src/app/api/generated/`, so after changing a controller, DTO, or enum: build, review the generated
diff, and commit it together with the backend change.

Conventions on the Angular side:

- Import from `src/app/api` (the barrel), never from `src/app/api/generated/...` directly.
- Operation names are `<controller><Action>` on the controller's `*Api` service and return promises:
  `teamsApi.teamsCreate({ body })`, `releasesApi.releasesGet({ id })`.
- Enums are string unions with a matching value array (`ReleasePrPhase` / `RELEASE_PR_PHASE`) for
  parsing user input.
- Use `apiErrorMessage(error, fallback)` to surface the API's `message` / problem-details text.
- Set `-p:GenerateApiContract=false` when building without npm (the Dockerfiles already do).

## Tests

```bash
dotnet test src-backend/AzureAdmin.API.Tests/AzureAdmin.API.Tests.csproj
npm test   # Angular unit tests (Karma/Vitest per angular.json)
```

## Related docs

- [README](../README.md) — stack, Compose ports, image publish
- [feature-gap-and-backlog.md](feature-gap-and-backlog.md) — product gaps and ideas
