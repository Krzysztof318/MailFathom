#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# What the coverage figure stopped saying. Line coverage has sat above 95% for months, which means it
# says a line ran rather than that anything asserted its result; mutation testing changes the
# production code and reports which changes no test noticed. That answer is diagnostic — it says where
# the next test is worth writing — so it is read and never enforced. Nothing here gates: `--break-at 0`
# is what keeps the score from deciding an exit status, and neither verification script nor any
# pull-request workflow calls this.
#
# Two projects and no others. `Domain` and `Application` hold the invariants and the use cases, which
# is where a surviving mutant names a missing assertion rather than an adapter detail nobody would act
# on; the remaining boundaries would spend the runtime on generated mutants against a database, a mail
# server, or a protocol shape. `--project` states that rather than leaving it to whatever the test
# project happens to reference.
#
# The runner is Microsoft Testing Platform rather than VSTest, because that is the only one that
# reaches xUnit v3: `xunit.v3.mtp-v2` carries no VSTest adapter, so Stryker's default runner finds no
# tests at all here. It is preview in Stryker 4.16 and says so on every run.

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'mutation-score.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

# Stryker spends half the machine's cores by default, which is the right manners on a machine
# somebody is also working on and pure waste on a runner that does nothing else — and this is the one
# job long enough for the difference to decide whether it finishes. `MAILFATHOM_MUTATION_CONCURRENCY`
# is how a caller that owns the whole machine says so; the workflow sets it to the runner's core
# count, and a developer leaves it unset and keeps the polite default.
mutation_concurrency=()
if [[ -n "${MAILFATHOM_MUTATION_CONCURRENCY:-}" ]]; then
  mutation_concurrency=(--concurrency "$MAILFATHOM_MUTATION_CONCURRENCY")
fi

if (($# > 0)); then
  projects=("$@")
else
  projects=(Domain Application)
fi

# The report is a fact about the run that produced it, so the summary is composed from the JSON report
# rather than from what Stryker printed. `NoCoverage` is a survivor the suite never even reached, which
# is why it counts against the score beside `Survived` and appears in the same table; `CompileError`
# and `Ignored` are mutants Stryker withdrew and score nothing either way.
#
# Scoring nothing at all is its own case rather than a clean one. Every mutant withdrawn as a compile
# error leaves no survivors to list, which reads exactly like a suite that killed everything — so the
# closing sentence is decided by whether anything was scored before it is decided by whether anything
# survived. A run that validated nothing has to say so, because that is what a whole test project
# failing to compile under mutation looks like from here.
summarize_report() {
  local project="$1"
  local report="$2"

  jq -r --arg project "$project" '
    [.files[].mutants[]] as $mutants
    | ($mutants | map(select(.status == "Killed")) | length) as $killed
    | ($mutants | map(select(.status == "Survived")) | length) as $survived
    | ($mutants | map(select(.status == "NoCoverage")) | length) as $uncovered
    | ($mutants | map(select(.status == "Timeout")) | length) as $timedOut
    | ($mutants | map(select(.status == "CompileError")) | length) as $compileErrors
    | ($killed + $timedOut + $survived + $uncovered) as $scored
    | (if $scored == 0 then "n/a"
       else (($killed + $timedOut) * 10000 / $scored | round / 100 | tostring) + " %"
       end) as $score
    | ($mutants
       | map(select(.status == "Survived" or .status == "NoCoverage"))
       | group_by(.mutatorName)
       | map({
           mutator: .[0].mutatorName,
           survived: (map(select(.status == "Survived")) | length),
           uncovered: (map(select(.status == "NoCoverage")) | length)
         })
       | sort_by(-(.survived + .uncovered))) as $byMutator
    | [
        "### Mutation score — " + $project,
        "",
        "| | |",
        "| --- | --- |",
        "| Score | " + $score + " |",
        "| Killed | " + ($killed | tostring) + " |",
        "| Survived | " + ($survived | tostring) + " |",
        "| Not covered by any test | " + ($uncovered | tostring) + " |",
        "| Timed out | " + ($timedOut | tostring) + " |",
        "| Withdrawn as compile errors | " + ($compileErrors | tostring) + " |",
        ""
      ]
      + (if $scored == 0 then ["No mutant was scored, so this run establishes nothing about the suite."]
         elif ($byMutator | length) == 0 then ["Every scored mutant was killed."]
         else [
           "Mutants no test noticed, by mutator:",
           "",
           "| Mutator | Survived | Not covered |",
           "| --- | ---: | ---: |"
         ]
         + ($byMutator | map("| " + .mutator
                             + " | " + (.survived | tostring)
                             + " | " + (.uncovered | tostring) + " |"))
         end)
      + [""]
    | .[]
  ' "$report"
}

for project in "${projects[@]}"; do
  test_project_directory="tests/${project}.UnitTests"

  if [[ ! -d "$test_project_directory" ]]; then
    printf '%s has no unit-test project at %s.\n' "$project" "$test_project_directory" >&2
    exit 1
  fi

  output_directory="${repository_root}/artifacts/mutation/${project}"

  # Stryker resolves the project under test from the test project it is invoked beside, so the working
  # directory is the argument rather than a flag. Its own console output is diagnostic and goes to
  # standard error, which leaves standard output carrying the summary alone — the same shape
  # `scripts/build-schema-artifact.sh` has, so a caller can append it to a step summary without
  # filtering a log out of it.
  (
    cd "$test_project_directory"

    dotnet stryker \
      --project "${project}.csproj" \
      --test-runner mtp \
      --reporter Html \
      --reporter Json \
      --reporter Dots \
      --output "$output_directory" \
      --break-at 0 \
      --skip-version-check \
      --verbosity info \
      "${mutation_concurrency[@]}"
  ) >&2

  report="${output_directory}/reports/mutation-report.json"

  if [[ ! -f "$report" ]]; then
    printf 'Stryker wrote no JSON report for %s at %s.\n' "$project" "$report" >&2
    exit 1
  fi

  summarize_report "$project" "$report"
done
