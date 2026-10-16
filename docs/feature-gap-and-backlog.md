# Azure Admin — Feature gaps & implementation backlog

This document summarizes remaining gaps and a broader list of **features that could be added**. Setup, Keycloak, ADO PAT scopes, and the release workflow are documented in [setup.md](setup.md).

---

## What is implemented today

The core product described in the [README](../README.md) is largely in place:

| Area | Status |
|------|--------|
| **Keycloak OIDC** | Login, logout, `/api/auth/me`, cookie session |
| **Teams** | Create, list, rename/reparent, delete; optional parent; filter by selected org |
| **Registered repositories** | Manual register, ADO catalog import, filter by team and **selected organization**, edit alias / team / decommissioned, delete |
| **Releases** | Draft→Active→Completed/Archived lifecycle; PR batches; commit notes; markdown export; edit meta; remove PRs from release |
| **PR status** | Persisted on `ReleasePullRequest`, shown on detail, sync on open / Check status |
| **Azure DevOps** | Per-user organizations, encrypted PATs, catalog (projects/repos) |
| **Settings** | Global conventional-commit grouping + Jira link extraction (when enabled) |
| **Dashboard** | Stats, onboarding checklist, recent activity (org-scoped when selected) |
| **In-app notifications** | `UserNotification` store, `/api/notifications`, bell panel with unread badge |
| **PAT expiry reminders** | In-app when PAT is expired or within 14 days; toggle via account settings |
| **Account settings** | `/settings/account` — profile, default org, theme, PAT notification preference |
| **Global search** | Topbar search (`/api/search`), org-scoped when an org is selected |
| **Organization switcher** | Scopes repos, teams, releases, dashboard, search, catalog/import |
| **Auth cleanup** | Keycloak-only sign-in |
| **Tests** | Backend xUnit starter + fixed `app.spec.ts` |
| **Docs** | README + [setup.md](setup.md) (Keycloak, PAT scopes, release workflow) |

**Note:** Jira is implemented when enabled in settings—the backend extracts ticket keys and builds browse URLs in release notes.

**Note:** Notifications are **in-app only** (no email channel). Email digests / PAT email are out of scope for this product.

---

## Features not implemented (remaining gaps)

### 5. Shared (non–per-user) application data

Only Azure organizations and PATs are tied to `UserId`. **Teams, releases, registered repositories, and app settings are global** for all logged-in users. There is no per-user or per-tenant isolation for release workflows.

`AppSettings` is a **single global row** (`SingletonId = 1`), not per user or per org.

### 8. Notification types beyond PAT

The notification store supports arbitrary kinds, but only **PAT expired** and **PAT expiring soon** are generated today. PR merged, batch failures, and similar events are not wired yet.

### Recently closed gaps

| Item | Notes |
|------|--------|
| **1. Release lifecycle** | Start / close / archive; auto-Active on first PR batch |
| **2. PR status in UI** | Persist + show + sync |
| **3. Editing / reassignment** | Releases, teams, repos, remove PR from release |
| **4. Org switcher scoping** | Releases, dashboard, teams, search |
| **6. Automated tests** | Backend unit tests + fixed frontend spec |
| **7. Documentation** | [setup.md](setup.md) |

---

## Minor / infrastructure notes

- **PWA:** Service worker and `manifest.webmanifest` are wired in `angular.json`.
- **CI:** Pipelines exist for frontend/backend; they do not imply missing app features.

### Summary of gaps

Largest **remaining** product gaps:

- Multi-user / multi-tenant data isolation (if needed)
- Broader in-app notification sources (PR/batch events)

---

## Features that could be implemented or added

### Close the gaps (still open)

| Item | Why it matters |
|------|----------------|
| **Per-user or per-org tenancy** | Today teams/releases/settings are shared by all users; scope data if multiple squads use one instance. |
| **Per-user / per-org app settings** | Replace global `AppSettings` singleton if different squads need different Jira/commit rules. |
| **More notification kinds** | PR merged, batch failures, etc., in existing `UserNotification` store (in-app only). |
| **Deeper tests** | API integration tests (releases, PAT, batch PR), frontend tests for critical flows. |

### Recently closed (formerly stubbed or gap items)

| Item | Status |
|------|--------|
| **In-app notifications** | Done — store, API, shell panel, unread count on navigation. |
| **PAT expiry reminders** | Done — in-app via `NotificationService`; user can disable in account settings. |
| **Account settings page** | Done — `/settings/account`. |
| **Global search** | Done — `/api/search`, topbar UI; org-scoped. |
| **Org switcher that scopes data** | Done for repos, teams, releases, dashboard, search. |
| **Release lifecycle / PR status / editing** | Done — see closed gaps above. |
| **Setup docs** | Done — [setup.md](setup.md). |
| **Remove registration dead code** | Done — Keycloak-only. |

### Release & PR workflow

| Item | Description |
|------|-------------|
| **“All PRs ready” gate** | Block master→prod batch until dev→master PRs are completed/merged (configurable per release). *(Basic gate already exists; make configurable.)* |
| **Release checklist** | Custom steps per phase (e.g. QA sign-off, change ticket) with checkboxes on release detail. |
| **Release templates** | Preset title/sprint pattern, default teams, default branch names, included repo sets. |
| **Scheduled / recurring releases** | Sprint-based auto-create draft releases. |
| **Auto-advance status** | When all PRs in a phase are completed, move release to `Active` or unlock next phase. |
| **PR refresh from ADO** | One-click sync titles, reviewers (status sync already exists). |
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

1. More in-app notification kinds (PR/batch events)  
2. Org-scoped or per-user data (if multiple squads share one deploy)

### Medium effort, strong workflow value

3. “All dev PRs merged” gate configurability  
4. PR webhooks from ADO  
5. Slack/Teams on batch complete  

### Polish & scale

6. RBAC + audit log  
7. API for CI  
8. Deeper integration / E2E tests  

---

## Next steps

Narrow this backlog by audience:

- **Single team, one ADO org** — workflow polish and notifications.  
- **Many teams, one instance** — prioritize tenancy, RBAC, and per-org settings.  
- **CI-driven releases** — prioritize API tokens and webhooks early.

Add effort estimates (S/M/L) per item when planning sprints.
