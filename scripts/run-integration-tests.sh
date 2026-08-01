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
ephemeral_resource_prefix='mailfathom-integrationtests'

# One identifier per run, generated here rather than by the app model because the removal below has to
# know it before the suite starts. Every container and volume this run creates is named under it, so a
# second suite started while this one is in progress collides with nothing and is destroyed by nothing:
# the sweep at the end names this run's resources instead of every resource the shared prefix matches.
ephemeral_run_identifier="$(od --address-radix=n --read-bytes=4 --format=x1 /dev/urandom | tr --delete ' \n')"
export MAILFATHOM_INTEGRATIONTESTS_RUN_ID="$ephemeral_run_identifier"
ephemeral_run_prefix="${ephemeral_resource_prefix}-${ephemeral_run_identifier}"

container_runtime="${MAILFATHOM_CONTAINER_RUNTIME:-docker}"

if ! command -v "$container_runtime" > /dev/null; then
  printf 'The integration suite needs a container runtime. %s was not found on PATH; set MAILFATHOM_CONTAINER_RUNTIME to the one to use.\n' \
    "$container_runtime" >&2
  exit 1
fi

# Removed when the run ends, whether it passed, failed, or was interrupted, because nothing this suite
# creates is meant to outlive it. Nothing is removed beforehand: a name no earlier run could have used
# is already an empty database, which is what the baseline migration has to apply to for a run to prove
# it applies cleanly, and a sweep of the shared prefix would destroy a concurrent run's containers to
# establish something this run's own name already establishes.
remove_ephemeral_resources() {
  mapfile -t ephemeral_containers < <(
    "$container_runtime" ps --all --quiet --filter "name=^${ephemeral_run_prefix}"
  )

  if ((${#ephemeral_containers[@]} > 0)); then
    "$container_runtime" rm --force --volumes "${ephemeral_containers[@]}" > /dev/null
  fi

  mapfile -t ephemeral_volumes < <(
    "$container_runtime" volume ls --quiet --filter "name=^${ephemeral_run_prefix}"
  )

  if ((${#ephemeral_volumes[@]} > 0)); then
    "$container_runtime" volume rm "${ephemeral_volumes[@]}" > /dev/null
  fi

  report_foreign_ephemeral_resources
}

# What a run killed with SIGKILL leaves behind, which no later run can safely delete on its own: at this
# point the resources still carrying the shared prefix belong either to a suite running right now or to
# one that died, and nothing here can tell those apart. Reported rather than removed, with the command
# that removes them once nothing else is running.
report_foreign_ephemeral_resources() {
  mapfile -t foreign_resources < <(
    {
      "$container_runtime" ps --all --format '{{.Names}}' --filter "name=^${ephemeral_resource_prefix}"
      "$container_runtime" volume ls --format '{{.Name}}' --filter "name=^${ephemeral_resource_prefix}"
    } | sort
  )

  if ((${#foreign_resources[@]} == 0)); then
    return
  fi

  printf '\n%d ephemeral resource(s) from another run are still present:\n' "${#foreign_resources[@]}" >&2
  printf '  %s\n' "${foreign_resources[@]}" >&2
  printf 'They belong to a suite running now or to one that was killed. Once nothing else is running:\n' >&2
  printf '  %s rm --force --volumes $(%s ps --all --quiet --filter name=^%s)\n' \
    "$container_runtime" "$container_runtime" "$ephemeral_resource_prefix" >&2
  printf '  %s volume rm $(%s volume ls --quiet --filter name=^%s)\n' \
    "$container_runtime" "$container_runtime" "$ephemeral_resource_prefix" >&2
}

trap remove_ephemeral_resources EXIT

raw_results_directory='artifacts/integration-tests/raw'
coverage_report_directory='artifacts/integration-tests/report'

# Removed rather than added to. Report generation globs the raw directory, so a Cobertura file from an
# earlier run would silently merge into this run's numbers and report coverage nothing just produced.
rm -rf artifacts/integration-tests

dotnet tool restore
dotnet restore "$integration_test_project" --locked-mode
dotnet build "$integration_test_project" --configuration Release --no-restore

# Run rather than test: the project opts out of test-platform discovery so a solution-wide run never
# starts it, which leaves executing it directly as the way to ask for it. Trailing arguments are
# forwarded, so `--filter` and the other Microsoft Testing Platform options work as they would anywhere
# else.
#
# The exit code is carried rather than propagated immediately, because a failing run is exactly when
# its report is worth reading. The script still ends on that code.
test_exit_code=0
dotnet run --project "$integration_test_project" --configuration Release --no-build -- \
  --report-xunit-trx --coverlet --results-directory "$raw_results_directory" "$@" || test_exit_code=$?

# The classes marked [RequiresIntegrationCoverage] are the whole scope of this report, and the marker
# is where that inventory already lives, so the filter is derived from it instead of being a second
# list to keep in step. The attribute's own declaration carries the marker only as an example and is
# not production code under test.
mapfile -t integration_covered_sources < <(
  grep --recursive --files-with-matches --include='*.cs' --extended-regexp \
    '^[[:space:]]*\[RequiresIntegrationCoverage\]' src \
    | grep --invert-match 'RequiresIntegrationCoverageAttribute\.cs' \
    | sort
)

if ((${#integration_covered_sources[@]} == 0)); then
  printf 'No source file under src/ carries [RequiresIntegrationCoverage], so the report would have no scope. Either the marker was removed everywhere, in which case this coverage step has no reason to exist, or the search is wrong.\n' >&2
  exit 1
fi

# Path suffixes rather than type names, because that is what the marker search returns directly and it
# matches whether the collector recorded an absolute path or the deterministic /_/ form a continuous
# integration build produces.
coverage_file_filters="$(printf '+**/%s;' "${integration_covered_sources[@]}")"
coverage_file_filters="${coverage_file_filters%;}"

mapfile -t collected_coverage_reports < <(
  find "$raw_results_directory" -name '*.cobertura*.xml' 2> /dev/null
)

if ((${#collected_coverage_reports[@]} > 0)); then
  # No threshold, by design. Unit tests stay the only source of an enforced coverage metric; this
  # report answers how far the integration suite has got through the classes that carry the marker,
  # which is progress to read rather than a gate to pass.
  dotnet tool run reportgenerator \
    "-reports:$raw_results_directory/**/*.cobertura*.xml" \
    "-targetdir:$coverage_report_directory" \
    '-reporttypes:Cobertura;HtmlInline;TextSummary' \
    "-filefilters:$coverage_file_filters" \
    "-title:MailFathom integration coverage"

  printf '\nIntegration coverage over the %d classes marked [RequiresIntegrationCoverage]:\n\n' \
    "${#integration_covered_sources[@]}"
  cat "$coverage_report_directory/Summary.txt"
else
  printf 'The run produced no Cobertura report under %s, so no integration coverage was measured.\n' \
    "$raw_results_directory" >&2
fi

exit "$test_exit_code"
