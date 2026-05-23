# Azure Admin — Feature gaps & implementation backlog

This document summarizes what is **not implemented yet** in the workspace and a broader list of **features that could be added**. It reflects the codebase as of the initial analysis (Keycloak auth, release PR batching, per-user ADO PATs, global teams/releases/settings).

---

## What is implemented today

The core product described in the [README](../README.md) is largely in place:

| Area | Status |
|------|--------|
| **Keycloak OIDC** | Login, logout, `/api/auth/me`, cookie session |
| **Teams** | Create, list, delete, optional parent team (API + UI) |
| **Registered repositories** | Manual register, ADO catalog import, filter by team, edit display alias, delete |
| **Releases** | Draft releases, attach teams via PR batches, batch-create dev→master / master→prod PRs, commit notes refresh, markdown export |
| **Azure DevOps** | Per-user organizations, encrypted PATs, catalog (projects/repos) |
| **Settings** | Global conventional-commit grouping + Jira link extraction (when enabled) |
| **Dashboard** | Stats, onboarding checklist, recent activity |

**Note:** Jira is implemented when enabled in settings—the backend extracts ticket keys and builds browse URLs in release notes. It is not a missing integration.

---

## Features not implemented (or only stubbed)

### 1. In-app notifications

The API explicitly reserves this and always returns zero unread count:

```csharp
// NavigationController.cs
// Reserved for future in-app notifications; always zero until a notifications store exists.
const int unreadNotificationsCount = 0;
```

The bell in the shell only calls `loadNavigation()` again; there is no notifications list or store.

### 2. PAT renewal reminders

The org detail form says users will get “renewal reminders,” but there is no job, email, or in-app alert—only stored `patExpiresAt` and visual badges when expired.

### 3. Local registration (`RegisterPage`)

`src/app/pages/register/register.page.ts` posts to `/api/auth/register`, but:

- That route is **not** in `app.routes.ts` (auth is Keycloak-only).
- `AuthController` has no register endpoint—only login, logout, and `me`.

This looks like leftover code from an ASP.NET Identity flow, not the current auth model.

### 4. Account settings

The shell menu has **Account settings** with no `routerLink` or handler—a dead control.

### 5. Global search

`src/styles.scss` defines `.topbar-search`, but the shell topbar has no search input—styles only.

### 6. Release lifecycle (Active / Completed / Archived)

The enum and dashboard logic reference `Active`, `Completed`, and `Archived`, but **nothing ever sets status beyond `Draft`**. Every create path uses `ReleaseLifecycleStatus.Draft` only; there is no PATCH/status API or UI to promote or complete a release.

The release list copy mentions “in-progress,” but that state cannot be reached in code.

### 7. Pull request status in the UI

ADO PR status is fetched only when replacing stale PR rows during batch create (`TryGetGitPullRequestStatusAsync`). It is **not** persisted on `ReleasePullRequest` and **not** shown on the release detail table (only title + “Open in ADO”).

### 8. Editing and reassignment

| Entity | Missing capability |
|--------|-------------------|
| **Releases** | Edit title, sprint, or status after creation |
| **Teams** | Rename or change parent (create/delete only) |
| **Registered repos** | Move repo to another team (`Patch` only updates `serviceName`) |
| **Release PRs** | Manual remove from a release (only implicit cleanup when ADO PR is abandoned) |

### 9. Sidebar organization switcher

The footer org switcher stores selection in `sessionStorage` but does **not** scope repositories, releases, or catalog calls—it is display/context only.

### 10. Shared (non–per-user) application data

Only Azure organizations and PATs are tied to `UserId`. **Teams, releases, registered repositories, and app settings are global** for all logged-in users. There is no per-user or per-tenant isolation for release workflows.

`AppSettings` is a **single global row** (`SingletonId = 1`), not per user or per org.

### 11. Automated tests

- **Backend:** no test project in the solution.
- **Frontend:** only `src/app/app.spec.ts`, which still expects `Hello, azure-admin` in an `h1` while `app.html` is just `<router-outlet />`—likely broken/stale.

### 12. Documentation

`docs/` was only a `.gitkeep` placeholder before this file—no in-repo product roadmap or feature spec.

---

## Minor / infrastructure notes

- **PWA:** Service worker and `manifest.webmanifest` are wired in `angular.json` (build config; not a separate user-facing feature unless offline/install is a product goal).
- **CI:** Pipelines exist for frontend/backend; they do not imply missing app features.

### Summary of gaps

The **release PR batching + commit notes + ADO PAT/catalog** path is the mature part of the app. The largest **product** gaps are:

- Notifications (including PAT reminders)
- Release lifecycle beyond Draft
- PR status visibility
- Richer CRUD (teams / releases / repos)
- Account / register flows
- Global search
- Multi-user data isolation (if each Keycloak user should own their own teams/releases)

---

## Features that could be implemented or added

### Close the gaps (unfinished or stubbed today)

| Item | Why it matters |
|------|----------------|
| **In-app notifications** | Bell UI exists; backend always returns `unreadNotificationsCount: 0`. Add a store + API + panel (PR merged, PAT expiring, batch failures). |
| **PAT expiry reminders** | UI promises “renewal reminders” but only stores `patExpiresAt`. Tie into notifications or email. |
| **Release lifecycle** | `Active` / `Completed` / `Archived` exist in the model but releases never leave `Draft`. Add status transitions + UI (start release, complete, archive). |
| **PR status in UI** | ADO status is read only when replacing stale PRs. Persist `status` / `mergeStatus` and show it on release detail; optional background sync. |
| **Edit releases** | PATCH title, sprint, status, description after creation. |
| **Edit teams** | Rename team, change parent without delete/recreate. |
| **Reassign repositories** | Move a registered repo to another team. |
| **Remove PR from release** | DELETE endpoint + UI when ADO PR is abandoned or created by mistake. |
| **Account settings page** | Profile, theme default, notification prefs, default org. |
| **Remove or wire registration** | Delete dead `RegisterPage` + `/api/auth/register`, or document Keycloak self-registration only. |
| **Global search** | Styles exist; add search across releases, repos, teams, orgs. |
| **Org switcher that scopes data** | Filter repos/import/catalog by selected org, or label data clearly as global. |
| **Per-user or per-org tenancy** | Today teams/releases/settings are shared by all users; scope data if multiple squads use one instance. |
| **Per-user / per-org app settings** | Replace global `AppSettings` singleton if different squads need different Jira/commit rules. |
| **Tests** | API integration tests (releases, PAT, batch PR), frontend tests for critical flows; fix stale `app.spec.ts`. |
| **Product docs** | Expand `docs/` with setup, ADO PAT scopes, release workflow, Keycloak config. |

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
| **Keyboard shortcuts** | `g r` → releases, `n` → new release, etc. |
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
2. Notifications (including PAT expiry)  
3. Edit release / reassign repo / remove PR from release  
4. Org-scoped or per-user data (if multiple squads share one deploy)

### Medium effort, strong workflow value

5. “All dev PRs merged” gate before prod batch  
6. PR sync from ADO + webhooks  
7. Slack/Teams on batch complete  

### Polish & scale

8. RBAC + audit log  
9. API for CI  
10. Tests + docs  

---

## Next steps

Narrow this backlog by audience:

- **Single team, one ADO org** — prioritize lifecycle, PR status, and editing.  
- **Many teams, one instance** — prioritize tenancy, RBAC, and per-org settings.  
- **CI-driven releases** — prioritize API tokens and webhooks early.

Add effort estimates (S/M/L) per item when planning sprints.
