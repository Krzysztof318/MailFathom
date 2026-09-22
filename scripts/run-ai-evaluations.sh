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
# of what the store holds. With it, the run needs MAILFATHOM_EVALUATION_MODELS, MAILFATHOM_CHAT_API_KEY,
# and MAILFATHOM_JUDGE_MODEL, and fails naming whichever is missing. The judge answers from the same
# endpoint and the same key as the models under test, so its model is all that is declared apart —
# beside MAILFATHOM_JUDGE_REASONING_EFFORT, which a run may leave unset to send the judge no reasoning
# parameter at all. MAILFATHOM_EVALUATION_REPETITIONS says how many times each case is asked of each
# model, from 1 to 20, and a run that leaves it unset asks each case once. MAILFATHOM_EVALUATION_REASONING_EFFORT
# states how hard every model under test reasons, and a run that leaves it unset sends none.
#
# The retrieval scenario measures embedding models rather than chat models, so it reads its own:
# MAILFATHOM_EVALUATION_EMBEDDING_MODELS and MAILFATHOM_EMBEDDING_API_KEY, which a requested run cannot
# proceed without, beside MAILFATHOM_EMBEDDING_ADDRESS and MAILFATHOM_EVALUATION_EMBEDDING_DIMENSION, which
# it may leave unset for the provider's own address and each model's own width.
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

exit "$test_exit_code"
