# Agent guidelines — Azure Admin

These rules apply to **any** coding agent working in this repository (Cursor, Claude Code, Copilot, Codex, etc.).

## Backward compatibility (hard rule)

Existing deployments and published images must keep working **without** operator reconfiguration.

1. **Do not break published contracts**
   - GHCR images (`Dockerfile`, `Dockerfile.backend`, `Dockerfile.frontend`) and their default ports, env var names, and health endpoints.
   - Helm chart values and existing `docker-compose.yml` / `.env.example` keys.
   - Public API request/response shapes that clients already send or read (JSON property names, enums, status codes). Prefer additive fields and optional parameters.

2. **Seamless upgrades**
   - Database changes must be EF Core migrations that apply on startup.
   - Existing rows must keep working with **zero** user clicks or re-saves (read path accepts old data; write path may normalize later).
   - Prefer widening columns / additive tables over destructive renames. If a rename is required, keep the old column or dual-read/dual-write until safe.
   - Settings and auth: keep legacy fields or accept both old and new payloads during transition (see Jira project keys: `jiraProjectKey` + `jiraProjectKeys`).

3. **Local / test stacks are additive only**
   - Optional environments (e.g. `docker-compose.dev.yml`, `docker/dev/`) must not modify production Compose, Dockerfiles, CI image build, or `.env.example` defaults.
   - Document them in their own folder (`docker/dev/README.md`), not by rewriting existing run instructions.

4. **Before changing shared infra**
   - Ask: would an existing user of `ghcr.io/armanossiloko/azure-admin` or `docker compose up` need to change env vars, ports, or Keycloak client settings?
   - If yes, redesign so the change is opt-in, or provide a seamless migration with defaults that preserve old behavior.

5. **When in doubt**
   - Additive > breaking.
   - Dual-read old + new > force migrate.
   - New compose file > edit the one people already run.
