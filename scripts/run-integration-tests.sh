#!/usr/bin/env bash
set -euo pipefail

# The integration suite runs on request and nowhere else. It is deliberately absent from
# scripts/verify-fast.sh and scripts/verify-full.sh, and its GitHub workflow is manual dispatch
# only, because it starts a PostgreSQL container and applies the baseline migration to it.

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'run-integration-tests.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

integration_test_project='tests/IntegrationTests/IntegrationTests.csproj'

# Must match OrchestrationContract.EphemeralResourceNamePrefix in src/AppHost. The app model names
# the container and the volume it creates under test with this prefix precisely so that a filter can
# find them again without knowing what a given run produced.
ephemeral_resource_prefix='mailmcp-integrationtests'

container_runtime="${MAILMCP_CONTAINER_RUNTIME:-docker}"

if ! command -v "$container_runtime" > /dev/null; then
  printf 'The integration suite needs a container runtime. %s was not found on PATH; set MAILMCP_CONTAINER_RUNTIME to the one to use.\n' \
    "$container_runtime" >&2
  exit 1
fi

# Removed before the run as well as after it. Before, because the baseline migration is only proven
# to apply cleanly when it applies to an empty database, and a volume left by an earlier run would
# quietly turn every subsequent run into an upgrade of that database instead. After, because nothing
# this suite creates is meant to outlive it.
remove_ephemeral_resources() {
  mapfile -t ephemeral_containers < <(
    "$container_runtime" ps --all --quiet --filter "name=^${ephemeral_resource_prefix}"
  )

  if ((${#ephemeral_containers[@]} > 0)); then
    "$container_runtime" rm --force --volumes "${ephemeral_containers[@]}" > /dev/null
  fi

  mapfile -t ephemeral_volumes < <(
    "$container_runtime" volume ls --quiet --filter "name=^${ephemeral_resource_prefix}"
  )

  if ((${#ephemeral_volumes[@]} > 0)); then
    "$container_runtime" volume rm "${ephemeral_volumes[@]}" > /dev/null
  fi
}

remove_ephemeral_resources
trap remove_ephemeral_resources EXIT

dotnet restore "$integration_test_project"
dotnet build "$integration_test_project" --configuration Release --no-restore

# Run rather than test: the project opts out of test-platform discovery so a solution-wide run never
# starts it, which leaves executing it directly as the way to ask for it. Arguments are forwarded, so
# `--filter` and the other Microsoft Testing Platform options work as they would anywhere else.
dotnet run --project "$integration_test_project" --configuration Release --no-build -- "$@"
