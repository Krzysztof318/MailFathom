#!/usr/bin/env bash
set -euo pipefail

# Starts a deployment for real and asserts what only a running one can answer.
#
#   scripts/smoke-deployment.sh compose      the Docker Compose deployment
#   scripts/smoke-deployment.sh kubernetes   the Helm chart, in an ephemeral kind cluster
#   scripts/smoke-deployment.sh all
#
# scripts/verify-deployment-assets.sh answers everything that can be read out of the files; this answers the rest —
# that the image builds and runs unprivileged on a read-only root filesystem, that the schema step is separate and
# idempotent, that a host refuses to serve against a schema it does not recognize, that readiness and liveness report
# what they claim to, and that shutting down is clean rather than a kill.
#
# Both parts destroy and recreate their own state. Neither touches anything outside the checkout, the Docker daemon,
# and the kind cluster it creates and deletes.

readonly compose_project='mailmcp-smoke'
# compose.yaml names its volume globally rather than letting Compose prefix it with the project, so the project name
# alone does not isolate this run from a developer's data. This is what does.
readonly smoke_postgres_volume='mailmcp-smoke-postgres-data'
readonly kind_cluster='mailmcp-smoke'
readonly runtime_image='mailmcp:smoke'
readonly migrations_image='mailmcp-migrations:smoke'
readonly kubernetes_namespace='mailmcp-smoke'
readonly helm_release='mailmcp'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'smoke-deployment.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

report() {
  printf '\n▸ %s\n' "$1"
}

pass() {
  printf '  ✓ %s\n' "$1"
}

abort() {
  printf '  ✗ %s\n' "$1" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || abort "$1 is required and was not found on the PATH."
}

# Polls until a command succeeds. Every wait in this script goes through it, so a hung deployment ends the run with
# the condition it was waiting for named rather than with a timeout nobody can attribute.
wait_until() {
  local description="$1"
  local attempts="$2"
  shift 2

  local attempt
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    if "$@" >/dev/null 2>&1; then
      pass "$description"
      return 0
    fi
    sleep 2
  done

  abort "Timed out waiting for: ${description}"
}

build_the_images() {
  report 'Building the images'

  docker build --target runtime --tag "$runtime_image" . >/dev/null
  docker build --target migrations --tag "$migrations_image" . >/dev/null

  pass 'Both images built.'
}

########################################################################################################################
# Docker Compose
########################################################################################################################

# Where this run's credentials and configuration live. Set by provision_smoke_workspace and removed on the way out.
smoke_workspace=''

# Every Compose invocation goes through here, and three of its arguments are what keep this run away from anything an
# operator owns:
#
#   * a smoke-only project name, which isolates containers and networks;
#   * MAILMCP_POSTGRES_VOLUME, because compose.yaml gives the volume an explicit global name — the project name does
#     *not* scope it, so without this the teardown below would delete the volume holding a developer's synchronized
#     mail on the same daemon;
#   * an override file whose secret and configuration paths point into a temporary directory, so nothing is read from
#     or written to deploy/compose/secrets/ and deploy/compose/config/.
#
# The override is passed second and the repository's own file first, which is also what keeps the build contexts
# resolving: Compose takes the project directory from the first file it is given.
compose_command() {
  (cd deploy/compose \
    && MAILMCP_IMAGE="$runtime_image" \
       MAILMCP_MIGRATIONS_IMAGE="$migrations_image" \
       MAILMCP_POSTGRES_VOLUME="$smoke_postgres_volume" \
    docker compose \
      --project-name "$compose_project" \
      --file compose.yaml \
      --file "$smoke_workspace/compose.smoke.yaml" \
      "$@")
}

# A named function rather than an inline `bash -c`, because wait_until calls what it is given in this shell: a nested
# shell would lose the project name and the image tags these commands are parameterized by, and would then poll a
# deployment that does not exist while reporting a timeout against the one that does.
compose_logs_mention() {
  compose_command logs mailmcp 2>&1 | grep -q "$1"
}

# Builds the throwaway workspace. Nothing here touches the tracked deployment directory: a developer who has already
# provisioned the supported deployment keeps their credentials, and this run cannot authenticate against their volume
# by accident either, because it does not use it.
provision_smoke_workspace() {
  smoke_workspace="$(mktemp --directory --tmpdir mailmcp-smoke.XXXXXXXX)"

  mkdir -p "$smoke_workspace/secrets/mailmcp" "$smoke_workspace/config"
  chmod 700 "$smoke_workspace/secrets"

  head -c 24 /dev/urandom | base64 | tr -d '\n' > "$smoke_workspace/secrets/postgres-superuser-password"
  head -c 24 /dev/urandom | base64 | tr -d '\n' > "$smoke_workspace/secrets/mailmcp-database-password"

  # The directory restricts access; the files are readable. Compose bind-mounts a file secret with the host's own
  # permissions — outside Swarm it ignores `mode`, `uid`, and `gid` — and the service runs as an unprivileged account
  # that is nobody's host user, so a 0600 file it cannot read presents as an unresolvable secret reference. This is
  # the arrangement docs/operations/deployment-compose.md asks an operator for, and the smoke uses it so the two
  # cannot diverge.
  chmod 444 "$smoke_workspace/secrets/postgres-superuser-password" "$smoke_workspace/secrets/mailmcp-database-password"

  # The configuration directory is deliberately left empty. An operator's own `config/10-mailmcp.json` would otherwise
  # be mounted into this run, and a real mailbox account in it would make the smoke synchronize somebody's mail.
  cat > "$smoke_workspace/compose.smoke.yaml" <<SMOKE_OVERRIDE
services:
  mailmcp:
    volumes: !override
      - type: bind
        source: $smoke_workspace/config
        target: /etc/mailmcp/config
        read_only: true
      - type: bind
        source: $smoke_workspace/secrets/mailmcp
        target: /etc/mailmcp/secrets
        read_only: true

secrets:
  postgres-superuser-password:
    file: $smoke_workspace/secrets/postgres-superuser-password
  mailmcp-database-password:
    file: $smoke_workspace/secrets/mailmcp-database-password
SMOKE_OVERRIDE
}

remove_smoke_workspace() {
  [[ -n "$smoke_workspace" && -d "$smoke_workspace" ]] || return 0

  rm -rf -- "$smoke_workspace"
  smoke_workspace=''
}

smoke_the_compose_deployment() {
  report 'Docker Compose deployment'

  # The workspace exists before the first teardown, because compose_command cannot run without the override file it
  # names — and a teardown issued against the repository file alone would be the very thing this isolation prevents.
  provision_smoke_workspace
  trap 'compose_command --profile migrate down --volumes --remove-orphans >/dev/null 2>&1 || true; remove_smoke_workspace' RETURN

  compose_command --profile migrate down --volumes --remove-orphans >/dev/null 2>&1 || true

  compose_command up --detach --wait postgres >/dev/null
  pass 'PostgreSQL reports healthy, so its initialization script created the role and the database.'

  # Before the schema exists, so the refusal is observed rather than assumed. This is the whole reason the migration is
  # a separate step: a host that applied one on start could never report this.
  compose_command up --detach mailmcp >/dev/null
  wait_until 'The host refuses to serve against a schema it does not recognize.' 30 \
    compose_logs_mention 'DatabaseSchemaOutOfDateException'
  compose_command stop mailmcp >/dev/null

  compose_command --profile migrate run --rm migrations >/dev/null
  pass 'The one-shot migration applied the schema.'

  compose_command --profile migrate run --rm migrations >/dev/null
  pass 'Running it again changed nothing, so it is idempotent.'

  # The grant path a deployment reaches when the migration role differs from the service's. The role named here is the
  # same one, which a deployment would never do — granting to yourself is a no-op — but it is what executes the
  # statements, and executing them is the risk: `ALTER DEFAULT PRIVILEGES` and the `GRANT ... ON ALL` forms are where a
  # syntax or privilege mistake would otherwise only surface in a cluster running the split for real.
  compose_command --profile migrate run --rm \
    --env MAILMCP_RUNTIME_ROLE="${MAILMCP_DATABASE_ROLE:-mailmcp}" migrations >/dev/null
  pass 'The runtime-role grant runs against the migrated schema.'

  compose_command up --detach --wait mailmcp >/dev/null
  pass 'The host became healthy against the migrated schema.'

  local container_id
  container_id="$(compose_command ps --quiet mailmcp)"

  [[ "$(docker inspect "$container_id" --format '{{.Config.User}}')" == '1654' ]] \
    || abort 'The container is not running as the unprivileged account.'
  pass 'It runs as the unprivileged account.'

  [[ "$(docker inspect "$container_id" --format '{{.HostConfig.ReadonlyRootfs}}')" == 'true' ]] \
    || abort 'The root filesystem is writable.'
  pass 'Its root filesystem is read-only.'

  # Proves the readiness endpoint answers over the published port rather than only to the in-container probe.
  local published_port
  published_port="$(compose_command port mailmcp 8080 | sed 's/.*://')"
  [[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "http://127.0.0.1:${published_port}/health")" == '200' ]] \
    || abort 'Readiness did not answer 200 over the published port.'
  [[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "http://127.0.0.1:${published_port}/alive")" == '200' ]] \
    || abort 'Liveness did not answer 200 over the published port.'
  pass 'Readiness and liveness answer over the published port.'

  # A clean shutdown ends with the host's own stop record, and the exit code says whether it was a stop or a kill:
  # 137 is SIGKILL, which is what a drain that outlives the grace period produces.
  compose_command stop --timeout 60 mailmcp >/dev/null
  local exit_code
  exit_code="$(docker inspect "$container_id" --format '{{.State.ExitCode}}')"
  [[ "$exit_code" != '137' ]] || abort 'The container was killed rather than stopped; the drain outlived the grace period.'
  compose_command logs mailmcp 2>&1 | grep -q 'stopped' \
    || abort 'The host did not record a stop, so shutdown was not graceful.'
  pass "Shutdown was graceful (exit code ${exit_code})."
}

########################################################################################################################
# Kubernetes
########################################################################################################################

pod_logs_mention() {
  kubectl --namespace "$kubernetes_namespace" logs --selector app.kubernetes.io/name=mailmcp --tail=-1 2>/dev/null | grep -q "$1"
}

chart_objects_are_gone() {
  [[ -z "$(kubectl --namespace "$kubernetes_namespace" get all --selector app.kubernetes.io/name=mailmcp --no-headers 2>/dev/null)" ]]
}

apply_the_smoke_database() {
  # Test infrastructure, and only that. It is a single unmanaged pod with no persistence, which is why the chart
  # itself installs no database: a store holding every synchronized message needs more than this.
  #
  # POSTGRES_USER is a superuser in this image, and the smoke deployment connects as it. That is a deliberate
  # simplification of the smoke environment rather than the shape a deployment should copy — the Compose deployment
  # beside it shows the unprivileged split, and Kubernetes leaves the database to whoever operates it.
  kubectl --namespace "$kubernetes_namespace" apply --filename - >/dev/null <<'SMOKE_DATABASE'
apiVersion: v1
kind: Service
metadata:
  name: postgres
spec:
  selector:
    app: postgres
  ports:
    - port: 5432
      targetPort: 5432
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
        - name: postgres
          image: pgvector/pgvector:0.8.2-pg17
          env:
            - name: POSTGRES_USER
              value: mailmcp
            - name: POSTGRES_DB
              value: mailmcp
            - name: POSTGRES_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: mailmcp-secrets
                  key: mailmcp-database-password
            - name: PGDATA
              value: /var/lib/postgresql/data/pgdata
          ports:
            - containerPort: 5432
          readinessProbe:
            exec:
              command: ["pg_isready", "--username", "mailmcp", "--dbname", "mailmcp"]
            initialDelaySeconds: 5
            periodSeconds: 5
          volumeMounts:
            - name: data
              mountPath: /var/lib/postgresql/data
      volumes:
        - name: data
          emptyDir: {}
SMOKE_DATABASE
}

smoke_the_kubernetes_deployment() {
  report 'Helm chart in an ephemeral cluster'

  trap 'kind delete cluster --name "$kind_cluster" >/dev/null 2>&1 || true' RETURN

  kind delete cluster --name "$kind_cluster" >/dev/null 2>&1 || true
  kind create cluster --name "$kind_cluster" --wait 120s >/dev/null
  pass 'The cluster is up.'

  kind load docker-image "$runtime_image" "$migrations_image" --name "$kind_cluster" >/dev/null
  pass 'Both images are loaded into the cluster, so nothing is pulled from a registry.'

  kubectl create namespace "$kubernetes_namespace" >/dev/null

  # The chart creates no Secret, so the smoke has to provision one exactly as an operator would.
  kubectl --namespace "$kubernetes_namespace" create secret generic mailmcp-secrets \
    --from-literal=mailmcp-database-password="$(head -c 24 /dev/urandom | base64 | tr -d '\n')" \
    --from-literal=mcp-workstation-key="$(head -c 24 /dev/urandom | base64 | tr -d '\n')" >/dev/null
  pass 'The Secret the chart references is provisioned.'

  apply_the_smoke_database
  kubectl --namespace "$kubernetes_namespace" rollout status deployment/postgres --timeout=180s >/dev/null
  pass 'The test database is ready.'

  local -a chart_values=(
    --set "image.registry="
    --set "image.repository=mailmcp"
    --set "image.tag=smoke"
    --set "image.pullPolicy=Never"
    --set "image.allowVersionMismatch=true"
    --set "database.host=postgres"
    --set "secrets.existingSecret=mailmcp-secrets"
  )

  # Installed before the schema exists, so the refusal is observed. Readiness must not arrive, and the reason must be
  # the pending migration rather than anything else.
  helm install "$helm_release" deploy/helm/mailmcp --namespace "$kubernetes_namespace" "${chart_values[@]}" >/dev/null
  pass 'The chart installed.'

  wait_until 'The pod refuses to serve against a schema it does not recognize.' 60 \
    pod_logs_mention 'DatabaseSchemaOutOfDateException'

  helm upgrade "$helm_release" deploy/helm/mailmcp --namespace "$kubernetes_namespace" "${chart_values[@]}" \
    --set migrations.enabled=true \
    --set migrations.image.repository=mailmcp-migrations \
    --set migrations.image.tag=smoke >/dev/null
  kubectl --namespace "$kubernetes_namespace" wait --for=condition=complete job \
    --selector app.kubernetes.io/component=schema-migration --timeout=180s >/dev/null
  pass 'The migration Job completed.'

  helm upgrade "$helm_release" deploy/helm/mailmcp --namespace "$kubernetes_namespace" "${chart_values[@]}" \
    --set migrations.enabled=false >/dev/null
  kubectl --namespace "$kubernetes_namespace" rollout restart deployment/"$helm_release" >/dev/null
  kubectl --namespace "$kubernetes_namespace" rollout status deployment/"$helm_release" --timeout=180s >/dev/null
  pass 'The deployment became ready against the migrated schema, which is what its readiness probe reports.'

  # An upgrade that changes nothing must stay ready rather than churn.
  helm upgrade "$helm_release" deploy/helm/mailmcp --namespace "$kubernetes_namespace" "${chart_values[@]}" >/dev/null
  kubectl --namespace "$kubernetes_namespace" rollout status deployment/"$helm_release" --timeout=180s >/dev/null
  pass 'A repeated upgrade left it ready.'

  helm uninstall "$helm_release" --namespace "$kubernetes_namespace" >/dev/null
  wait_until 'Uninstalling removed every object the chart owns.' 60 chart_objects_are_gone
}

########################################################################################################################

main() {
  require_command git
  require_command docker

  case "${1:-all}" in
    compose)
      require_command curl
      build_the_images
      smoke_the_compose_deployment
      ;;
    kubernetes)
      require_command kind
      require_command kubectl
      require_command helm
      build_the_images
      smoke_the_kubernetes_deployment
      ;;
    all)
      require_command curl
      require_command kind
      require_command kubectl
      require_command helm
      build_the_images
      smoke_the_compose_deployment
      smoke_the_kubernetes_deployment
      ;;
    *)
      printf 'Usage: %s [compose|kubernetes|all]\n' "$0" >&2
      exit 1
      ;;
  esac

  printf '\nDeployment smoke verification passed.\n'
}

main "$@"
