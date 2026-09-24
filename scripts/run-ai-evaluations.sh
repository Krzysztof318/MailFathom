#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# The agent evaluation suite runs on request and nowhere else. It is absent from scripts/verify-fast.sh
# and scripts/verify-full.sh because a scenario calls real models and a judge, and its GitHub workflow
# never runs on a pull request.
#
# Without MAILFATHOM_AI_EVALUATIONS=true the paid scenarios skip, and what runs is only the free proof
# of what the store holds. With it, the run needs MAILFATHOM_CHAT_API_KEY and MAILFATHOM_JUDGE_MODEL, and
# fails naming whichever is missing. The judge answers from the same endpoint and the same key as the
# models under test, so its model is all that is declared apart — beside MAILFATHOM_JUDGE_REASONING_EFFORT,
# which a run may leave unset to send the judge no reasoning parameter at all.
#
# MAILFATHOM_EVALUATION declares the run as one YAML block, which is one line in flow style:
# {MainModel: {Model: vendor/model, ReasoningEffort: medium}, ImageDescription: [model-a, model-b]}.
# MainModel is the model every agent runs on and each agent may name its own under its Chat key; a role
# takes one model or a list, and an evaluation runs once per model of the agent it measures. The block also
# takes Repetitions, from 1 to 20, and EmbeddingModels and EmbeddingDimension for the retrieval scenario.
# Where it names no MainModel, MAILFATHOM_CHAT_MODEL is measured, and the run fails naming both when
# neither is set; where it names no EmbeddingModels, MAILFATHOM_EMBEDDING_MODEL is. The retrieval scenario
# reaches its models with MAILFATHOM_EMBEDDING_API_KEY, which a requested run cannot proceed without, and
# MAILFATHOM_EMBEDDING_ADDRESS, which it may leave unset for the provider's own address.
#
# The store is kept rather than cleared: its results are what the report compares this run against,
# and its cache is what makes an unchanged prompt free. MAILFATHOM_AI_EVALUATIONS_STORE points it
# elsewhere, which is how the workflow hands in the store the previous run left.

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'run-ai-evaluations.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

evaluation_project='backend/tests/Evaluations/Evaluations.csproj'
output_directory='artifacts/ai-evaluations'

MAILFATHOM_AI_EVALUATIONS_STORE="$(realpath -m "${MAILFATHOM_AI_EVALUATIONS_STORE:-$output_directory/store}")"
export MAILFATHOM_AI_EVALUATIONS_STORE

# One name for every scenario in the run, so the report shows it as one column. A timestamp sorts in
# the order the runs happened, which is the order the report reads them in.
MAILFATHOM_AI_EVALUATIONS_EXECUTION="${MAILFATHOM_AI_EVALUATIONS_EXECUTION:-$(date -u +%Y%m%dT%H%M%SZ)}"
export MAILFATHOM_AI_EVALUATIONS_EXECUTION

# The aieval tool reports usage to Microsoft unless told not to, and nothing about this repository's
# runs is anybody else's to collect.
export DOTNET_AIEVAL_TELEMETRY_OPTOUT=1
export DOTNET_AIEVAL_SKIP_FIRST_TIME_EXPERIENCE=1

# How many runs the store keeps results for. The report reads the newest ten; the rest is history a
# comparison can still be widened to without paying for any of it again.
retained_executions=30

mkdir -p "$MAILFATHOM_AI_EVALUATIONS_STORE"
rm -rf "$output_directory/results" "$output_directory/report.html"

dotnet tool restore
dotnet restore "$evaluation_project" --locked-mode
dotnet build "$evaluation_project" --configuration Release --no-restore

# Run rather than test, for the reason the integration suite gives: the project opts out of discovery
# so a solution-wide run never starts it. The exit code is carried so the report is still rendered
# for a run that failed, which is exactly when it is worth reading.
test_exit_code=0
dotnet run --project "$evaluation_project" --configuration Release --no-build -- \
  --report-xunit-trx --results-directory "$output_directory/results" "$@" || test_exit_code=$?

# Each half is tidied only once a run has written it: the tool fails on a directory that does not
# exist yet, which is the state of a store no requested run has touched.
if [[ -d "$MAILFATHOM_AI_EVALUATIONS_STORE/cache" ]]; then
  dotnet tool run aieval clean-cache --path "$MAILFATHOM_AI_EVALUATIONS_STORE"
fi

if [[ -d "$MAILFATHOM_AI_EVALUATIONS_STORE/results" ]]; then
  dotnet tool run aieval clean-results --path "$MAILFATHOM_AI_EVALUATIONS_STORE" -n "$retained_executions"
  dotnet tool run aieval report --path "$MAILFATHOM_AI_EVALUATIONS_STORE" --output "$output_directory/report.html"
else
  printf 'The store holds no results yet, so there is no report to render.\n' >&2
fi

# A model's answer varies between calls, so a few scenarios falling short is the measurement rather
# than a defect, and only a pass rate under this floor fails the run. The rate counts the paid
# `<Subject>Evaluations` classes alone: the free tests beside them are deterministic, so any one of
# them failing still fails the run, and counted in they would hold the rate near 100%. Exit code 2 is
# the test platform's "at least one test failed"; every other code is carried as it is.
required_pass_percentage=85

if [[ "$test_exit_code" -eq 2 ]] && trx_files=("$output_directory"/results/*.trx) && [[ -f "${trx_files[0]}" ]]; then
  read -r free_failed paid_passed paid_executed < <(
    grep -ho '<UnitTestResult testName="[^"]*" outcome="[A-Za-z]*"' "${trx_files[@]}" |
      sed -E 's/^<UnitTestResult testName="([^"(]*)[^"]*" outcome="([A-Za-z]*)"$/\1 \2/' |
      awk '
        $2 == "NotExecuted" { next }
        {
          class = $1
          sub(/\.[^.]*$/, "", class)
          if (class ~ /Evaluations$/) { paid_executed++; if ($2 == "Passed") paid_passed++ }
          else if ($2 != "Passed") free_failed++
        }
        END { print free_failed + 0, paid_passed + 0, paid_executed + 0 }')

  printf '%d of %d evaluation scenarios passed, and %d free tests failed; the run fails under %d%% or on any free failure.\n' \
    "$paid_passed" "$paid_executed" "$free_failed" "$required_pass_percentage"

  if (( free_failed == 0 && paid_executed > 0 && paid_passed * 100 >= required_pass_percentage * paid_executed )); then
    test_exit_code=0
  fi
fi

exit "$test_exit_code"
