# Local test stack (optional)

Self-contained **Postgres + Keycloak + API + SPA** for local feature testing.

This stack is **additive**. It does **not** change:

- `docker-compose.yml` (existing external-Keycloak Compose)
- `.env.example`
- `Dockerfile` / `Dockerfile.backend` / `Dockerfile.frontend`
- published GHCR images or Helm chart defaults

CI builds only the root `Dockerfile` with selective `COPY`s, so nothing under `docker/dev/` is baked into production images.

## Run

From the repo root:

```bash
cp .env.dev.example .env.dev
docker compose -f docker-compose.dev.yml --env-file .env.dev up --build
```

| Service | URL |
|---------|-----|
| App | http://localhost:8080 |
| Keycloak admin | http://localhost:8081 (`admin` / `admin`) |
| Realm login user | `admin` / `admin` (realm `azure-admin`) |

Realm + confidential client: [`keycloak/azure-admin-realm.json`](keycloak/azure-admin-realm.json).

Stop:

```bash
docker compose -f docker-compose.dev.yml --env-file .env.dev down
```

Existing users of `docker compose up` (without `-f docker-compose.dev.yml`) are unaffected.
