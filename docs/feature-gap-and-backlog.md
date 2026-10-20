# Azure Admin — Feature gaps & backlog

Setup, Keycloak, ADO PAT scopes, and the release workflow: [setup.md](setup.md). Product overview: [README](../README.md).

In-app notifications cover PAT expiry, PR merged (on status sync), and batch failures. **Teams / Slack / Discord** outgoing webhooks can post one summary per PR batch (Settings → Chat notifications). There is no email digest.

---

## Remaining product gap

**Shared application data** — Azure orgs and PATs are per user. Teams, releases, registered repositories, and `AppSettings` (single global row) are shared by all logged-in users. There is no per-user or per-tenant isolation for release workflows. Acceptable for a single-squad instance; revisit if multiple squads share one deploy.

---

## Ideas (not committed)

### Release & PR workflow

| Item | Notes |
|------|--------|
| Configurable “all PRs ready” gate | Basic Dev→Master-before-Master→Prod gate exists; make it optional/per-release |
| Release checklist | Custom steps per phase (QA sign-off, change ticket, …) |
| Release templates | Preset title/sprint, default teams, branches, repo sets |
| Scheduled / recurring releases | Sprint-based auto-create drafts |
| Auto-advance when phase complete | Unlock next phase or complete release when all PRs in a phase are done |
| Richer PR sync from ADO | Titles, reviewers (status sync already exists) |
| Bulk abandon / recreate | Select repos in a phase and re-run batch |
| Custom phases | Beyond Dev→Master / Master→Prod / Prod→Dev |
| PR description from release notes | Push markdown into ADO PR on create/update |
| Work item linking | ADO / Jira at release level, not only in commits |
| Release comparison | Diff vs previous sprint/release |
| Hotfix release type | Narrow repo set + different branch defaults |

### Commit notes & integrations

| Item | Notes |
|------|--------|
| More Jira patterns | Smart commits, keys in branch names |
| Azure Boards (`AB#123`) | Same idea as Jira links |
| Confluence / wiki export | Publish notes to a page template |
| Slack / Teams / Discord | Done: Settings → Chat notifications (per-phase created/merged toggles, templates) |
| Changelog formats | JSON, HTML, PDF, Keep a Changelog |
| Commit filters | Exclude bots/merges/paths; include only `feat`/`fix` |
| Semantic version suggestion | Infer from conventional commits |
| Co-author / signed-off-by | Show in enriched commit display |
| Diff / file list per repo | From ADO compare API |

### Azure DevOps & credentials

| Item | Notes |
|------|--------|
| PAT health dashboard | Expiring soon, missing PAT, last successful call |
| PAT scope validator | Test required scopes before save |
| Federated auth | OIDC / managed identity instead of long-lived PATs |
| Multi-org release | One release spanning repos in different ADO orgs |
| Repo sync from ADO | Flag renamed/deleted registrations |
| Branch existence check | Before batch create |
| Policy awareness | Warn if branch policies block complete |
| Import teams from ADO | Map ADO teams/projects → app teams |
| ADO webhooks | Push PR status updates instead of poll |

### Teams, repos & catalog

| Item | Notes |
|------|--------|
| Team hierarchy tree UI | Instead of flat parent column |
| Repo tags / labels | Filter batch selection |
| Default repos per team | Pre-select in release-create |
| Duplicate registration guard | Hint when repo exists on another team |
| Bulk import improvements | Remember last project; import all in project |
| Service alias templates | Repo name → display name rules |

### Dashboard & navigation

| Item | Notes |
|------|--------|
| Actionable dashboard | “PRs needing attention” → filtered list |
| Richer release list filters | Status, sprint, team, date range |
| Activity filters | By kind |
| More keyboard shortcuts | e.g. `g r` releases, `n` new release (`/` and `Ctrl+K` search exist) |
| Favorites | Pin releases or repos |

### Security & operations

| Item | Notes |
|------|--------|
| RBAC | Admin / operator / read-only via Keycloak roles |
| Audit log | Who ran batches, changed settings, deleted releases |
| API tokens for CI | Headless “create release PRs” from pipeline |
| Rate limit / retry | Safer ADO batch calls |
| Richer readiness | DB, Keycloak, ADO in probes |
| OpenTelemetry / metrics | Batch duration, ADO error rates |
| Backup / export | Release history JSON |

### UX & platform

| Item | Notes |
|------|--------|
| Onboarding wizard | Org → PAT → team → repo → first release |
| Stronger empty states | One-click setup everywhere |
| Offline / PWA polish | Cached read-only list (service worker already configured) |
| i18n | e.g. German/English |
| Accessibility pass | Focus traps, table semantics, ARIA on wizards |
| Mobile release detail | Card layout on small screens |
| Softer deletes / undo | Restore archived releases |

### Developer experience

| Item | Notes |
|------|--------|
| OpenAPI client for Angular | Generated types instead of hand-rolled DTOs |
| E2E tests | Playwright with ADO mock |
| Local ADO mock | WireMock / recorded fixtures |
| Feature flags | Jira, conventional commits, phases per env |
| Seed data script | Demo teams/repos/releases |
| Deeper API/UI tests | Beyond current xUnit starter + `app.spec.ts` |

---

## Prioritization hints

| Audience | Lean toward |
|----------|-------------|
| Single team, one ADO org | Workflow polish, Slack/Teams, checklists |
| Many teams, one instance | Tenancy, RBAC, per-org settings |
| CI-driven releases | API tokens, webhooks |
