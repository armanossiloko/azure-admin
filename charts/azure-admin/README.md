# azure-admin Helm chart

Deploys the single azure-admin image (ASP.NET Core API serving the Angular SPA).
Postgres and Keycloak are **not** part of the chart; point it at existing instances.

## Image and chart

Both are built by `.github/workflows/build.yml`:

| Trigger | Image tags (`ghcr.io/<owner>/azure-admin`) | Chart |
|---|---|---|
| push to `master` with `feat`/`fix`/breaking commits | `sha-<short>`, `latest`, `<new version>` | pushed to `oci://ghcr.io/<owner>/charts/azure-admin` as the new version (default image tag = same version) |
| push to `master` with only `chore`/`docs`/`ci` commits | `sha-<short>`, `latest` | pushed as `<last release>-master.<run>.<short>` (default image tag `sha-<short>`) |
| pull request | built, not pushed | linted only |

The version is calculated by [semantic-release](https://semantic-release.gitbook.io) from the
[Conventional Commits](https://www.conventionalcommits.org) since the last release: `fix:` -> patch,
`feat:` -> minor, `feat!:` or a `BREAKING CHANGE:` footer -> major. The first release is `1.0.0`.
The git tag (`v1.2.3`) is the only source of the version; `Chart.yaml` and `package.json` hold placeholders.

## Install

The chart is stored as an OCI artifact in ghcr.io, not in a classic Helm repository, so there is no
`helm repo add` and no `index.yaml`. The GitHub package page therefore shows a generic `docker pull`
line; that is just how GitHub displays any OCI artifact. Use Helm directly (Helm 3.8+):

```bash
helm show values oci://ghcr.io/<owner>/charts/azure-admin --version 1.0.0
helm install azure-admin oci://ghcr.io/<owner>/charts/azure-admin --version 1.0.0 -f my-values.yaml
```

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

## Health endpoint

`GET /health` (checks the database) is served on a separate container port `8081` (`health`) and is
only reachable there; on the app port `8080` it returns 404, and the app itself returns 404 on `8081`.
The port is used by the startup and readiness probes (liveness only checks that the app port is open, so
a database outage does not restart pods). It is deliberately **not** in the Service or the Ingress.
Anyone who can reach the pod IP in the cluster can still call it, so use a NetworkPolicy if you need
to restrict that further.

## Argo CD

From the OCI registry. The registry has to be known to Argo CD as a Helm repository with OCI enabled
(no `oci://` prefix in `repoURL`); for a public package no credentials are needed:

```yaml
# Argo CD repository (Settings -> Repositories, or a declarative Secret with argocd.argoproj.io/secret-type: repository)
type: helm
name: azure-admin-charts
url: ghcr.io/<owner>/charts
enableOCI: "true"
```

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
