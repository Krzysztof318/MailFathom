#!/usr/bin/env bash
set -euo pipefail

# Static verification of everything under deploy/.
#
# Nothing here starts a container, reaches a registry, or needs a cluster: it answers the questions that can be
# answered by reading the files, which is what makes it cheap enough to run in the implementation loop. The questions
# that need something running — that the image builds, that the Compose stack comes up, that the chart installs into a
# cluster — belong to the manually dispatched `Deployment assets` workflow.
#
# It checks three things a reader would otherwise have to check by hand on every change:
#
#   * the image definition still holds the properties #123 requires of it — pinned bases, an unprivileged account, no
#     credential baked in;
#   * the Compose deployment renders and its GHCR overlay still refuses to render without a deliberate
#     acknowledgement;
#   * the chart lints, renders identically twice, and its schema still rejects the values that must never install.
#
# `helm` is used from the PATH when it is there, and from a pinned container image otherwise, so a developer who has
# not installed it still gets the same verdict.

readonly helm_container_image='alpine/helm:3.19.0'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-deployment-assets.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

readonly chart_directory='deploy/helm/mailfathom'
readonly compose_directory='deploy/compose'
readonly dockerfile='deploy/docker/Dockerfile'
# The ignore-file lives beside the Dockerfile rather than at the context root. Docker looks for
# `<dockerfile>.dockerignore` first and prefers it over the root one, so the build context stays bounded by a file that
# travels with the definition that uses it.
readonly dockerignore_file='deploy/docker/Dockerfile.dockerignore'

failures=0

report() {
  printf '\n▸ %s\n' "$1"
}

pass() {
  printf '  ✓ %s\n' "$1"
}

fail() {
  printf '  ✗ %s\n' "$1" >&2
  failures=$((failures + 1))
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    printf '%s is required and was not found on the PATH.\n' "$1" >&2
    exit 1
  }
}

# Mounts deploy/helm rather than the repository root, so a chart path is the same string whichever way helm is run.
helm_command() {
  if command -v helm >/dev/null 2>&1; then
    (cd deploy/helm && helm "$@")
  else
    docker run --rm --volume "$repository_root/deploy/helm:/charts" --workdir /charts "$helm_container_image" "$@"
  fi
}

# Asserts that a command fails, and that its output names the reason. A guard that stopped rejecting what it exists to
# reject would otherwise pass this script silently: the render would simply succeed.
expect_rejection() {
  local description="$1"
  local expected_reason="$2"
  shift 2

  local output
  if output="$("$@" 2>&1)"; then
    fail "$description — it was accepted."
    return
  fi

  if [[ "$output" != *"$expected_reason"* ]]; then
    fail "$description — it was rejected, but not for the expected reason. Output: ${output}"
    return
  fi

  pass "$description"
}

verify_the_image_definition() {
  report 'Dockerfile'

  # A floating base tag makes the image a function of when it was built rather than of what was reviewed.
  if grep -nE '^ARG [A-Z_]*IMAGE=.*:(latest|[0-9]+\.[0-9]+)$' "$dockerfile" >/dev/null; then
    fail 'A base image is pinned to a floating tag. Pin an explicit patch version or a digest.'
  else
    pass 'Every base image is pinned to an explicit version.'
  fi

  # The runtime stage must hand the process to an unprivileged account before its entrypoint.
  if awk '/^FROM .* AS runtime$/,0' "$dockerfile" | grep -qE '^USER '; then
    pass 'The runtime stage drops to an unprivileged account.'
  else
    fail 'The runtime stage never issues USER, so the service would run as root.'
  fi

  # Nothing in this image may apply a schema. The migration artifact and the image that runs it are #126's, and an
  # entrypoint that could reach a database with DDL is exactly what "the host never applies migrations" excludes.
  if grep -qE 'dotnet ef|migrations script|apply-schema|psql' "$dockerfile"; then
    fail 'The Dockerfile builds or carries a schema step. The reviewed schema artifact belongs to #126.'
  else
    pass 'The image carries no schema tool and no SQL.'
  fi

  # A credential in a build argument ends up in the image history, where `docker history` reads it back.
  if grep -nEi '^(ARG|ENV) .*(PASSWORD|SECRET|TOKEN|APIKEY|API_KEY|CREDENTIAL)' "$dockerfile" >/dev/null; then
    fail 'A build argument or environment variable in the Dockerfile names a credential. Image history is readable.'
  else
    pass 'No credential is named by a build argument or environment variable.'
  fi

  if [[ -f "$dockerignore_file" ]] && grep -qE '^\*\*$' "$dockerignore_file"; then
    pass 'The ignore-file excludes everything before allowing anything back.'
  else
    fail "${dockerignore_file} does not start from an exclude-everything rule, so a new file at the repository root would reach the build context."
  fi

  # The identifier is asserted rather than merely the label's presence: a label naming terms other than the root
  # LICENSE's is the failure worth catching, and a registry reports it to everyone who pulls the image.
  if grep -qE '^[^#]*org\.opencontainers\.image\.licenses="Apache-2\.0"' "$dockerfile"; then
    pass 'The image declares its Apache-2.0 license.'
  else
    fail 'The Dockerfile carries no org.opencontainers.image.licenses="Apache-2.0" label, so a pulled image names no terms.'
  fi

  # The label is the claim; LICENSE and NOTICE are the terms themselves, and the exclude-everything rule above means
  # they reach the image only because the ignore-file names them. Without them the publish inside the build fails on
  # Host.csproj's own check, which is a slower and less obvious way to learn that the allow-list lost a line.
  if grep -qx '!/LICENSE' "$dockerignore_file" && grep -qx '!/NOTICE' "$dockerignore_file"; then
    pass 'The build context carries LICENSE and NOTICE.'
  else
    fail "${dockerignore_file} excludes LICENSE or NOTICE, so the publish inside the image build has none to copy."
  fi

  # The publish has to receive both of continuous integration's version inputs, or the assemblies inside the image
  # report a build the labels beside them contradict. This is the half a running process publishes, and it is the one
  # a reader of a support report has.
  if grep -q 'p:SourceRevisionId=' "$dockerfile" && grep -q 'p:VersionSuffix=' "$dockerfile"; then
    pass 'The publish stamps the revision and the prerelease suffix into the assemblies.'
  else
    fail 'The Dockerfile publishes without SourceRevisionId or VersionSuffix, so the image cannot report which build it is.'
  fi

  # Every build path in the repository has to name the version, because the ARG default is a placeholder rather than a
  # version. A path that stopped passing it would keep building and would ship an image labelled 0.0.0-unversioned.
  local unnamed_build_paths=()
  local build_path
  for build_path in scripts/smoke-deployment.sh .github/workflows/deployment-assets.yml; do
    if grep -q 'read-declared-version.sh' "$build_path"; then
      continue
    fi
    unnamed_build_paths+=("$build_path")
  done

  if [[ "${#unnamed_build_paths[@]}" -eq 0 ]]; then
    pass 'Every build path names the image after the declared version.'
  else
    fail "These build paths name no version, so they would ship the placeholder: ${unnamed_build_paths[*]}"
  fi
}

verify_the_compose_deployment() {
  report 'Docker Compose'

  # `docker compose config` reads the secret *declarations* but not the files behind them, so verification needs no
  # credential to exist. The paths are created empty and removed again.
  local created_placeholders=()
  local secret_file
  for secret_file in "$compose_directory/secrets/postgres-superuser-password" "$compose_directory/secrets/mailfathom-database-password"; do
    if [[ ! -e "$secret_file" ]]; then
      mkdir -p "$(dirname "$secret_file")"
      : > "$secret_file"
      created_placeholders+=("$secret_file")
    fi
  done

  if (cd "$compose_directory" && docker compose config --quiet 2>/dev/null); then
    pass 'compose.yaml renders.'
  else
    fail 'compose.yaml does not render.'
  fi

  # Nothing in the deployment may apply a schema, whichever profile or overlay is selected.
  if grep -rqE 'apply-schema|dotnet ef|migrations script' "$compose_directory"; then
    fail 'The Compose deployment carries a schema step. The reviewed schema artifact belongs to #126.'
  else
    pass 'The Compose deployment applies no schema.'
  fi

  # The tag is supplied so the acknowledgement is what the render is left missing. Both are required, and Compose
  # reports whichever it reaches first, so leaving out both would prove only that one of them binds.
  expect_rejection \
    'The GHCR overlay refuses to render without an acknowledgement.' \
    'MAILFATHOM_NIGHTLY_ACKNOWLEDGED' \
    env --chdir="$compose_directory" --unset=MAILFATHOM_NIGHTLY_ACKNOWLEDGED MAILFATHOM_NIGHTLY_TAG=verification \
    docker compose --file compose.yaml --file compose.nightly.yaml config --quiet

  expect_rejection \
    'The GHCR overlay refuses to render without a named nightly identifier.' \
    'MAILFATHOM_NIGHTLY_TAG' \
    env --chdir="$compose_directory" --unset=MAILFATHOM_NIGHTLY_TAG MAILFATHOM_NIGHTLY_ACKNOWLEDGED=i-understand-this-is-unsupported \
    docker compose --file compose.yaml --file compose.nightly.yaml config --quiet

  # Nothing in the supported deployment may reach GHCR, however it is configured.
  if grep -q 'ghcr.io' "$compose_directory/compose.yaml"; then
    fail 'compose.yaml names ghcr.io. The nightly channel belongs to compose.nightly.yaml alone.'
  else
    pass 'compose.yaml names no nightly registry.'
  fi

  local placeholder
  for placeholder in "${created_placeholders[@]+"${created_placeholders[@]}"}"; do
    rm -f "$placeholder"
  done
}

verify_the_chart() {
  report 'Helm chart'

  # `appVersion` is a second written copy of a number declared in Directory.Build.props, and the only thing keeping the
  # two together is this check. A chart that documented one version while the build stamped another would reject the
  # image it was written for, through its own drift check below.
  local declared_version chart_app_version
  declared_version="$(bash scripts/read-declared-version.sh)"
  chart_app_version="$(
    sed -n 's/^appVersion:[[:space:]]*"\{0,1\}\([^"]*\)"\{0,1\}[[:space:]]*$/\1/p' "$chart_directory/Chart.yaml"
  )"

  if [[ "$chart_app_version" == "$declared_version" ]]; then
    pass "The chart documents application version ${declared_version}, which is what the build stamps."
  else
    fail "Chart.yaml documents appVersion ${chart_app_version}, but the build stamps ${declared_version}."
  fi

  # The drift check binds on the release channel, so an upgrade cannot quietly deploy an application version the chart
  # does not describe. It refuses by default and is turned off deliberately, which is what ci/release-values.yaml does.
  expect_rejection \
    'A release install whose tag disagrees with appVersion is rejected.' \
    'set image.allowVersionMismatch=true' \
    helm_command template verification mailfathom \
      --values mailfathom/ci/release-values.yaml \
      --set image.allowVersionMismatch=false

  local values_file
  for values_file in release nightly; do
    if helm_command lint mailfathom --values "mailfathom/ci/${values_file}-values.yaml" >/dev/null 2>&1; then
      pass "The chart lints against ci/${values_file}-values.yaml."
    else
      fail "The chart does not lint against ci/${values_file}-values.yaml."
      helm_command lint mailfathom --values "mailfathom/ci/${values_file}-values.yaml" >&2 || true
    fi
  done

  # Rendered twice and compared, because a template that reaches for the current time, a random value, or map ordering
  # produces a diff on every deployment and makes a review of what changed impossible.
  local first_render second_render
  first_render="$(helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml 2>&1)"
  second_render="$(helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml 2>&1)"

  if [[ "$first_render" == "$second_render" ]]; then
    pass 'Rendering is deterministic.'
  else
    fail 'Two renders of the same values differ.'
  fi

  # The rendered output is where a credential would show up if a template ever put one there.
  if printf '%s' "$first_render" | grep -qiE '^\s*(password|apiKey|token):' ; then
    fail 'The rendered output carries something that reads like a credential.'
  else
    pass 'The rendered output carries no credential.'
  fi

  if printf '%s' "$first_render" | grep -q 'kind: Secret'; then
    fail 'The chart renders a Secret. It must reference one the operator already created instead.'
  else
    pass 'The chart creates no Secret.'
  fi

  local rendered_kind
  for rendered_kind in Deployment Service ConfigMap ServiceAccount Ingress; do
    if printf '%s' "$first_render" | grep -q "^kind: ${rendered_kind}$"; then
      pass "${rendered_kind} is rendered."
    else
      fail "${rendered_kind} is not rendered."
    fi
  done

  expect_rejection \
    'Default values are refused, because no release image exists to default to.' \
    'image/repository' \
    helm_command template verification mailfathom

  expect_rejection \
    'A moving image tag is refused.' \
    'image/tag' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set image.tag=latest

  expect_rejection \
    'A nightly image without the acknowledgement is refused.' \
    'nightlyAcknowledgement' \
    helm_command template verification mailfathom --values mailfathom/ci/nightly-values.yaml --set image.nightlyAcknowledgement=

  expect_rejection \
    'A nightly image pointed at another registry is refused.' \
    'published only to ghcr.io' \
    helm_command template verification mailfathom --values mailfathom/ci/nightly-values.yaml --set image.registry=docker.io

  expect_rejection \
    'A writable root filesystem is refused.' \
    'readOnlyRootFilesystem' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set containerSecurityContext.readOnlyRootFilesystem=false

  expect_rejection \
    'Running as root is refused.' \
    'runAsNonRoot' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set podSecurityContext.runAsNonRoot=false

  expect_rejection \
    'An install without a database host is refused.' \
    'database.host' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set database.host=

  expect_rejection \
    'An install without a provisioned Secret is refused.' \
    'secrets.existingSecret' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set secrets.existingSecret=

  expect_rejection \
    'A liveness probe pointed at the database-consulting endpoint is refused.' \
    'probes/liveness/path' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set probes.liveness.path=/health

  expect_rejection \
    'A startup probe pointed at the liveness endpoint is refused.' \
    'probes/startup/path' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set probes.startup.path=/alive

  expect_rejection \
    'A probe port that collides with the application listener is refused.' \
    'probes/port' \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set probes.port=8080

  expect_rejection \
    'Moving the probe listener through extraEnvironment is refused, so it cannot drift from the container port.' \
    "invalid propertyName 'HealthEndpoints__Port'" \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml \
    --set 'config.extraEnvironment.HealthEndpoints__Port=9000'

  expect_rejection \
    'A misspelled values key is refused rather than silently ignored.' \
    "additional properties 'pullPolcy' not allowed" \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml --set image.pullPolcy=Always

  expect_rejection \
    'A deployment-owned setting in extraEnvironment is refused, so a credential cannot reach the environment block.' \
    "invalid propertyName 'ConnectionStrings__mailfathom'" \
    helm_command template verification mailfathom --values mailfathom/ci/release-values.yaml \
    --set 'config.extraEnvironment.ConnectionStrings__mailfathom=Host=x;Password=y'

  # The chart applies no schema and renders nothing that could. A Job reintroduced here would be an automatic
  # migration the moment somebody gave it a Helm hook; #126 owns the artifact and the step that runs it.
  if printf '%s' "$first_render" | grep -q '^kind: Job$'; then
    fail 'The chart renders a Job. The reviewed schema artifact and the step that applies it belong to #126.'
  else
    pass 'The chart renders no schema Job.'
  fi

  # MailFathom is Apache-2.0, and the identifier is asserted rather than merely its presence: a chart that names terms
  # other than the root LICENSE's is the failure worth catching, and it reads exactly like a correct one.
  if grep -qE '^[[:space:]]*artifacthub\.io/license:[[:space:]]*Apache-2\.0[[:space:]]*$' "$chart_directory/Chart.yaml"; then
    pass 'The chart declares its Apache-2.0 license.'
  else
    fail 'Chart.yaml carries no artifacthub.io/license: Apache-2.0 annotation, so an installed chart names no terms.'
  fi

  local nightly_render
  nightly_render="$(helm_command template verification mailfathom --values mailfathom/ci/nightly-values.yaml 2>&1)"
  if printf '%s' "$nightly_render" | grep -q 'ghcr-nightly-unsupported'; then
    pass 'A nightly deployment labels every object as unsupported.'
  else
    fail 'A nightly deployment is indistinguishable from a release in its labels.'
  fi
}

verify_one_resource_per_template() {
  report 'Template layout'

  local template_file
  local offenders=()
  for template_file in "$chart_directory"/templates/*.yaml; do
    if [[ "$(grep -c '^kind:' "$template_file")" -gt 1 ]]; then
      offenders+=("$template_file")
    fi
  done

  if [[ "${#offenders[@]}" -eq 0 ]]; then
    pass 'Each template file declares one resource.'
  else
    fail "These template files declare more than one resource: ${offenders[*]}"
  fi
}

require_command git
require_command docker

verify_the_image_definition
verify_the_compose_deployment
verify_the_chart
verify_one_resource_per_template

printf '\n'
if [[ "$failures" -ne 0 ]]; then
  printf '%d deployment-asset check(s) failed.\n' "$failures" >&2
  exit 1
fi

printf 'Deployment assets verified.\n'
