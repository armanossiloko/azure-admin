# azure-admin Helm chart

Deploys the single azure-admin image (ASP.NET Core API serving the Angular SPA).
Postgres and Keycloak are **not** part of the chart; point it at existing instances.

## Image and chart

Both are built by `.github/workflows/build.yml`:

| Trigger | Image tags (`ghcr.io/<owner>/azure-admin`) | Chart |
|---|---|---|
| push to `master` | `sha-<short>`, `latest` | linted only |
| tag `v1.2.3` | `sha-<short>`, `1.2.3` | pushed to `oci://ghcr.io/<owner>/charts/azure-admin` as version `1.2.3` (default image tag `1.2.3`) |
| pull request | built, not pushed | linted only |

## Required values

```yaml
config:
  postgres:
    host: postgres.db.svc
  keycloak:
    authority: https://keycloak.example.com/realms/myrealm
    clientId: azure-admin
secrets:
  existingSecret: azure-admin-secrets   # keys: postgres-password, keycloak-client-secret
```

Instead of `existingSecret`, `secrets.postgresPassword` / `secrets.keycloakClientSecret` create a Secret from
plain values — fine for testing, not for a GitOps repo.

## Keycloak

Register these redirect URIs for the client (using the public host of the ingress):

- `https://<host>/signin-oidc`
- `https://<host>/signout-callback-oidc`

## Argo CD

From the OCI registry:

```yaml
source:
  repoURL: ghcr.io/<owner>/charts
  chart: azure-admin
  targetRevision: 1.2.3
  helm:
    valuesObject: { ... }
```

or from git (then pin `image.tag`, e.g. `sha-abc1234`):

```yaml
source:
  repoURL: https://github.com/<owner>/azure-admin.git
  path: charts/azure-admin
  targetRevision: master
```
