#!/usr/bin/env bash
set -euo pipefail

# Starts a deployment for real and asserts what only a running one can answer.
#
#   scripts/smoke-deployment.sh compose      the Docker Compose deployment
#   scripts/smoke-deployment.sh kubernetes   the Helm chart, in an ephemeral kind cluster
#   scripts/smoke-deployment.sh all
#
# scripts/verify-deployment-assets.sh answers everything that can be read out of the files; this answers the rest —
# that the image builds and runs unprivileged on a read-only root filesystem, that it reads its mounted configuration
# and resolves its mounted secret, that it reaches the database, and that it then refuses to serve against a schema it
# does not recognize.
#
# That refusal is where both halves stop, deliberately. Neither the image nor the chart applies a schema, and the
# reviewed artifact that would establish one is #126's, so a deployment cannot be driven to readiness here without
# reproducing that artifact in a test script — which would make the smoke prove something the deployment does not
# contain. Everything up to the refusal is proven; readiness joins this script in the change that supplies the schema
# step.
#
# Both parts destroy and recreate their own state. Neither touches anything outside the checkout, the Docker daemon,
# and the kind cluster it creates and deletes.

readonly compose_project='mailmcp-smoke'
# compose.yaml names its volume globally rather than letting Compose prefix it with the project, so the project name
# alone does not isolate this run from a developer's data. This is what does.
readonly smoke_postgres_volume='mailmcp-smoke-postgres-data'
readonly kind_cluster='mailmcp-smoke'
readonly runtime_image='mailmcp:smoke'
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

build_the_image() {
  report 'Building the image'

  docker build --target runtime --file deploy/docker/Dockerfile --tag "$runtime_image" . >/dev/null

  pass 'The runtime image built.'
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
  trap 'compose_command down --volumes --remove-orphans >/dev/null 2>&1 || true; remove_smoke_workspace' RETURN

  compose_command down --volumes --remove-orphans >/dev/null 2>&1 || true

  compose_command up --detach --wait postgres >/dev/null
  pass 'PostgreSQL reports healthy, so its initialization script created the role and the database.'

  # Reaching this message is what the run proves. The host got far enough to read its mounted configuration directory,
  # resolve the mounted database password, open a connection as the unprivileged role the initialization script
  # created, and read the migration history — and then refused to serve, because no schema has been applied. A host
  # that applied one while starting could never report this, which is why the refusal is the assertion rather than an
  # obstacle to one.
  compose_command up --detach mailmcp >/dev/null
  wait_until 'The host reaches the database and refuses to serve against a schema it does not recognize.' 30 \
    compose_logs_mention 'DatabaseSchemaOutOfDateException'

  local container_id
  container_id="$(compose_command ps --all --quiet mailmcp)"

  [[ "$(docker inspect "$container_id" --format '{{.Config.User}}')" == '1654' ]] \
    || abort 'The container is not running as the unprivileged account.'
  pass 'It runs as the unprivileged account.'

  [[ "$(docker inspect "$container_id" --format '{{.HostConfig.ReadonlyRootfs}}')" == 'true' ]] \
    || abort 'The root filesystem is writable.'
  pass 'Its root filesystem is read-only.'

  # The refusal ends the process, and `restart: unless-stopped` starts it again — which is what a deployment waiting
  # for an operator to apply a schema should do, and why a restart having happened is the evidence that the process
  # ended itself. The service is stopped first so the recorded exit code is a settled one rather than a race against
  # the next restart.
  #
  # Only 137 is asserted against, deliberately. That is SIGKILL, which is what a container the daemon had to stop by
  # force reports. The code a failed start *does* produce is whatever the runtime's abort path yields, and that
  # differs by environment — 134 outside a container, 139 in this image — so asserting a particular one would be
  # asserting a property of the .NET host rather than of the deployment.
  compose_command stop --timeout 60 mailmcp >/dev/null
  local exit_code restart_count
  exit_code="$(docker inspect "$container_id" --format '{{.State.ExitCode}}')"
  restart_count="$(docker inspect "$container_id" --format '{{.RestartCount}}')"
  [[ "$restart_count" != '0' ]] || abort 'The container never restarted, so the refusal did not end the process.'
  [[ "$exit_code" != '137' ]] || abort 'The container was killed by the daemon rather than ending on its own.'
  pass "The refusal ended the process itself (exit code ${exit_code}, restarted ${restart_count} time(s) by the restart policy)."
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

  kind load docker-image "$runtime_image" --name "$kind_cluster" >/dev/null
  pass 'The image is loaded into the cluster, so nothing is pulled from a registry.'

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

  helm install "$helm_release" deploy/helm/mailmcp --namespace "$kubernetes_namespace" "${chart_values[@]}" >/dev/null
  pass 'The chart installed.'

  # The same refusal the Compose half observes, reached through the chart's own wiring: the ConfigMap it mounts, the
  # Secret it references, and the connection string it renders. Readiness never arrives while no schema exists, and
  # that is the state this run asserts rather than works around.
  wait_until 'The pod reaches the database and refuses to serve against a schema it does not recognize.' 60 \
    pod_logs_mention 'DatabaseSchemaOutOfDateException'

  # An upgrade that changes nothing must render the same objects rather than churn. Compared as rendered manifests,
  # because the rollout it produces cannot become ready without a schema.
  local first_manifest second_manifest
  first_manifest="$(helm get manifest "$helm_release" --namespace "$kubernetes_namespace")"
  helm upgrade "$helm_release" deploy/helm/mailmcp --namespace "$kubernetes_namespace" "${chart_values[@]}" >/dev/null
  second_manifest="$(helm get manifest "$helm_release" --namespace "$kubernetes_namespace")"
  [[ "$first_manifest" == "$second_manifest" ]] || abort 'A repeated upgrade changed the rendered objects.'
  pass 'A repeated upgrade rendered the same objects.'

  helm uninstall "$helm_release" --namespace "$kubernetes_namespace" >/dev/null
  wait_until 'Uninstalling removed every object the chart owns.' 60 chart_objects_are_gone
}

########################################################################################################################

main() {
  require_command git
  require_command docker

  case "${1:-all}" in
    compose)
      build_the_image
      smoke_the_compose_deployment
      ;;
    kubernetes)
      require_command kind
      require_command kubectl
      require_command helm
      build_the_image
      smoke_the_kubernetes_deployment
      ;;
    all)
      require_command kind
      require_command kubectl
      require_command helm
      build_the_image
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
