# Azure Admin — Feature gaps & implementation backlog

This document summarizes what is **not implemented yet** in the workspace and a broader list of **features that could be added**. It reflects the codebase after the stubbed-shell work (June 2026): Keycloak auth, release PR batching, per-user ADO PATs, global teams/releases/settings, plus in-app notifications, account settings, global search, and org-scoped repository views.

---

## What is implemented today

The core product described in the [README](../README.md) is largely in place:

| Area | Status |
|------|--------|
| **Keycloak OIDC** | Login, logout, `/api/auth/me`, cookie session |
| **Teams** | Create, list, delete, optional parent team (API + UI) |
| **Registered repositories** | Manual register, ADO catalog import, filter by team and **selected organization**, edit display alias, delete |
| **Releases** | Draft releases, attach teams via PR batches, batch-create dev→master / master→prod PRs, commit notes refresh, markdown export |
| **Azure DevOps** | Per-user organizations, encrypted PATs, catalog (projects/repos) |
| **Settings** | Global conventional-commit grouping + Jira link extraction (when enabled) |
| **Dashboard** | Stats, onboarding checklist, recent activity |
| **In-app notifications** | `UserNotification` store, `/api/notifications`, bell panel with unread badge; navigation returns live `unreadNotificationsCount` |
| **PAT expiry reminders** | In-app notifications when PAT is expired or within 14 days (`NotificationService` sync on navigation/notifications load); toggle via account settings (`NotifyPatExpiry`) |
| **Account settings** | `/settings/account` — profile (Keycloak), default org, theme, PAT notification preference; shell menu wired |
| **Global search** | Topbar search (`/api/search`), releases / teams / repos / orgs; `/` and `Ctrl+K` shortcuts |
| **Organization switcher** | Sidebar selection persisted in `sessionStorage`; **scopes** registered-repository list, catalog import default, and release-create repo loading via `organizationId` query param |
| **Auth cleanup** | Dead `RegisterPage` and `/api/auth/register` removed (Keycloak-only sign-in) |

**Note:** Jira is implemented when enabled in settings—the backend extracts ticket keys and builds browse URLs in release notes. It is not a missing integration.

**Note:** PAT reminders are **in-app only** (no email or background job). Email digests remain a future item in the backlog below.

---

## Features not implemented (remaining gaps)

These are **real missing product features**, not UI stubs. Former shell mocks (notifications, search, account settings, org switcher, registration) are implemented—see table above.

### 1. Release lifecycle (Active / Completed / Archived)

The enum and dashboard logic reference `Active`, `Completed`, and `Archived`, but **nothing ever sets status beyond `Draft`**. Every create path uses `ReleaseLifecycleStatus.Draft` only; there is no PATCH/status API or UI to promote or complete a release.

The release list copy mentions “in-progress,” but that state cannot be reached in code.

### 2. Pull request status in the UI

ADO PR status is fetched only when replacing stale PR rows during batch create (`TryGetGitPullRequestStatusAsync`). It is **not** persisted on `ReleasePullRequest` and **not** shown on the release detail table (only title + “Open in ADO”).

### 3. Editing and reassignment

| Entity | Missing capability |
|--------|-------------------|
| **Releases** | Edit title, sprint, or status after creation |
| **Teams** | Rename or change parent (create/delete only) |
| **Registered repos** | Move repo to another team (`Patch` only updates `serviceName`) |
| **Release PRs** | Manual remove from a release (only implicit cleanup when ADO PR is abandoned) |

### 4. Org switcher — partial scoping

The switcher **does** filter registered repositories and related catalog/import flows. It **does not** filter releases, dashboard stats, or global team data. Releases remain org-agnostic in the model.

### 5. Shared (non–per-user) application data

Only Azure organizations and PATs are tied to `UserId`. **Teams, releases, registered repositories, and app settings are global** for all logged-in users. There is no per-user or per-tenant isolation for release workflows.

`AppSettings` is a **single global row** (`SingletonId = 1`), not per user or per org.

### 6. Automated tests

- **Backend:** no test project in the solution.
- **Frontend:** only `src/app/app.spec.ts`, which still expects `Hello, azure-admin` in an `h1` while `app.html` is just `<router-outlet />`—likely broken/stale.

### 7. Documentation

This file and the README exist; there is still no dedicated setup guide (ADO PAT scopes, release workflow, Keycloak config) beyond README/env examples.

### 8. Notification types beyond PAT

The notification store supports arbitrary kinds, but only **PAT expired** and **PAT expiring soon** are generated today. PR merged, batch failures, and similar events are not wired yet.

---

## Minor / infrastructure notes

- **PWA:** Service worker and `manifest.webmanifest` are wired in `angular.json` (build config; not a separate user-facing feature unless offline/install is a product goal).
- **CI:** Pipelines exist for frontend/backend; they do not imply missing app features.

### Summary of gaps

The **release PR batching + commit notes + ADO PAT/catalog** path is the mature part of the app. The largest **remaining product** gaps are:

- Release lifecycle beyond Draft
- PR status visibility
- Richer CRUD (teams / releases / repos)
- Multi-user data isolation (if each Keycloak user should own their own teams/releases)
- Tests and expanded product docs
- Broader notification sources (PR/batch events), optional email for PAT expiry

---

## Features that could be implemented or added

### Close the gaps (still open)

| Item | Why it matters |
|------|----------------|
| **Release lifecycle** | `Active` / `Completed` / `Archived` exist in the model but releases never leave `Draft`. Add status transitions + UI (start release, complete, archive). |
| **PR status in UI** | ADO status is read only when replacing stale PRs. Persist `status` / `mergeStatus` and show it on release detail; optional background sync. |
| **Edit releases** | PATCH title, sprint, status, description after creation. |
| **Edit teams** | Rename team, change parent without delete/recreate. |
| **Reassign repositories** | Move a registered repo to another team. |
| **Remove PR from release** | DELETE endpoint + UI when ADO PR is abandoned or created by mistake. |
| **Org switcher — full scoping** | Extend org filter to releases list/dashboard, or document global release model. |
| **Per-user or per-org tenancy** | Today teams/releases/settings are shared by all users; scope data if multiple squads use one instance. |
| **Per-user / per-org app settings** | Replace global `AppSettings` singleton if different squads need different Jira/commit rules. |
| **More notification kinds** | PR merged, batch failures, etc., in existing `UserNotification` store. |
| **PAT expiry email** | Optional email in addition to in-app PAT reminders. |
| **Tests** | API integration tests (releases, PAT, batch PR), frontend tests for critical flows; fix stale `app.spec.ts`. |
| **Product docs** | Expand `docs/` with setup, ADO PAT scopes, release workflow, Keycloak config. |

### Recently closed (formerly stubbed)

| Item | Status |
|------|--------|
| **In-app notifications** | Done — store, API, shell panel, unread count on navigation. |
| **PAT expiry reminders** | Done — in-app via `NotificationService`; user can disable in account settings. |
| **Account settings page** | Done — `/settings/account`. |
| **Global search** | Done — `/api/search`, topbar UI. |
| **Org switcher that scopes data** | Done for repositories / import / release-create repos; releases still global. |
| **Remove registration dead code** | Done — `RegisterPage` removed; Keycloak-only. |

### Release & PR workflow

| Item | Description |
|------|-------------|
| **“All PRs ready” gate** | Block master→prod batch until dev→master PRs are completed/merged (configurable per release). |
| **Release checklist** | Custom steps per phase (e.g. QA sign-off, change ticket) with checkboxes on release detail. |
| **Release templates** | Preset title/sprint pattern, default teams, default branch names, included repo sets. |
| **Scheduled / recurring releases** | Sprint-based auto-create draft releases. |
| **Auto-advance status** | When all PRs in a phase are completed, move release to `Active` or unlock next phase. |
| **PR refresh from ADO** | One-click sync titles, status, reviewers without re-creating PRs. |
| **Bulk abandon / recreate** | Select multiple repos in a phase and re-run batch after abandon. |
| **Custom phases** | Beyond Dev→Master and Master→Prod (e.g. hotfix branch). |
| **PR description from release notes** | Push generated markdown into ADO PR description on create or update. |
| **Work item linking** | Link ADO work items or Jira tickets at release level, not only in commit messages. |
| **Release comparison** | Diff commit counts or notes vs previous sprint/release. |
| **Hotfix release type** | Narrow repo set + different branch defaults. |

### Commit notes & integrations

| Item | Description |
|------|-------------|
| **More Jira patterns** | Multiple project keys, smart commits, issue keys in branch names. |
| **Azure Boards / work items** | Link `AB#123` style references like Jira. |
| **Confluence / wiki export** | One-click publish release notes to a page template. |
| **Slack / Teams notifications** | Post when batch creates PRs or when a phase completes. |
| **Email digest** | Weekly active releases + open PRs summary. |
| **Changelog formats** | JSON, HTML, PDF, or Keep a Changelog besides markdown. |
| **Commit filters** | Exclude bots, merge commits, or paths; include only `feat`/`fix`. |
| **Semantic version suggestion** | Infer next version from conventional commits (major/minor/patch). |
| **Signed-off-by / co-author** | Show in enriched commit display. |
| **Diff / file list per repo** | Optional “what changed” section from ADO compare API. |

### Azure DevOps & credentials

| Item | Description |
|------|-------------|
| **PAT health dashboard** | Widget: expiring soon, missing PAT, last successful ADO call per org. |
| **PAT scope validator** | Test PAT against required scopes before save. |
| **Service connection / federated auth** | Alternative to long-lived PATs (OIDC, managed identity in Azure). |
| **Multi-org release** | Single release spanning repos in different ADO orgs (today org is per-repo). |
| **Repo sync from ADO** | Detect renamed/deleted repos; flag stale registrations. |
| **Branch existence check** | Validate source/target branches before batch create. |
| **Policy awareness** | Warn if branch policies will block auto-complete. |
| **Import teams from ADO** | Map ADO teams/projects to app teams. |
| **Webhook from ADO** | Update PR status when PR completes in DevOps (push vs poll). |

### Teams, repos & catalog

| Item | Description |
|------|-------------|
| **Team hierarchy in UI** | Tree view instead of flat parent column. |
| **Repo tags / labels** | e.g. `frontend`, `api`, filter batch selection by tag. |
| **Default repos per team** | Pre-select common repos in release-create wizard. |
| **Duplicate registration guard** | UI hint when same repo exists on another team. |
| **Bulk import improvements** | Remember last project; import all repos in project. |
| **Service alias templates** | Naming rules from repo name → display name. |

### Dashboard & navigation

| Item | Description |
|------|-------------|
| **Actionable dashboard** | Click “PRs needing attention” → filtered release list. |
| **Filters on release list** | Status, sprint, team, date range. |
| **My releases / my org** | When tenancy exists, filter to current user’s work. |
| **Activity filters** | By kind (PR vs release vs org). |
| **Keyboard shortcuts** | `g r` → releases, `n` → new release, etc. (search: `/` and `Ctrl+K` done). |
| **Favorites** | Pin releases or repos. |

### Security, admin & operations

| Item | Description |
|------|-------------|
| **Roles (RBAC)** | Admin vs operator vs read-only; align with Keycloak roles. |
| **Audit log** | Who created PR batch, changed settings, deleted release. |
| **API tokens for CI** | Headless “create release PRs” from pipeline. |
| **Rate limiting / retry** | Safer ADO batch calls with backoff. |
| **Health beyond `/health`** | DB, Keycloak, ADO connectivity in readiness probe. |
| **Structured logging & metrics** | OpenTelemetry, release batch duration, ADO error rates. |
| **Backup / export** | Export release history JSON for compliance. |

### UX & platform

| Item | Description |
|------|-------------|
| **Onboarding wizard** | Guided flow: org → PAT → team → repo → first release (extends dashboard checklist). |
| **Empty states with actions** | Deep links already partial; make every empty state one-click setup. |
| **Offline / PWA polish** | Read-only cached release list when offline (service worker is configured). |
| **i18n** | German/English for labels (if multi-region). |
| **Accessibility pass** | Focus traps in menus, table semantics, ARIA on batch wizards. |
| **Mobile-friendly release detail** | Tables are wide; card layout on small screens. |
| **Undo / confirm patterns** | Softer deletes, restore archived releases. |

### Developer experience

| Item | Description |
|------|-------------|
| **OpenAPI client for Angular** | Generated types from Swagger instead of hand-rolled DTOs. |
| **E2E tests** | Playwright: login → register repo → create release PRs (with ADO mock). |
| **Local ADO mock** | WireMock or recorded fixtures for offline dev. |
| **Feature flags** | Toggle Jira, conventional commits, phases per environment. |
| **Seed data script** | Demo teams/repos/releases for new developers. |

---

## Suggested prioritization

### High impact, fits current app

1. Release lifecycle + PR status on detail  
2. Edit release / reassign repo / remove PR from release  
3. Org-scoped or per-user data (if multiple squads share one deploy)

### Medium effort, strong workflow value

4. “All dev PRs merged” gate before prod batch  
5. PR sync from ADO + webhooks  
6. More notification kinds (PR/batch) + optional PAT email  
7. Slack/Teams on batch complete  

### Polish & scale

8. RBAC + audit log  
9. API for CI  
10. Tests + expanded product docs  

---

## Next steps

Narrow this backlog by audience:

- **Single team, one ADO org** — prioritize lifecycle, PR status, and editing.  
- **Many teams, one instance** — prioritize tenancy, RBAC, and per-org settings.  
- **CI-driven releases** — prioritize API tokens and webhooks early.

Add effort estimates (S/M/L) per item when planning sprints.
