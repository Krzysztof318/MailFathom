#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

if [[ "$(basename "$0")" == 'dotnet' ]]; then
  : "${FAKE_DOTNET_LOG:?FAKE_DOTNET_LOG must identify the invocation log}"
  printf '%s\n' "$*" >> "$FAKE_DOTNET_LOG"

  if [[ -n "${FAKE_DOTNET_FAIL_MATCH:-}" && "$*" == *"$FAKE_DOTNET_FAIL_MATCH"* ]]; then
    exit 19
  fi

  # The repairing formatting pass is the one invocation in either gate that rewrites the tree, and
  # what it rewrites decides whether the run may record anything. A contract asks for that by naming
  # the file this stands in for.
  if [[ -n "${FAKE_DOTNET_REWRITE:-}" && "$*" == *'format'* ]]; then
    printf 'rewritten\n' >> "$FAKE_DOTNET_REWRITE"
  fi

  if [[ "$*" == '--version' ]]; then
    printf '10.0.110\n'
  fi

  exit 0
fi

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source_repository_root="$(cd "$scripts_directory/.." && pwd -P)"
test_directory="$(mktemp -d)"
repository_root="$test_directory/repository"
# The remote's own path names the canonical repository, because the scripts identify their base
# remote by where it points rather than by its name. A fixture remote called anything else would
# be a fork as far as they are concerned, which is what
# `verify_full_refuses_a_checkout_with_no_upstream_remote` asserts deliberately.
remote_repository_root="$test_directory/upstream/Krzysztof318/MailFathom"
fake_bin_directory="$test_directory/bin"
protected_paths_bin_directory="$test_directory/protected-paths-bin"
typo_check_bin_directory="$test_directory/typo-check-bin"
fathom_review_bin_directory="$test_directory/fathom-review-bin"
settle_bin_directory="$test_directory/fathom-review-settle-bin"
collect_bin_directory="$test_directory/fathom-review-collect-bin"
submit_bin_directory="$test_directory/fathom-review-submit-bin"
board_bin_directory="$test_directory/fathom-review-board-bin"
rules_board_bin_directory="$test_directory/pull-request-rules-board-bin"
invocation_log="$test_directory/dotnet-invocations.log"
workflow_invocation_log="$test_directory/workflow-invocations.log"
fixture_branch='agent/workflow-fixture'
passed_count=0
failed_count=0

cleanup() {
  rm -rf "$test_directory"
}

trap cleanup EXIT

mkdir -p \
  "$fake_bin_directory" \
  "$repository_root/docs" \
  "$repository_root/scripts" \
  "$repository_root/backend/src" \
  "$repository_root/backend/tests"
ln -s "$scripts_directory/test-agent-workflow.sh" "$fake_bin_directory/dotnet"

git -C "$repository_root" init --initial-branch=main --quiet
git -C "$repository_root" config user.email agent-workflow@example.invalid
git -C "$repository_root" config user.name 'Agent Workflow Tests'
printf '<Solution />\n' > "$repository_root/backend/MailFathom.slnx"
printf 'clean\n' > "$repository_root/tracked.txt"
# The gates record what they verified under `artifacts/`, which the real repository ignores. Without
# the same line here the full gate would refuse its own record as an untracked file, and the digest
# of the tree would include the digest of the tree.
printf 'artifacts/\n' > "$repository_root/.gitignore"
printf '%s\n' \
  '#!/usr/bin/env bash' \
  'set -euo pipefail' \
  ': "${FAKE_WORKFLOW_LOG:?FAKE_WORKFLOW_LOG must identify the invocation log}"' \
  "printf 'workflow-contracts\\n' >> \"\$FAKE_WORKFLOW_LOG\"" \
  "printf 'the contract suite ran\\n'" \
  'if [[ -n "${FAKE_WORKFLOW_FAIL:-}" ]]; then exit 23; fi' \
  > "$repository_root/scripts/test-agent-workflow.sh"
chmod +x "$repository_root/scripts/test-agent-workflow.sh"
git -C "$repository_root" add .gitignore backend/MailFathom.slnx scripts/test-agent-workflow.sh tracked.txt
git -C "$repository_root" commit --quiet -m 'test fixture'

git clone --quiet "$repository_root" "$remote_repository_root"
git -C "$remote_repository_root" config user.email agent-workflow@example.invalid
git -C "$remote_repository_root" config user.name 'Agent Workflow Tests'
git -C "$repository_root" remote add origin "$remote_repository_root"
git -C "$repository_root" fetch --quiet origin main
# Every contract below runs the verification scripts, and they now refuse the integration branch.
# The fixture therefore works from a branch shaped like a real task branch, and the refusal itself
# is exercised by the tests that check it out deliberately.
git -C "$repository_root" checkout --quiet -b "$fixture_branch"
# The fast loop formats the C# files the branch changed, so the fixture branch carries one. Keeping
# it committed rather than dirty leaves the working tree clean for the contracts that require it.
printf 'namespace Fixture;\n' > "$repository_root/backend/src/Sample.cs"
git -C "$repository_root" add backend/src/Sample.cs
git -C "$repository_root" commit --quiet -m 'fixture C# file'

# The protected-paths contracts stand in for the GitHub REST call the workflow makes, so their fake
# `gh` lives in a directory of its own rather than beside the fake `dotnet`. Only those contracts put
# it on `PATH`, and nothing else in this suite can then reach a `gh` it did not ask for.
mkdir -p "$protected_paths_bin_directory"
cat > "$protected_paths_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s\n' "$FAKE_CHANGED_PATHS"
FAKE_GH
chmod +x "$protected_paths_bin_directory/gh"

# The `Typo check` step makes the same shape of REST call and gets its own fake for the same reason.
# It answers with the paths the contract supplies, which stand for what the real `--jq` would have
# left after dropping the removed ones — the filtering itself is the endpoint's and jq's rather than
# the step's, so what these contracts exercise is everything the step does with the answer.
mkdir -p "$typo_check_bin_directory"
cat > "$typo_check_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s' "$FAKE_CHANGED_PATHS"
FAKE_GH
chmod +x "$typo_check_bin_directory/gh"

# The gate asks one endpoint — the reviews already on this pull request — and counts the automatic
# ones among them. The filter that decides which those are is the contract worth testing, so this
# runs the step's own `--jq` against whatever reviews a contract set up rather than answering with a
# number of its own. No fixture means a pull request nobody has reviewed, which is what every
# contract about the other branches of the gate wants.
mkdir -p "$fathom_review_bin_directory"
cat > "$fathom_review_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

filter=''

while (($# > 0)); do
  if [[ "$1" == '--jq' ]]; then
    filter="$2"
    shift 2
    continue
  fi

  shift
done

if [[ -z "$filter" ]]; then
  echo 'The gate called gh without a --jq filter.' >&2
  exit 1
fi

if [[ -n "${FAKE_REVIEWS_FILE:-}" && -s "${FAKE_REVIEWS_FILE:-}" ]]; then
  jq "$filter" "$FAKE_REVIEWS_FILE"
else
  printf '[]\n' | jq "$filter"
fi
FAKE_GH
chmod +x "$fathom_review_bin_directory/gh"

# The settle step asks the same two endpoints when the comments on the pull request were written,
# and decides only from those timestamps. This fake answers whatever the contract set: nothing for
# a pull request nobody has commented on, `now` for a conversation that never goes quiet, an
# explicit timestamp otherwise, and `oldest-first` for the order GitHub actually returns, where the
# record that decides is the last one rather than the first.
mkdir -p "$settle_bin_directory"
cat > "$settle_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
case "${FAKE_NEWEST_COMMENT:-}" in
  '') ;;
  now) date -u +%Y-%m-%dT%H:%M:%SZ ;;
  oldest-first)
    printf '2020-01-01T00:00:00Z\n'
    date -u +%Y-%m-%dT%H:%M:%SZ
    ;;
  *) printf '%s\n' "$FAKE_NEWEST_COMMENT" ;;
esac
FAKE_GH
chmod +x "$settle_bin_directory/gh"

export FAKE_DOTNET_LOG="$invocation_log"
export FAKE_WORKFLOW_LOG="$workflow_invocation_log"
export PATH="$fake_bin_directory:$PATH"

assert_file_content() {
  local expected_content="$1"
  local actual_file="$2"

  if [[ "$(cat "$actual_file")" != "$expected_content" ]]; then
    printf 'Expected:\n%s\nActual:\n%s\n' "$expected_content" "$(cat "$actual_file")" >&2
    return 1
  fi
}

assert_contains() {
  local expected_text="$1"
  local actual_file="$2"

  if ! grep -Fq -e "$expected_text" "$actual_file"; then
    printf 'Expected %s to contain: %s\n' "$actual_file" "$expected_text" >&2
    return 1
  fi
}

# For a step whose whole output is one JSON document, where the interesting assertion is a field
# rather than the file. It names the filter in the failure, so a red test says which part disagreed
# instead of printing two documents to compare by eye.
assert_json() {
  local expected_value="$1"
  local filter="$2"
  local actual_file="$3"
  local actual_value

  actual_value="$(jq -c "$filter" "$actual_file")"

  if [[ "$actual_value" != "$expected_value" ]]; then
    printf 'Expected %s of %s to be:\n%s\nActual:\n%s\n' \
      "$filter" "$actual_file" "$expected_value" "$actual_value" >&2
    return 1
  fi
}

assert_excludes() {
  local unexpected_text="$1"
  local actual_file="$2"

  if grep -Fq -e "$unexpected_text" "$actual_file"; then
    printf 'Expected %s not to contain: %s\n' "$actual_file" "$unexpected_text" >&2
    return 1
  fi
}

run_test() {
  local test_name="$1"
  local test_status

  # Every contract below states what a gate does when it is asked to verify something, so each one
  # starts from a checkout no gate has recorded a verdict about. The contracts that are *about* the
  # records write them themselves, within one test.
  rm -rf "$repository_root/artifacts/verify"

  set +e
  (
    set -e
    "$test_name"
  )
  test_status=$?
  set -e

  if ((test_status == 0)); then
    printf 'PASS %s\n' "$test_name"
    passed_count=$((passed_count + 1))
  else
    printf 'FAIL %s\n' "$test_name" >&2
    failed_count=$((failed_count + 1))
  fi
}

# The client is a second solution with a formatting pass of its own, so a contract about it needs a
# client C# file the branch changed. It is added and removed inside one test rather than carried by
# the fixture branch, because the contract below asserts the *whole* invocation log of a service-only
# change — which is where the guarantee that a branch opening no client file pays nothing lives.
add_client_change() {
  mkdir -p "$repository_root/frontend/src/Client"
  printf '<Solution />\n' > "$repository_root/frontend/MailFathom.Client.slnx"
  printf 'namespace Fixture;\n' > "$repository_root/frontend/src/Client/Sample.cs"
  # Staged rather than left untracked, because the full gate refuses untracked files before it
  # decides anything and two of the contracts below run it.
  git -C "$repository_root" add frontend
}

remove_client_change() {
  git -C "$repository_root" rm --quiet --cached -r frontend
  rm -rf "$repository_root/frontend"
}

# A change no build reads runs neither stack's flow, which is the whole of what the change filters
# buy locally: before them, a documentation-only branch paid for a Release build and the entire unit
# suite of a solution it could not have broken. What still answers for such a change is the contract
# suite and the whitespace checks in the full gate, neither of which is a stack's flow.
verify_fast_runs_no_stack_flow_for_a_change_no_build_reads() {
  : > "$invocation_log"
  git -C "$repository_root" checkout --quiet --detach origin/main
  stage_documentation_change

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ) > "$test_directory/verify-fast-no-stack-output" 2>&1

  discard_documentation_change
  git -C "$repository_root" checkout --quiet "$fixture_branch"

  assert_file_content '' "$invocation_log"
  assert_contains 'This change reaches neither stack' "$test_directory/verify-fast-no-stack-output"
}

verify_full_runs_no_stack_flow_for_a_change_no_build_reads() {
  local gate_output="$test_directory/verify-full-no-stack-output"

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  git -C "$repository_root" checkout --quiet --detach origin/main
  stage_documentation_change

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$gate_output" 2>&1

  discard_documentation_change
  git -C "$repository_root" checkout --quiet "$fixture_branch"

  # The suite is the answer such a change earns, and it is the whole of it: nothing was restored,
  # built, tested, measured, or formatted. The gate says so, because a run that built nothing in
  # silence reads as a run that did not happen.
  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
  assert_file_content '' "$invocation_log"
  assert_contains 'This change reaches neither stack' "$gate_output"
}

# One repairing pass and no verifying one. The verifying pass would restate the Release build two
# lines above it: `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` turn every IDE rule the
# `.editorconfig` sets to `warning` into a build error, so a diagnostic with no code fix has already
# failed the run before formatting is reached. What the repairing pass is here for is the part no
# build reports — the ordering of using directives, a missing final newline — and repairing it is
# something only this script does.
#
# The whole log is asserted rather than a line of it, and that is what states the client's cost: a
# branch that changed no file under `frontend/` neither restores nor loads the client solution, so
# the run this contract describes is byte-for-byte what it was before the client stack existed.
verify_fast_runs_restore_build_tests_and_formatting() {
  : > "$invocation_log"

  (
    cd "$repository_root/backend/tests"
    "$scripts_directory/verify-fast.sh"
  )

  assert_file_content \
    $'restore backend/MailFathom.slnx --locked-mode\nbuild backend/MailFathom.slnx --configuration Release --no-restore\ntest --solution backend/MailFathom.slnx --configuration Release --no-build\nformat backend/MailFathom.slnx --no-restore --include backend/src/Sample.cs' \
    "$invocation_log"
}

# The other half of the same decision. A branch that reaches both stacks runs both flows, each over
# its own solution and formatted with the files that belong to it, and the client's restore is part
# of its own branch rather than of the run: `dotnet format` needs a restored solution, and the
# `--no-restore` above is only true of the service one, which this flow restored for its own build.
verify_fast_runs_the_flow_of_each_stack_the_change_reaches() {
  add_client_change
  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  remove_client_change
  assert_file_content \
    $'restore backend/MailFathom.slnx --locked-mode\nbuild backend/MailFathom.slnx --configuration Release --no-restore\ntest --solution backend/MailFathom.slnx --configuration Release --no-build\nformat backend/MailFathom.slnx --no-restore --include backend/src/Sample.cs\nrestore frontend/MailFathom.Client.slnx --locked-mode\nbuild frontend/MailFathom.Client.slnx --configuration Release --no-restore\ntest --solution frontend/MailFathom.Client.slnx --configuration Release --no-build\nformat frontend/MailFathom.Client.slnx --no-restore --include frontend/src/Client/Sample.cs' \
    "$invocation_log"
}

# The full gate over the same branch, where the two stacks differ in one place and nowhere else: the
# service solution is verified with the files the branch changed, because `--include` selects within
# the workspace `dotnet format` loaded and the service workspace is several dozen projects, while the
# client solution is verified whole — two projects, and the verdict the `Frontend` job of `CI` holds
# a branch to. A client path in the service list would name no file that solution holds, which is
# what the split exists for.
verify_full_verifies_each_solution_the_change_reaches() {
  add_client_change
  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  remove_client_change
  assert_contains \
    'format backend/MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic --include backend/src/Sample.cs' \
    "$invocation_log"
  assert_contains 'build frontend/MailFathom.Client.slnx --configuration Release --no-restore' "$invocation_log"
  assert_contains 'test --solution frontend/MailFathom.Client.slnx --configuration Release --no-build' "$invocation_log"
  assert_contains \
    'format frontend/MailFathom.Client.slnx --no-restore --verify-no-changes --verbosity diagnostic' \
    "$invocation_log"
  assert_excludes 'format frontend/MailFathom.Client.slnx --no-restore --verify-no-changes --verbosity diagnostic --include' \
    "$invocation_log"
}

# A file above both stacks is the case neither filter owns alone. `global.json` pins the SDK the
# service compiles with and the Uno SDK that chooses every client package, so a change to it moves
# both and both flows run — which is the answer `ci.yml` gives it as well, through the same entry in
# both of its filters.
verify_full_runs_both_flows_for_a_change_above_both_stacks() {
  : > "$invocation_log"
  # Detached at the base, so `global.json` is the only path the change carries and each flow is
  # earned by it rather than by the C# file the fixture branch already holds.
  git -C "$repository_root" checkout --quiet --detach origin/main
  printf '{}\n' > "$repository_root/global.json"
  git -C "$repository_root" add global.json

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  git -C "$repository_root" rm --quiet --force --cached global.json
  rm -f "$repository_root/global.json"
  git -C "$repository_root" checkout --quiet "$fixture_branch"

  assert_contains 'build backend/MailFathom.slnx --configuration Release --no-restore' "$invocation_log"
  assert_contains 'build frontend/MailFathom.Client.slnx --configuration Release --no-restore' "$invocation_log"
}

# The fixture branch changes one C# file and nothing else, which is also the case the scoped
# verification below is about: the gate verifies the file the branch wrote rather than the 1113 the
# solution holds, because formatting is a property of a file and every other one was verified by
# whatever change last touched it.
verify_full_runs_tests_once_through_coverage() {
  : > "$invocation_log"

  (
    cd "$repository_root/backend/src"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content \
    $'tool restore\nrestore backend/MailFathom.slnx --locked-mode\nbuild backend/MailFathom.slnx --configuration Release --no-restore\nmsbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release\nformat backend/MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic --include backend/src/Sample.cs' \
    "$invocation_log"
}

# The full gate reads what the branch changed to decide what it still has to check, so a contract
# about either decision has to say so. A documentation file is the shortest way to state a change no
# build reads and every whole-tree contract does; it is staged rather than left untracked, because
# the gate rejects untracked files before it decides anything.
stage_documentation_change() {
  printf 'note\n' > "$repository_root/docs/note.md"
  git -C "$repository_root" add docs/note.md
}

discard_documentation_change() {
  git -C "$repository_root" rm --quiet --force --cached docs/note.md
  rm -f "$repository_root/docs/note.md"
}

verify_full_runs_workflow_contracts_for_a_change_beyond_csharp() {
  : > "$workflow_invocation_log"
  stage_documentation_change

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  discard_documentation_change
  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
}

# All but one invariant the suite asserts is carried by a file no C# change can move: a licensing
# header outside `.cs`, a `describes:` marker, a table-of-contents entry. The exception is the
# NUL-byte sweep, which reads `.cs` along with every other tracked text file. `CI` runs the suite on
# every pull request including a draft, so what is skipped here is an earlier verdict rather than the
# verdict — for that one invariant genuinely deferred, and for the rest nothing to ask about at all.
verify_full_skips_workflow_contracts_for_a_csharp_only_change() {
  : > "$workflow_invocation_log"
  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content '' "$workflow_invocation_log"
  assert_contains 'msbuild .config/CodeCoverage.proj' "$invocation_log"
}

# A marker and a table-of-contents entry name a path, so a file that stops being where it was is what
# leaves one of them resolving to nothing — and the files that remain say nothing about it. The
# deletion is staged for the same reason the documentation change is.
verify_full_runs_workflow_contracts_when_the_branch_removed_a_path() {
  : > "$workflow_invocation_log"
  git -C "$repository_root" rm --quiet tracked.txt

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  git -C "$repository_root" checkout --quiet HEAD -- tracked.txt
  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
}

# The suite runs beside the dotnet chain rather than in front of it, so a failing suite no longer
# means an unspent build: both are already running when either fails. What the gate owes is the
# verdict, and it still refuses — having reported both answers rather than only the first. This
# contract asserts the consequence rather than the concurrency: a test that timed two clocks against
# each other would be measuring the machine.
verify_full_fails_when_workflow_contracts_fail_beside_a_running_chain() {
  : > "$invocation_log"
  : > "$workflow_invocation_log"
  stage_documentation_change

  if (
    export FAKE_WORKFLOW_FAIL=1
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ); then
    discard_documentation_change
    printf 'verify-full.sh succeeded despite failing workflow contracts\n' >&2
    return 1
  fi

  discard_documentation_change
  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
  assert_contains 'msbuild .config/CodeCoverage.proj' "$invocation_log"
}

# The other order, which keeps the shape it had. A broken build is not a tree worth reporting
# contract findings about, so the suite is stopped rather than waited out and the gate answers at the
# speed of the failure it already has.
verify_full_stops_the_contract_suite_once_the_chain_failed() {
  local gate_output="$test_directory/verify-full-chain-failure-output"
  stage_documentation_change

  if (
    export FAKE_DOTNET_FAIL_MATCH='build backend/MailFathom.slnx'
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$gate_output" 2>&1; then
    discard_documentation_change
    printf 'verify-full.sh succeeded despite a failing build\n' >&2
    return 1
  fi

  discard_documentation_change
  assert_contains 'its verdict was not collected' "$gate_output"
  assert_excludes 'the contract suite ran' "$gate_output"
}

# A failing suite is the reason a gate says no, so its output has to arrive at the gate's own stdout
# rather than only in the file it was redirected to while it ran.
verify_full_relays_what_the_contract_suite_printed() {
  local gate_output="$test_directory/verify-full-output"
  : > "$workflow_invocation_log"
  stage_documentation_change

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$gate_output" 2>&1

  discard_documentation_change
  assert_contains 'the contract suite ran' "$gate_output"
}

# The whole point of a record: a second run over content the first one already passed reports the
# first rather than repeating it.
verify_fast_skips_a_tree_it_already_proved() {
  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  assert_file_content '' "$invocation_log"
}

# The recorded time is the whole evidence a skip offers, so it has to name the run that did the work.
# A skipped run that re-stamped it would push that time forward on every rerun until it described
# nothing. The sentinel is what makes this deterministic: two runs a second apart would otherwise
# write the same timestamp and the contract would pass without asserting anything.
verify_full_leaves_the_record_alone_when_it_skips() {
  local record="$repository_root/artifacts/verify/verify-full.digest"
  local digest

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  digest="$(head --lines=1 "$record")"
  printf '%s\nthe-run-that-did-the-work\n' "$digest" > "$record"

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content "$digest"$'\nthe-run-that-did-the-work' "$record"
}

verify_fast_runs_again_once_the_tree_changed() {
  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  printf 'namespace Fixture;\n\n// changed\n' > "$repository_root/backend/src/Sample.cs"
  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  git -C "$repository_root" checkout --quiet HEAD -- backend/src/Sample.cs
  assert_contains 'build backend/MailFathom.slnx --configuration Release --no-restore' "$invocation_log"
}

# The full gate builds, tests, collects coverage over the same suite, and verifies the formatting the
# loop repairs, so its record answers for the loop as well — over the service solution, which is as
# far as that gate reads. The contract below is the other side of that sentence.
verify_fast_accepts_the_record_the_full_gate_wrote() {
  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  assert_file_content '' "$invocation_log"
}

# And it answers on a client change too, which it did not while the full gate read the service
# solution alone. The gate now verifies the client solution whole, so a passing record says every
# file in it is already formatted — which leaves the loop's repairing pass over a subset of those
# files one possible outcome. The record therefore subsumes the loop in both stacks or in neither,
# and there is no branch shape that has to read only its own.
verify_fast_accepts_the_full_gate_record_for_a_client_change() {
  add_client_change

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  remove_client_change
  assert_file_content '' "$invocation_log"
}

# And never the other way round — with one exception, which is why both halves are asserted from one
# run. Passing the loop says nothing about coverage or the contract suite, so those are asked again;
# it does settle the formatting, because the repairing pass is the same tool over the same file set
# and a record exists only where it rewrote nothing, which leaves the verifying pass one possible
# answer.
verify_full_refuses_the_fast_loop_record_except_for_the_formatting_pass() {
  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  assert_contains 'msbuild .config/CodeCoverage.proj' "$invocation_log"
  assert_excludes 'format backend/MailFathom.slnx' "$invocation_log"
}

# A run whose formatting pass rewrote a file verified a build and a suite against content the working
# tree no longer holds, so it records nothing and the next run does the work.
verify_fast_records_nothing_when_formatting_rewrote_a_file() {
  (
    export FAKE_DOTNET_REWRITE="$repository_root/backend/src/Sample.cs"
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  git -C "$repository_root" checkout --quiet HEAD -- backend/src/Sample.cs
  : > "$invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  assert_contains 'build backend/MailFathom.slnx --configuration Release --no-restore' "$invocation_log"
}

verify_force_runs_everything_a_record_would_have_skipped() {
  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  : > "$invocation_log"

  (
    export VERIFY_FORCE=1
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  )

  assert_contains 'build backend/MailFathom.slnx --configuration Release --no-restore' "$invocation_log"
}

# A record says a gate passed, so a gate that failed writes none — however far it got. The whitespace
# check is the last step of the full gate and runs after everything the record would skip, which
# makes a run that fails there the case that would be easiest to record by accident.
verify_full_records_nothing_when_it_failed() {
  printf 'trailing \n' > "$repository_root/tracked.txt"
  git -C "$repository_root" add tracked.txt
  git -C "$repository_root" commit --quiet -m 'committed trailing whitespace'

  if (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ); then
    git -C "$repository_root" reset --quiet --hard HEAD~1
    printf 'verify-full.sh accepted committed trailing whitespace\n' >&2
    return 1
  fi

  : > "$invocation_log"

  if (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ); then
    git -C "$repository_root" reset --quiet --hard HEAD~1
    printf 'verify-full.sh accepted committed trailing whitespace on a second run\n' >&2
    return 1
  fi

  git -C "$repository_root" reset --quiet --hard HEAD~1
  assert_contains 'msbuild .config/CodeCoverage.proj' "$invocation_log"
}

# The one change that can move the formatting verdict on a file it never opened. `.editorconfig`
# carries the rules themselves, and the shared MSBuild files, the SDK pin, and the solution decide
# which of them run and over what — so this is where the whole solution is still worth its cost.
verify_full_formats_the_whole_solution_when_a_shared_style_input_changed() {
  : > "$invocation_log"
  printf 'root = true\n' > "$repository_root/.editorconfig"
  git -C "$repository_root" add .editorconfig

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  git -C "$repository_root" rm --quiet --force --cached .editorconfig
  rm -f "$repository_root/.editorconfig"

  assert_contains 'format backend/MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic' "$invocation_log"
  assert_excludes '--include' "$invocation_log"
}

# A deleted style input decides as much as an edited one, and it is the case a list of the files that
# still exist cannot see. The rules a nested `.editorconfig` carried stop applying the moment it is
# gone, so every file beneath it is read against the ones above from that commit on — without any of
# them having been touched.
verify_full_formats_the_whole_solution_when_a_shared_style_input_was_removed() {
  : > "$invocation_log"
  printf 'root = true\n' > "$repository_root/backend/src/.editorconfig"
  git -C "$repository_root" add backend/src/.editorconfig
  git -C "$repository_root" commit --quiet -m 'nested editorconfig'
  git -C "$repository_root" rm --quiet backend/src/.editorconfig

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  git -C "$repository_root" reset --quiet --hard HEAD~1

  assert_contains 'format backend/MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic' "$invocation_log"
  assert_excludes '--include' "$invocation_log"
}

# A change that wrote no C# file has nothing for `dotnet format` to be asked about, in either
# direction: there is no file to verify and no shared input that would widen the scope to the
# solution. The suite still runs, because that change is exactly what it reads.
# A change the service filter reaches without carrying a C# file: the solution is built and tested,
# because a file it reads moved, and nothing is formatted, because formatting is a property of a file
# and this branch wrote none the formatter reads. The non-C# path is under `backend/src/` rather than
# one of the shared style inputs, which have a whole-solution pass of their own two contracts below.
verify_full_formats_nothing_when_no_csharp_file_changed() {
  : > "$invocation_log"
  : > "$workflow_invocation_log"
  git -C "$repository_root" checkout --quiet --detach origin/main
  # `backend/src/` holds only the fixture branch's C# file, so it does not exist at the base.
  mkdir --parents "$repository_root/backend/src"
  printf 'note\n' > "$repository_root/backend/src/note.txt"
  git -C "$repository_root" add backend/src/note.txt

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  git -C "$repository_root" rm --quiet --force --cached backend/src/note.txt
  rm -f "$repository_root/backend/src/note.txt"
  git -C "$repository_root" checkout --quiet "$fixture_branch"

  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
  assert_contains 'msbuild .config/CodeCoverage.proj' "$invocation_log"
  assert_excludes 'format backend/MailFathom.slnx' "$invocation_log"
}

verify_full_checks_committed_staged_and_unstaged_changes() {
  local committed_output="$test_directory/committed-diff-output"
  local staged_output="$test_directory/staged-diff-output"
  local unstaged_output="$test_directory/unstaged-diff-output"

  printf 'clean\n\n' > "$repository_root/tracked.txt"
  git -C "$repository_root" add tracked.txt
  git -C "$repository_root" commit --quiet -m 'introduce committed whitespace'

  if (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$committed_output" 2>&1; then
    printf 'verify-full.sh ignored committed whitespace errors\n' >&2
    return 1
  fi

  assert_contains 'new blank line at EOF.' "$committed_output"

  printf 'clean\n' > "$repository_root/tracked.txt"
  git -C "$repository_root" add tracked.txt
  git -C "$repository_root" commit --quiet -m 'remove committed whitespace'

  printf 'clean\n\n' > "$repository_root/tracked.txt"
  git -C "$repository_root" add tracked.txt

  if (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$staged_output" 2>&1; then
    printf 'verify-full.sh ignored staged whitespace errors\n' >&2
    return 1
  fi

  assert_contains 'new blank line at EOF.' "$staged_output"

  git -C "$repository_root" restore --staged tracked.txt

  if (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$unstaged_output" 2>&1; then
    printf 'verify-full.sh ignored unstaged whitespace errors\n' >&2
    return 1
  fi

  assert_contains 'new blank line at EOF.' "$unstaged_output"

  git -C "$repository_root" restore tracked.txt
}

verify_full_rejects_untracked_files() {
  local untracked_output="$test_directory/untracked-output"

  printf 'untracked\n\n' > "$repository_root/untracked.txt"

  if (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$untracked_output" 2>&1; then
    rm -f "$repository_root/untracked.txt"
    printf 'verify-full.sh ignored an untracked file\n' >&2
    return 1
  fi

  rm -f "$repository_root/untracked.txt"
  assert_contains 'Untracked files must be staged or removed before full verification:' "$untracked_output"
  assert_contains 'untracked.txt' "$untracked_output"
}

verify_full_fetches_the_remote_base_before_verifying() {
  git -C "$repository_root" update-ref -d refs/remotes/origin/main

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  [[ "$(git -C "$repository_root" rev-parse refs/remotes/origin/main)" == "$(git -C "$remote_repository_root" rev-parse main)" ]]
}

verify_full_stops_when_head_is_behind_origin_main() {
  local behind_output="$test_directory/behind-origin-main-output"
  local script_status=0

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  git -C "$remote_repository_root" commit --quiet --allow-empty -m 'remote main moved ahead'

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$behind_output" 2>&1 || script_status=$?

  git -C "$remote_repository_root" reset --quiet --hard HEAD~1
  git -C "$repository_root" fetch --quiet --force origin main

  if ((script_status == 0)); then
    printf 'verify-full.sh accepted a branch behind origin/main\n' >&2
    return 1
  fi

  assert_contains 'HEAD does not contain the current origin/main.' "$behind_output"
  assert_file_content '' "$invocation_log"
  assert_file_content '' "$workflow_invocation_log"
}

verify_full_stops_when_the_remote_is_unreachable() {
  local unreachable_output="$test_directory/unreachable-remote-output"
  local script_status=0

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  git -C "$repository_root" remote set-url origin "$test_directory/missing-remote/Krzysztof318/MailFathom"

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$unreachable_output" 2>&1 || script_status=$?

  git -C "$repository_root" remote set-url origin "$remote_repository_root"

  if ((script_status == 0)); then
    printf 'verify-full.sh continued despite an unreachable remote\n' >&2
    return 1
  fi

  assert_contains 'verify-full.sh cannot fetch origin main.' "$unreachable_output"
  assert_file_content '' "$invocation_log"
  assert_file_content '' "$workflow_invocation_log"
}

verify_full_stops_when_a_stale_tracking_ref_hides_remote_movement() {
  local stale_base_output="$test_directory/stale-tracking-ref-output"
  local script_status=0

  : > "$invocation_log"
  git -C "$repository_root" config --unset remote.origin.fetch
  git -C "$repository_root" update-ref refs/remotes/origin/main HEAD
  git -C "$remote_repository_root" commit --quiet --allow-empty -m 'remote main moved past the stale tracking ref'

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$stale_base_output" 2>&1 || script_status=$?

  git -C "$remote_repository_root" reset --quiet --hard HEAD~1
  git -C "$repository_root" config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
  git -C "$repository_root" fetch --quiet --force origin main

  if ((script_status == 0)); then
    printf 'verify-full.sh accepted a stale remote-tracking ref\n' >&2
    return 1
  fi

  assert_contains 'HEAD does not contain the current origin/main.' "$stale_base_output"
  assert_file_content '' "$invocation_log"
}

verify_fast_refuses_the_main_branch() {
  local refusal_output="$test_directory/verify-fast-on-main-output"
  local script_status=0

  : > "$invocation_log"
  git -C "$repository_root" checkout --quiet main

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ) > "$refusal_output" 2>&1 || script_status=$?

  git -C "$repository_root" checkout --quiet "$fixture_branch"

  if ((script_status == 0)); then
    printf 'verify-fast.sh accepted a run on main\n' >&2
    return 1
  fi

  assert_contains 'verify-fast.sh must not run on main.' "$refusal_output"
  assert_file_content '' "$invocation_log"
}

verify_full_refuses_the_master_branch() {
  local refusal_output="$test_directory/verify-full-on-master-output"
  local script_status=0

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  git -C "$repository_root" checkout --quiet -B master

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ) > "$refusal_output" 2>&1 || script_status=$?

  git -C "$repository_root" checkout --quiet "$fixture_branch"
  git -C "$repository_root" branch --quiet -D master

  if ((script_status == 0)); then
    printf 'verify-full.sh accepted a run on master\n' >&2
    return 1
  fi

  assert_contains 'verify-full.sh must not run on master.' "$refusal_output"
  assert_file_content '' "$invocation_log"
  assert_file_content '' "$workflow_invocation_log"
}

verify_fast_accepts_a_detached_head() {
  local script_status=0

  : > "$invocation_log"
  git -C "$repository_root" checkout --quiet --detach HEAD

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ) > /dev/null 2>&1 || script_status=$?

  git -C "$repository_root" checkout --quiet "$fixture_branch"

  if ((script_status != 0)); then
    printf 'verify-fast.sh refused a detached HEAD\n' >&2
    return 1
  fi

  assert_file_content \
    $'restore backend/MailFathom.slnx --locked-mode\nbuild backend/MailFathom.slnx --configuration Release --no-restore\ntest --solution backend/MailFathom.slnx --configuration Release --no-build\nformat backend/MailFathom.slnx --no-restore --include backend/src/Sample.cs' \
    "$invocation_log"
}

verify_fast_skips_formatting_when_no_csharp_file_changed() {
  local script_status=0

  : > "$invocation_log"
  git -C "$repository_root" checkout --quiet --detach origin/main
  # `backend/src/` holds only the fixture branch's C# file, so it does not exist at the base.
  mkdir --parents "$repository_root/backend/src"
  printf 'note\n' > "$repository_root/backend/src/note.txt"
  git -C "$repository_root" add backend/src/note.txt

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ) > /dev/null 2>&1 || script_status=$?

  git -C "$repository_root" rm --quiet --force --cached backend/src/note.txt
  rm -f "$repository_root/backend/src/note.txt"
  git -C "$repository_root" checkout --quiet "$fixture_branch"

  if ((script_status != 0)); then
    printf 'verify-fast.sh failed with nothing to format\n' >&2
    return 1
  fi

  assert_file_content \
    $'restore backend/MailFathom.slnx --locked-mode\nbuild backend/MailFathom.slnx --configuration Release --no-restore\ntest --solution backend/MailFathom.slnx --configuration Release --no-build' \
    "$invocation_log"
}

verification_stops_after_first_failure() {
  : > "$invocation_log"

  if (
    export FAKE_DOTNET_FAIL_MATCH='build backend/MailFathom.slnx'
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ); then
    printf 'verify-fast.sh succeeded despite the configured build failure\n' >&2
    return 1
  fi

  assert_file_content \
    $'restore backend/MailFathom.slnx --locked-mode\nbuild backend/MailFathom.slnx --configuration Release --no-restore' \
    "$invocation_log"
}

workspace_inspection_is_read_only_and_labeled() {
  local before_head
  local after_head
  local before_index
  local after_index
  local before_refs="$test_directory/refs-before"
  local after_refs="$test_directory/refs-after"
  local before_status="$test_directory/status-before"
  local after_status="$test_directory/status-after"
  local output_file="$test_directory/workspace-output"

  before_head="$(git -C "$repository_root" rev-parse HEAD)"
  before_index="$(git -C "$repository_root" hash-object "$(git -C "$repository_root" rev-parse --git-path index)")"
  git -C "$repository_root" for-each-ref --format='%(refname) %(objectname)' > "$before_refs"
  git -C "$repository_root" status --porcelain=v2 --branch > "$before_status"

  (
    cd "$repository_root/docs"
    "$scripts_directory/inspect-workspace.sh"
  ) > "$output_file"

  after_head="$(git -C "$repository_root" rev-parse HEAD)"
  after_index="$(git -C "$repository_root" hash-object "$(git -C "$repository_root" rev-parse --git-path index)")"
  git -C "$repository_root" for-each-ref --format='%(refname) %(objectname)' > "$after_refs"
  git -C "$repository_root" status --porcelain=v2 --branch > "$after_status"

  [[ "$before_head" == "$after_head" ]]
  [[ "$before_index" == "$after_index" ]]
  cmp -s "$before_refs" "$after_refs"
  cmp -s "$before_status" "$after_status"
  assert_contains 'Repository:' "$output_file"
  assert_contains 'Branch:' "$output_file"
  assert_contains 'Worktree:' "$output_file"
  assert_contains 'Upstream:' "$output_file"
  assert_contains 'Base branch: origin/main' "$output_file"
  assert_contains 'Contains base branch:' "$output_file"
  assert_contains 'Working tree:' "$output_file"
  assert_contains 'Registered worktrees:' "$output_file"
  assert_contains '.NET SDK:' "$output_file"
}

workspace_inspection_reports_unavailable_sdk() {
  local output_file="$test_directory/unavailable-sdk-output"

  (
    export FAKE_DOTNET_FAIL_MATCH='--version'
    cd "$repository_root"
    "$scripts_directory/inspect-workspace.sh"
  ) > "$output_file"

  assert_contains '.NET SDK: unavailable' "$output_file"
}

# A contributor's workspace: an ordinary clone of a fork, on a branch they named. The contracts below
# are what stops the gates from silently verifying such a branch against the fork's own `main`, which
# is whatever the contributor last synced rather than the base the pull request will merge into.
#
# The fork is a real second repository rather than a renamed remote, because the failure this guards
# against is exactly that `origin` resolves to a repository that is not MailFathom.
create_fork_fixture() {
  local fork_root="$1"
  local fork_remote_root="$test_directory/fork/a-contributor/MailFathom"

  rm -rf "$fork_root" "$fork_remote_root"
  git clone --quiet "$remote_repository_root" "$fork_remote_root"
  git clone --quiet "$fork_remote_root" "$fork_root"
  git -C "$fork_root" config user.email agent-workflow@example.invalid
  git -C "$fork_root" config user.name 'Agent Workflow Tests'
  git -C "$fork_root" checkout --quiet -b 'fix-the-listing-bug'
}

fork_workspace_resolves_its_base_against_the_upstream_remote() {
  local fork_root="$test_directory/fork-checkout"
  local output_file="$test_directory/fork-workspace-output"

  create_fork_fixture "$fork_root"
  git -C "$fork_root" remote add upstream "$remote_repository_root"
  git -C "$fork_root" fetch --quiet upstream main

  (
    cd "$fork_root"
    "$scripts_directory/inspect-workspace.sh"
  ) > "$output_file"

  assert_contains 'Branch: fix-the-listing-bug' "$output_file"
  assert_contains 'Base branch: upstream/main' "$output_file"
  assert_contains 'Contains base branch: yes' "$output_file"
}

# The fork's own `origin` is not MailFathom, so nothing here is a base. Reporting `unresolved` rather
# than falling back to `origin/main` is the whole point: the fallback is the answer that looks right.
fork_workspace_reports_an_unresolved_base_without_an_upstream_remote() {
  local fork_root="$test_directory/fork-checkout-no-upstream"
  local output_file="$test_directory/fork-no-upstream-output"

  create_fork_fixture "$fork_root"

  (
    cd "$fork_root"
    "$scripts_directory/inspect-workspace.sh"
  ) > "$output_file"

  assert_contains 'Base branch: unresolved' "$output_file"
  assert_excludes 'Contains base branch: yes' "$output_file"
}

# An owner whose name merely ends with the canonical one. The base remote is identified by the
# repository a remote points at, and a suffix match has no left-hand boundary: `notkrzysztof318` and
# `some-krzysztof318` both end in `krzysztof318`, so a raw suffix would accept either as MailFathom
# and the gate would fetch and compare against a stranger's `main`. That is the silent wrong base the
# resolver exists to prevent, and it is the one case the exact-match fixtures above cannot reach.
fork_workspace_refuses_an_owner_whose_name_merely_ends_with_the_canonical_one() {
  local fork_root="$test_directory/fork-lookalike-owner"
  local lookalike_remote_root="$test_directory/lookalike/notKrzysztof318/MailFathom"
  local output_file="$test_directory/fork-lookalike-output"
  local refusal_output="$test_directory/fork-lookalike-refusal"
  local script_status=0

  create_fork_fixture "$fork_root"
  git clone --quiet "$remote_repository_root" "$lookalike_remote_root"
  git -C "$fork_root" remote add upstream "$lookalike_remote_root"

  (
    cd "$fork_root"
    "$scripts_directory/inspect-workspace.sh"
  ) > "$output_file"

  assert_contains 'Base branch: unresolved' "$output_file"

  # Cleared after the inspection rather than before it, because inspecting a workspace reports the
  # SDK version and that is a `dotnet` call of its own. What the assertion below is about is that the
  # gate refused before it built anything.
  : > "$invocation_log"

  (
    cd "$fork_root"
    "$scripts_directory/verify-full.sh"
  ) > "$refusal_output" 2>&1 || script_status=$?

  if ((script_status == 0)); then
    printf 'verify-full.sh accepted a remote whose owner merely ends with the canonical one\n' >&2
    return 1
  fi

  assert_contains 'No remote points at Krzysztof318/MailFathom' "$refusal_output"
  assert_file_content '' "$invocation_log"
}

# The full gate fetches and compares against the upstream remote, so a fork branch cut from a base
# that has since moved fails the same way the owner's own branch does — and passes the same way.
verify_full_verifies_a_fork_against_its_upstream_remote() {
  local fork_root="$test_directory/fork-verify-full"

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  create_fork_fixture "$fork_root"
  git -C "$fork_root" remote add upstream "$remote_repository_root"
  # The branch carries a documentation change so the gate reaches the whole-tree contracts, which is
  # what the assertion below reads as evidence that the run went through rather than stopping early.
  printf 'note\n' > "$fork_root/NOTES.md"
  git -C "$fork_root" add NOTES.md
  git -C "$fork_root" commit --quiet -m 'fork documentation change'

  (
    cd "$fork_root"
    "$scripts_directory/verify-full.sh"
  )

  # Written by the fetch inside the run rather than by the fixture, which never fetched upstream.
  [[ "$(git -C "$fork_root" rev-parse refs/remotes/upstream/main)" == "$(git -C "$remote_repository_root" rev-parse main)" ]]
  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
}

verify_full_stops_when_a_fork_branch_is_behind_upstream_main() {
  local fork_root="$test_directory/fork-behind-upstream"
  local behind_output="$test_directory/fork-behind-upstream-output"
  local script_status=0

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  create_fork_fixture "$fork_root"
  git -C "$fork_root" remote add upstream "$remote_repository_root"
  git -C "$remote_repository_root" commit --quiet --allow-empty -m 'upstream main moved ahead'

  (
    cd "$fork_root"
    "$scripts_directory/verify-full.sh"
  ) > "$behind_output" 2>&1 || script_status=$?

  git -C "$remote_repository_root" reset --quiet --hard HEAD~1

  if ((script_status == 0)); then
    printf 'verify-full.sh accepted a fork branch behind upstream/main\n' >&2
    return 1
  fi

  assert_contains 'HEAD does not contain the current upstream/main.' "$behind_output"
  assert_file_content '' "$invocation_log"
  assert_file_content '' "$workflow_invocation_log"
}

# The message is the whole value of this refusal. A gate that stops without naming the command that
# fixes it is indistinguishable, to a first-time contributor, from a repository that does not build.
verify_full_refuses_a_checkout_with_no_upstream_remote() {
  local fork_root="$test_directory/fork-no-upstream-gate"
  local refusal_output="$test_directory/fork-no-upstream-gate-output"
  local script_status=0

  : > "$invocation_log"
  : > "$workflow_invocation_log"
  create_fork_fixture "$fork_root"

  (
    cd "$fork_root"
    "$scripts_directory/verify-full.sh"
  ) > "$refusal_output" 2>&1 || script_status=$?

  if ((script_status == 0)); then
    printf 'verify-full.sh verified a fork with no upstream remote\n' >&2
    return 1
  fi

  assert_contains 'No remote points at Krzysztof318/MailFathom' "$refusal_output"
  assert_contains 'git remote add upstream https://github.com/Krzysztof318/MailFathom.git' "$refusal_output"
  assert_file_content '' "$invocation_log"
  assert_file_content '' "$workflow_invocation_log"
}

# The fast loop only decides which files to format, so a fork with no upstream costs a narrower
# scope rather than a refusal. It must still run: a contributor fixing their remotes cannot be
# blocked from building and testing meanwhile.
verify_fast_runs_in_a_fork_with_no_upstream_remote() {
  local fork_root="$test_directory/fork-verify-fast"

  : > "$invocation_log"
  create_fork_fixture "$fork_root"
  mkdir -p "$fork_root/backend/src"
  printf 'namespace Fork;\n' > "$fork_root/backend/src/ForkSample.cs"

  (
    cd "$fork_root"
    "$scripts_directory/verify-fast.sh"
  )

  assert_contains 'format backend/MailFathom.slnx --no-restore --include backend/src/ForkSample.cs' "$invocation_log"
}

# The `Protected paths` workflow never checks the branch out, so its guard cannot be a script in
# `scripts/` that the job calls: it is one shell block inside the YAML. Extracting that block
# verbatim from the committed file is what lets these contracts run the same code the runner runs
# rather than a copy that drifts from it. The block is the step's `run: |`, indented ten spaces, and
# it is the last thing in the file; `bash -n` on the result is what notices if that stops being true.
extract_protected_paths_step() {
  local step_script="$1"

  awk 'extracting { sub(/^          /, ""); print } /^        run: \|$/ { extracting = 1 }' \
    "$source_repository_root/.github/workflows/protected-paths.yml" > "$step_script"

  [[ -s "$step_script" ]]
  bash -n "$step_script"
}

# Returns the step's own exit status, so every caller invokes it as a condition rather than letting
# `set -e` end the test before the refusal can be inspected.
run_protected_paths_step() {
  local pull_request_author="$1"
  local changed_paths="$2"
  local output_file="$3"
  local summary_file="$4"
  local changed_file_count="${5:-$(printf '%s\n' "$changed_paths" | grep -c '')}"
  local step_script="$test_directory/protected-paths-step.sh"

  extract_protected_paths_step "$step_script"
  : > "$summary_file"

  (
    export PATH="$protected_paths_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export REPOSITORY_OWNER='Krzysztof318'
    export PULL_REQUEST_NUMBER='1'
    export PULL_REQUEST_AUTHOR="$pull_request_author"
    export CHANGED_FILE_COUNT="$changed_file_count"
    export FAKE_CHANGED_PATHS="$changed_paths"
    export GITHUB_STEP_SUMMARY="$summary_file"
    bash "$step_script"
  ) > "$output_file" 2>&1
}

protected_paths_allows_a_change_that_touches_nothing_protected() {
  local output_file="$test_directory/protected-paths-clean-output"
  local summary_file="$test_directory/protected-paths-clean-summary"

  if ! run_protected_paths_step \
    'outside-contributor' \
    $'backend/src/Domain/Emails/EmailOccurrenceId.cs\nREADME.md\ndocs/operations/agent-workflow.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths refused a pull request that changes nothing protected\n' >&2
    return 1
  fi

  assert_contains 'changes nothing under' "$output_file"
  assert_file_content '' "$summary_file"
}

protected_paths_refuses_a_contributor_changing_repository_root_configuration() {
  local output_file="$test_directory/protected-paths-root-output"
  local summary_file="$test_directory/protected-paths-root-summary"

  if run_protected_paths_step \
    'outside-contributor' \
    $'Version.props\nLICENSE\nNOTICE\nNuGet.config\nglobal.json\nCHANGELOG.md\nCLA.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed a contributor to change the repository-root configuration\n' >&2
    return 1
  fi

  local protected_file
  for protected_file in Version.props LICENSE NOTICE NuGet.config global.json CHANGELOG.md CLA.md; do
    assert_contains "::error file=${protected_file}::" "$output_file"
  done
}

protected_paths_refuses_a_contributor_changing_a_protected_directory() {
  local output_file="$test_directory/protected-paths-directory-output"
  local summary_file="$test_directory/protected-paths-directory-summary"

  # `docs/decisions/` is the entry that is neither dotted nor configuration, so it is the one whose
  # prefix match is worth asserting alongside the four that are.
  if run_protected_paths_step \
    'outside-contributor' \
    $'.github/workflows/ci.yml\n.config/BannedSymbols.txt\n.agents/skills/start-task/SKILL.md\n.claude/settings.json\ndocs/decisions/0001-application-owned-repositories-for-persistence-ports.md\ndocs/decisions/adr-template.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed a contributor to change a protected directory\n' >&2
    return 1
  fi

  local protected_directory_path
  for protected_directory_path in \
    .github/workflows/ci.yml \
    .config/BannedSymbols.txt \
    .agents/skills/start-task/SKILL.md \
    .claude/settings.json \
    docs/decisions/0001-application-owned-repositories-for-persistence-ports.md \
    docs/decisions/adr-template.md; do
    assert_contains "::error file=${protected_directory_path}::" "$output_file"
  done
}

protected_paths_matches_the_configuration_files_at_every_depth() {
  local output_file="$test_directory/protected-paths-nested-output"
  local summary_file="$test_directory/protected-paths-nested-summary"

  if run_protected_paths_step \
    'outside-contributor' \
    $'backend/src/Infrastructure/Persistence/Migrations/.editorconfig\ndocs/.gitattributes\nbackend/tests/shared/.worktreeinclude\nbackend/tests/AGENTS.md\nbackend/src/CLAUDE.md\nbackend/Directory.Build.props' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed a contributor to change a nested configuration file\n' >&2
    return 1
  fi

  assert_contains '::error file=backend/src/Infrastructure/Persistence/Migrations/.editorconfig::' "$output_file"
  assert_contains '::error file=docs/.gitattributes::' "$output_file"
  assert_contains '::error file=backend/tests/shared/.worktreeinclude::' "$output_file"
  assert_contains '::error file=backend/tests/AGENTS.md::' "$output_file"
  assert_contains '::error file=backend/src/CLAUDE.md::' "$output_file"
  assert_contains '::error file=backend/Directory.Build.props::' "$output_file"
}

protected_paths_ignores_paths_that_only_resemble_a_protected_one() {
  local output_file="$test_directory/protected-paths-resemblance-output"
  local summary_file="$test_directory/protected-paths-resemblance-summary"

  # A protected name is anchored to a path segment and a protected file to the repository root, so a
  # longer name beginning the same way, a suffix of one, and a copy placed elsewhere all pass.
  if ! run_protected_paths_step \
    'outside-contributor' \
    $'docs/my.editorconfig\n.editorconfiguration\ndeploy/global.json\nbackend/src/Host/NOTICE.md\n.githubbed/stale.yml\ndocs/CONTRIBUTING-AGENTS.md\ndocs/decisions-notes.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths refused paths that only resemble a protected one\n' >&2
    return 1
  fi

  assert_contains 'changes nothing under' "$output_file"
  assert_excludes '::error' "$output_file"
}

protected_paths_reports_the_paths_it_found_when_the_owner_is_the_author() {
  local output_file="$test_directory/protected-paths-owner-output"
  local summary_file="$test_directory/protected-paths-owner-summary"

  # Login casing must not decide the gate, so the fixture spells the owner differently from the
  # repository. The pass still has to name what it let through.
  if ! run_protected_paths_step \
    'krzysztof318' \
    $'.config/BannedSymbols.txt\n.gitattributes\n.gitattributes\nglobal.json' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths refused a pull request authored by the repository owner\n' >&2
    return 1
  fi

  assert_contains 'changes 3 protected path(s)' "$output_file"
  assert_contains 'The repository owner authored this pull request' "$output_file"
  assert_excludes '::error' "$output_file"

  assert_contains '### Protected paths this pull request changes' "$summary_file"
  assert_contains '- `.config/BannedSymbols.txt`' "$summary_file"
  assert_contains '- `.gitattributes`' "$summary_file"
  assert_contains '- `global.json`' "$summary_file"
}

protected_paths_allows_dependabot_to_update_the_workflows() {
  local output_file="$test_directory/protected-paths-dependabot-output"
  local summary_file="$test_directory/protected-paths-dependabot-summary"

  # A Dependabot pull request against an action looks like this one, and without the exception it
  # would be permanently unmergeable against a required check.
  if ! run_protected_paths_step \
    'dependabot[bot]' \
    $'.github/workflows/ci.yml\n.github/workflows/codeql.yml\n.github/workflows/nightly.yml' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths refused a Dependabot pull request that only updates the workflows\n' >&2
    return 1
  fi

  assert_contains 'Dependabot authored this pull request' "$output_file"
  assert_excludes '::error' "$output_file"

  # The pass still names what it let through, exactly as the owner's does.
  assert_contains '- `.github/workflows/ci.yml`' "$summary_file"
}

protected_paths_refuses_dependabot_outside_the_workflows() {
  local output_file="$test_directory/protected-paths-dependabot-wide-output"
  local summary_file="$test_directory/protected-paths-dependabot-wide-summary"

  # The author alone does not decide this check. A dependency update edits the workflows; an ADR
  # arriving under the same login is not one whatever it calls itself, and the exception is scoped so
  # that it says so rather than taking the author's word for what the change is.
  if run_protected_paths_step \
    'dependabot[bot]' \
    $'.github/workflows/ci.yml\ndocs/decisions/0001-application-owned-repositories-for-persistence-ports.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed Dependabot to change a protected path outside the workflows\n' >&2
    return 1
  fi

  assert_contains 'outside .github/workflows/: docs/decisions/0001-application-owned-repositories-for-persistence-ports.md' "$output_file"
}

protected_paths_refuses_an_author_merely_resembling_dependabot() {
  local output_file="$test_directory/protected-paths-dependabot-lookalike-output"
  local summary_file="$test_directory/protected-paths-dependabot-lookalike-summary"

  # The bracketed suffix is part of the login GitHub sets, and it is what a chosen account name
  # cannot contain. Matching it literally rather than by prefix is what keeps `dependabot` — a login
  # anybody could have registered — outside the exception.
  if run_protected_paths_step \
    'dependabot' \
    '.github/workflows/ci.yml' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed an author whose login only resembles the updater\n' >&2
    return 1
  fi

  assert_contains '::error file=.github/workflows/ci.yml::' "$output_file"
}

protected_paths_refuses_a_pull_request_larger_than_the_reportable_limit() {
  local output_file="$test_directory/protected-paths-oversized-output"
  local summary_file="$test_directory/protected-paths-oversized-summary"

  if run_protected_paths_step \
    'outside-contributor' \
    'README.md' \
    "$output_file" \
    "$summary_file" \
    3001; then
    printf 'Protected paths passed a pull request too large for the changed-files endpoint\n' >&2
    return 1
  fi

  assert_contains 'cannot be verified' "$output_file"
}

# The ceiling refuses an answer that could not be computed, and the owner is the one author for whom
# that answer decides nothing: every protected path is theirs to change, so the enumeration could only
# have named which ones. Refusing them there would block a tree-wide rename on a list nobody acts on.
# The pass still says the list is missing, which is the part the owner does read.
protected_paths_lets_the_owner_past_the_reportable_limit() {
  local output_file="$test_directory/protected-paths-oversized-owner-output"
  local summary_file="$test_directory/protected-paths-oversized-owner-summary"

  if ! run_protected_paths_step \
    'Krzysztof318' \
    'README.md' \
    "$output_file" \
    "$summary_file" \
    3001; then
    printf 'Protected paths refused the owner a pull request too large for the changed-files endpoint\n' >&2
    return 1
  fi

  assert_contains 'cannot be listed' "$output_file"
  assert_excludes '::error' "$output_file"
  assert_file_content '' "$summary_file"
}

# The `Typo check` workflow decides in shell which files it hands the checker, and that decision is
# the whole of what this repository wrote: the checking itself belongs to a pinned action. Extracting
# the block verbatim is again what makes the contract run the runner's own code. The block ends at
# the first line that leaves its ten-space indentation, so the step that follows it in the workflow
# changes nothing here.
extract_typo_check_step() {
  local step_script="$1"

  awk '
    $0 == "        id: changed-files" { found = 1; next }
    found && !extracting && /^        run: \|$/ { extracting = 1; next }
    extracting {
      if ($0 != "" && $0 !~ /^          /) { exit }
      sub(/^          /, "")
      print
    }
  ' "$source_repository_root/.github/workflows/typo-check.yml" > "$step_script"

  [[ -s "$step_script" ]]
  bash -n "$step_script"
}

# Returns the step's own exit status and writes the `GITHUB_OUTPUT` file the workflow would read, so
# a caller asserts on what the next step is handed rather than on what the log says about it.
run_typo_check_step() {
  local changed_paths="$1"
  local output_file="$2"
  local step_output_file="$3"
  local changed_file_count="${4:-$(printf '%s\n' "$changed_paths" | grep -c '')}"
  local step_script="$test_directory/typo-check-step.sh"

  extract_typo_check_step "$step_script"
  : > "$step_output_file"

  (
    export PATH="$typo_check_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export CHANGED_FILE_COUNT="$changed_file_count"
    export FAKE_CHANGED_PATHS="$changed_paths"
    export GITHUB_OUTPUT="$step_output_file"
    bash "$step_script"
  ) > "$output_file" 2>&1
}

typo_check_passes_the_files_the_pull_request_changed() {
  local output_file="$test_directory/typo-check-changed-output"
  local step_output_file="$test_directory/typo-check-changed-step-output"

  if ! run_typo_check_step \
    $'README.md\nbackend/src/Domain/Emails/EmailAddress.cs\n.github/workflows/typo-check.yml' \
    "$output_file" \
    "$step_output_file"; then
    printf 'Typo check failed to collect an ordinary changed-file list\n' >&2
    return 1
  fi

  assert_file_content \
    'files=README.md backend/src/Domain/Emails/EmailAddress.cs .github/workflows/typo-check.yml' \
    "$step_output_file"
  assert_contains 'changes 3 file(s)' "$output_file"
}

typo_check_leaves_an_image_out_of_the_file_list() {
  local output_file="$test_directory/typo-check-image-output"
  local step_output_file="$test_directory/typo-check-image-step-output"

  if ! run_typo_check_step \
    $'README.md\nassets/icon-180.png' \
    "$output_file" \
    "$step_output_file"; then
    printf 'Typo check failed to collect a changed-file list containing an image\n' >&2
    return 1
  fi

  assert_file_content 'files=README.md' "$step_output_file"
}

typo_check_checks_nothing_when_a_pull_request_only_changes_images() {
  local output_file="$test_directory/typo-check-images-only-output"
  local step_output_file="$test_directory/typo-check-images-only-step-output"

  if ! run_typo_check_step \
    $'assets/icon-180.png\ndocs/diagram.svg.png' \
    "$output_file" \
    "$step_output_file"; then
    printf 'Typo check failed on a pull request that only changes images\n' >&2
    return 1
  fi

  # The empty list rather than the whole checkout. Falling back here would spell-check the repository
  # over a change that added no words, which is the cost the fallback exists to avoid paying twice.
  assert_file_content 'files=' "$step_output_file"
  assert_contains 'nothing to spell-check' "$output_file"
}

typo_check_checks_nothing_when_the_pull_request_only_removes_files() {
  local output_file="$test_directory/typo-check-removals-output"
  local step_output_file="$test_directory/typo-check-removals-step-output"

  if ! run_typo_check_step '' "$output_file" "$step_output_file" 2; then
    printf 'Typo check failed on a pull request that only removes files\n' >&2
    return 1
  fi

  assert_file_content 'files=' "$step_output_file"
  assert_contains 'nothing to check' "$output_file"
}

typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_whitespace() {
  local output_file="$test_directory/typo-check-whitespace-output"
  local step_output_file="$test_directory/typo-check-whitespace-step-output"

  if ! run_typo_check_step \
    $'README.md\ndocs/a file.md' \
    "$output_file" \
    "$step_output_file"; then
    printf 'Typo check failed instead of widening its scope for a path containing whitespace\n' >&2
    return 1
  fi

  assert_file_content 'files=.' "$step_output_file"
  assert_contains 'docs/a file.md' "$output_file"
}

typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_a_glob_character() {
  local output_file="$test_directory/typo-check-glob-output"
  local step_output_file="$test_directory/typo-check-glob-step-output"

  if ! run_typo_check_step \
    $'README.md\ndocs/a[1].md' \
    "$output_file" \
    "$step_output_file"; then
    printf 'Typo check failed instead of widening its scope for a path containing a glob character\n' >&2
    return 1
  fi

  assert_file_content 'files=.' "$step_output_file"
  assert_contains 'docs/a[1].md' "$output_file"
}

typo_check_falls_back_to_the_whole_checkout_for_a_pull_request_beyond_the_reportable_limit() {
  local output_file="$test_directory/typo-check-oversized-output"
  local step_output_file="$test_directory/typo-check-oversized-step-output"

  if ! run_typo_check_step 'README.md' "$output_file" "$step_output_file" 3001; then
    printf 'Typo check failed instead of widening its scope for an oversized pull request\n' >&2
    return 1
  fi

  assert_file_content 'files=.' "$step_output_file"
  assert_contains '3001 files' "$output_file"
}

# Every read in `fathom-review.yml` goes through `call-github-api.sh`, so a step extracted below
# reaches it the way the runner does — out of the checkout, at the path the workflow's `env` block
# names. The backoff base is zeroed for the same reason the settle windows are seconds: what a
# contract asserts is the decision, and a retry these fakes provoke by accident must not cost the
# suite its own timeout. The attempt budget is left at the value the script declares, because that
# is what the contracts below are about.
export_api_retry_environment() {
  export GITHUB_API_SCRIPT="$source_repository_root/.github/pull-request/call-github-api.sh"
  export API_RETRY_DELAY_SECONDS='0'
}

# The steps these contracts run are shell blocks inside `fathom-review.yml`, and they cannot be
# scripts in `scripts/` for the reason the protected-paths guard cannot: the workflow never checks
# the branch out. Extracting one verbatim is again what makes these contracts run the runner's own
# code. A block is the `run: |` under the step's `id:`, indented ten spaces, and it ends at the
# first line that leaves that indentation, so a step added after it changes nothing here.
extract_workflow_step() {
  local workflow_file="$1"
  local step_id="$2"
  local step_script="$3"

  awk -v step_declaration="        id: $step_id" '
    $0 == step_declaration { found = 1; next }
    found && !extracting && /^        run: \|$/ { extracting = 1; next }
    extracting {
      if ($0 != "" && $0 !~ /^          /) { exit }
      sub(/^          /, "")
      print
    }
  ' "$workflow_file" > "$step_script"

  [[ -s "$step_script" ]]
  bash -n "$step_script"
}

extract_fathom_review_step() {
  extract_workflow_step "$source_repository_root/.github/workflows/fathom-review.yml" "$1" "$2"
}

# The decision is read from `GITHUB_OUTPUT`, which is where the reviewing job reads it, rather than
# from the log line beside it that no other job consumes.
run_fathom_review_gate() {
  local event_action="$1"
  local output_file="$2"
  local step_output_file="$3"
  # The author and the applied label default to the ordinary case — the owner's own pull request,
  # and an event carrying no label — so a contract below names only the input it is about.
  local pull_request_author="${4:-Krzysztof318}"
  local added_label="${5:-}"
  # The reviews already on the pull request, absent by default: a first review is the ordinary case
  # and the ceiling contracts are the ones that write a history.
  local reviews_file="${6:-}"
  local step_script="$test_directory/fathom-review-gate.sh"

  extract_fathom_review_step 'gate' "$step_script"
  : > "$step_output_file"

  (
    export PATH="$fathom_review_bin_directory:$PATH"
    export FAKE_REVIEWS_FILE="$reviews_file"
    export EVENT_NAME='pull_request_target'
    export EVENT_ACTION="$event_action"
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export PULL_REQUEST_DRAFT='false'
    export PULL_REQUEST_AUTHOR="$pull_request_author"
    export HEAD_REPOSITORY='Krzysztof318/MailFathom'
    export ADDED_LABEL="$added_label"
    export IS_PULL_REQUEST_COMMENT='false'
    export COMMENT_BODY=''
    export COMMENT_ASSOCIATION=''
    export GH_TOKEN='fake-token'
    export REVIEWER_LOGIN='fathom-reviewer[bot]'
    export UPDATER_LOGIN='dependabot[bot]'
    # The markers the workflow declares once at the top of the file. The gate reads the automatic one
    # to count with; the requested one is here because a contract writes it into a fixture review.
    export AUTOMATIC_REVIEW_MARKER='<!-- fathom-review: automatic -->'
    export REQUESTED_REVIEW_MARKER='<!-- fathom-review: requested -->'
    export GITHUB_OUTPUT="$step_output_file"
    export_api_retry_environment
    bash "$step_script"
  ) > "$output_file" 2>&1
}

# The reviews the gate counts, written as the endpoint returns them: this App's automatic passes,
# this App's requested passes, and somebody else's review, which is never either.
write_fathom_review_history() {
  local automatic_count="$1"
  local requested_count="$2"
  local reviews_file="$3"

  jq -nc \
    --argjson automatic "$automatic_count" \
    --argjson requested "$requested_count" '
    [range($automatic) | {user: {login: "fathom-reviewer[bot]"}, body: "# NEEDS CHANGES\n\n<!-- fathom-review: automatic -->"}]
    + [range($requested) | {user: {login: "fathom-reviewer[bot]"}, body: "# NEEDS CHANGES\n\n<!-- fathom-review: requested -->"}]
    + [{user: {login: "Krzysztof318"}, body: "Looks right to me."}]
  ' > "$reviews_file"
}

fathom_review_reviews_a_push_to_a_published_pull_request() {
  local output_file="$test_directory/fathom-review-push-output"
  local step_output_file="$test_directory/fathom-review-push-step-output"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file"

  assert_contains 'review=true' "$step_output_file"
  # A push started this run, so the review it produces is one of the six the ceiling counts, and the
  # marker the submission writes into the body is what makes it countable.
  assert_contains 'explicit=false' "$step_output_file"
  assert_contains 'the branch was pushed to' "$output_file"
}

# The ceiling is what stops a branch pushed to forty times being reviewed forty times. These five
# contracts fix both halves of it: which reviews are counted, and which runs the count refuses.
fathom_review_reviews_a_push_below_the_automatic_ceiling() {
  local output_file="$test_directory/fathom-review-below-ceiling-output"
  local step_output_file="$test_directory/fathom-review-below-ceiling-step-output"
  local reviews_file="$test_directory/fathom-review-below-ceiling-reviews"

  write_fathom_review_history 5 0 "$reviews_file"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file" 'Krzysztof318' '' "$reviews_file"

  assert_contains 'review=true' "$step_output_file"
  # The same count the ceiling reasons about is published for the submission step, which spends it on
  # a different decision. Publishing it here is what keeps the two from drifting apart into a review
  # that settles on one pass number and a ceiling that refuses on another.
  assert_contains 'automatic_reviews=5' "$step_output_file"
}

fathom_review_stops_reviewing_a_push_at_the_automatic_ceiling() {
  local output_file="$test_directory/fathom-review-ceiling-reached-output"
  local step_output_file="$test_directory/fathom-review-ceiling-reached-step-output"
  local reviews_file="$test_directory/fathom-review-ceiling-reached-reviews"

  write_fathom_review_history 6 0 "$reviews_file"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file" 'Krzysztof318' '' "$reviews_file"

  assert_contains 'review=false' "$step_output_file"
  assert_contains 'the automatic review ceiling of 6 is reached' "$output_file"
  # The refusal names the way out, because a maintainer reading it is one label away from the pass
  # the ceiling just declined to spend.
  assert_contains 'label it fathom-review or comment fathom-review' "$output_file"
}

# A review somebody asked for is a decision already taken, so it neither counts against the budget a
# later push draws on nor is refused by it. Counting every review instead — which is what this
# replaces — let a few requested passes stop a pull request being reviewed on push at all.
fathom_review_never_counts_a_requested_review_against_the_ceiling() {
  local output_file="$test_directory/fathom-review-requested-uncounted-output"
  local step_output_file="$test_directory/fathom-review-requested-uncounted-step-output"
  local reviews_file="$test_directory/fathom-review-requested-uncounted-reviews"

  write_fathom_review_history 5 4 "$reviews_file"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file" 'Krzysztof318' '' "$reviews_file"

  assert_contains 'review=true' "$step_output_file"
}

# A review body carries the reviewer's own findings, which are model text derived from the diff. A
# requested review of a change to the workflow itself quotes the automatic marker in a finding, so
# matching the marker anywhere in the body would count that review as an automatic pass and refuse a
# later push a review it was owed. The marker counts where the submission step writes it: the last
# line.
fathom_review_counts_the_marker_only_where_the_submission_writes_it() {
  local output_file="$test_directory/fathom-review-marker-position-output"
  local step_output_file="$test_directory/fathom-review-marker-position-step-output"
  local reviews_file="$test_directory/fathom-review-marker-position-reviews"

  jq -nc '
    [range(5) | {user: {login: "fathom-reviewer[bot]"}, body: "# NEEDS CHANGES\n\n<!-- fathom-review: automatic -->"}]
    + [range(3) | {user: {login: "fathom-reviewer[bot]"},
                   body: "# NEEDS CHANGES\n\nThe gate counts a body ending in <!-- fathom-review: automatic --> and this one quotes it.\n\n<!-- fathom-review: requested -->"}]
  ' > "$reviews_file"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file" 'Krzysztof318' '' "$reviews_file"

  assert_contains 'review=true' "$step_output_file"
}

# The bar a pass applies is resolved once, here, from the same count the ceiling reads. These three
# fix it in every direction it can be got wrong: an early pass, the pass the threshold names, and a
# review somebody asked for at a count that would otherwise settle it.
fathom_review_reads_an_early_pass_at_the_full_bar() {
  local output_file="$test_directory/fathom-review-posture-early-output"
  local step_output_file="$test_directory/fathom-review-posture-early-step-output"
  local reviews_file="$test_directory/fathom-review-posture-early-reviews"

  write_fathom_review_history 2 0 "$reviews_file"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file" 'Krzysztof318' '' "$reviews_file"

  assert_contains 'review=true' "$step_output_file"
  assert_contains 'posture=full' "$step_output_file"
}

# Three published passes means this run is the fourth, which is where the measurement in the gate put
# the threshold: of the reviews published at pass 4 or later, the overwhelming majority withheld
# approval without a P1 among them.
fathom_review_settles_the_bar_from_the_fourth_pass() {
  local output_file="$test_directory/fathom-review-posture-settling-output"
  local step_output_file="$test_directory/fathom-review-posture-settling-step-output"
  local reviews_file="$test_directory/fathom-review-posture-settling-reviews"

  write_fathom_review_history 3 0 "$reviews_file"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file" 'Krzysztof318' '' "$reviews_file"

  assert_contains 'posture=settling' "$step_output_file"
  # The reason is said in the log beside the decision, because a maintainer reading a run that
  # approved a change carrying a P2 has to be able to see which bar produced that.
  assert_contains 'posture: settling after 3 automatic passes' "$output_file"
}

# A requested pass is `full` however many automatic ones came before it, for the same reason it is
# unbounded in scope: somebody spent usage asking for the reading a first pass performs.
fathom_review_keeps_a_requested_review_at_the_full_bar() {
  local output_file="$test_directory/fathom-review-posture-requested-output"
  local step_output_file="$test_directory/fathom-review-posture-requested-step-output"
  local reviews_file="$test_directory/fathom-review-posture-requested-reviews"

  write_fathom_review_history 5 0 "$reviews_file"

  run_fathom_review_gate 'labeled' "$output_file" "$step_output_file" 'Krzysztof318' 'fathom-review' "$reviews_file"

  assert_contains 'explicit=true' "$step_output_file"
  assert_contains 'posture=full' "$step_output_file"
}

fathom_review_answers_a_request_past_the_automatic_ceiling() {
  local output_file="$test_directory/fathom-review-request-past-ceiling-output"
  local step_output_file="$test_directory/fathom-review-request-past-ceiling-step-output"
  local reviews_file="$test_directory/fathom-review-request-past-ceiling-reviews"

  write_fathom_review_history 9 0 "$reviews_file"

  run_fathom_review_gate 'labeled' "$output_file" "$step_output_file" 'Krzysztof318' 'fathom-review' "$reviews_file"

  assert_contains 'review=true' "$step_output_file"
  # The other half of the same decision: a maintainer asked for this one, so it carries the requested
  # marker and never joins the count that refused the push.
  assert_contains 'explicit=true' "$step_output_file"
}

# A merge, the owner's ruleset bypass included, arrives as this event, and its whole purpose is the
# cancellation it caused before the job started. Starting a review of its own would spend
# subscription usage on a change that has already landed.
fathom_review_refuses_a_closed_pull_request() {
  local output_file="$test_directory/fathom-review-closed-output"
  local step_output_file="$test_directory/fathom-review-closed-step-output"

  run_fathom_review_gate 'closed' "$output_file" "$step_output_file"

  assert_contains 'review=false' "$step_output_file"
  assert_contains 'the pull request is closed' "$output_file"
}

# The updater opens an ordinary published pull request, so every check that contains this workflow's
# cost — the draft one especially — lets it straight through. What the reviewer would read is a
# version number, and what decides a bump is the upstream release notes and the license register
# instead.
fathom_review_refuses_a_pull_request_the_updater_opened() {
  local output_file="$test_directory/fathom-review-updater-output"
  local step_output_file="$test_directory/fathom-review-updater-step-output"

  run_fathom_review_gate 'opened' "$output_file" "$step_output_file" 'dependabot[bot]'

  assert_contains 'review=false' "$step_output_file"
  assert_contains 'authored by dependabot[bot]' "$output_file"
}

# The refusal above is a default and not a wall. A major bump that renames an input is worth the
# pass, and the maintainer reaches it the same way a draft and a fork are reached.
fathom_review_reviews_an_updater_pull_request_the_maintainer_labelled() {
  local output_file="$test_directory/fathom-review-updater-labelled-output"
  local step_output_file="$test_directory/fathom-review-updater-labelled-step-output"

  run_fathom_review_gate 'labeled' "$output_file" "$step_output_file" 'dependabot[bot]' 'fathom-review'

  assert_contains 'review=true' "$step_output_file"
  assert_contains 'a maintainer applied the fathom-review label' "$output_file"
}

# The settle step waits for the pull request's conversation to stop moving before the snapshot is
# frozen, so a reply written in the seconds after the push that started the run is inside it. The
# windows come from the step's own `env` block, which is what lets these contracts run the real loop
# against seconds instead of minutes; the values the workflow declares are not what is asserted
# here, only the decisions the loop takes between them.
run_fathom_review_settle() {
  local newest_comment="$1"
  local output_file="$2"
  local step_script="$test_directory/fathom-review-settle.sh"

  extract_fathom_review_step 'settle' "$step_script"

  (
    export PATH="$settle_bin_directory:$PATH"
    export FAKE_NEWEST_COMMENT="$newest_comment"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export SETTLE_MINIMUM_SECONDS='2'
    # Three rather than one, because the fake `gh` reports whole seconds. A conversation it answers
    # as `now` is already up to a second old by the time the loop reads its own clock, so a quiet
    # window of one second is decided by which side of a second boundary the two `date` calls fall
    # on, and the tests that require the ceiling would settle instead. Truncation can cost only one
    # second, so any window above it leaves the decision to the loop.
    export SETTLE_QUIET_SECONDS='3'
    export SETTLE_LIMIT_SECONDS='4'
    export SETTLE_POLL_SECONDS='1'
    export_api_retry_environment
    bash "$step_script"
  ) > "$output_file" 2>&1
}

assert_seconds_elapsed_at_least() {
  local minimum_seconds="$1"
  local started_at="$2"
  local elapsed=$(($(date -u +%s) - started_at))

  if ((elapsed < minimum_seconds)); then
    printf 'Expected at least %ss to elapse, but %ss did\n' "$minimum_seconds" "$elapsed" >&2
    return 1
  fi
}

assert_seconds_elapsed_below() {
  local ceiling_seconds="$1"
  local started_at="$2"
  local elapsed=$(($(date -u +%s) - started_at))

  if ((elapsed >= ceiling_seconds)); then
    printf 'Expected fewer than %ss to elapse, but %ss did\n' "$ceiling_seconds" "$elapsed" >&2
    return 1
  fi
}

# A first review has nothing to answer, so it pays none of the wait below.
fathom_review_collects_at_once_when_nobody_has_commented() {
  local output_file="$test_directory/fathom-review-settle-empty-output"
  local started_at
  started_at="$(date -u +%s)"

  run_fathom_review_settle '' "$output_file"

  assert_contains 'Nothing to settle' "$output_file"
  assert_seconds_elapsed_below 2 "$started_at"
}

# The contract this whole step exists for. On #223 the collection closed twelve seconds before the
# author's reply to the previous pass was written, and the reviewer reported the finding again as
# unanswered. A conversation that is already quiet still waits the minimum window, because the reply
# that matters is the one not written yet.
fathom_review_waits_before_freezing_a_quiet_conversation() {
  local output_file="$test_directory/fathom-review-settle-quiet-output"
  local started_at
  started_at="$(date -u +%s)"

  run_fathom_review_settle '2020-01-01T00:00:00Z' "$output_file"

  assert_contains 'Settled' "$output_file"
  assert_seconds_elapsed_at_least 2 "$started_at"
}

# The other end of it: somebody typing steadily must not hold the run open, so the wait is bounded
# and says in the log that it collected a conversation still in motion.
fathom_review_stops_waiting_at_the_ceiling() {
  local output_file="$test_directory/fathom-review-settle-ceiling-output"
  local started_at
  started_at="$(date -u +%s)"

  run_fathom_review_settle 'now' "$output_file"

  assert_contains 'still moving' "$output_file"
  assert_seconds_elapsed_at_least 4 "$started_at"
}

# Both comment endpoints return oldest first, and the per-issue one silently ignores a `sort` or
# `direction` asking otherwise, so the step must decide from the largest timestamp it saw rather
# than from the first record. A step that read the first would take the 2020 stamp here, find the
# conversation quiet, and freeze the snapshot while a reply was seconds old.
fathom_review_reads_the_newest_comment_whatever_the_order() {
  local output_file="$test_directory/fathom-review-settle-order-output"
  local started_at
  started_at="$(date -u +%s)"

  run_fathom_review_settle 'oldest-first' "$output_file"

  assert_contains 'still moving' "$output_file"
  assert_seconds_elapsed_at_least 4 "$started_at"
}

# The collection step decides what the reviewer is able to see, and one field it collects decides
# how the review is conducted rather than what it concludes: `security` on an issue the change closes
# is this project's statement that the change needs a security review before it merges, and the
# prompt turns it into a pass over every file. The prompt cannot read a label the collection drops,
# and dropping one would leave the review looking exactly as it does now — so the projection is
# pinned here.
#
# The fake `gh` answers each endpoint the step calls with a canned record and then applies the step's
# own `--jq` filter to it with the real `jq`, which is what the client it stands in for does. That is
# what makes these contracts assert the filter the workflow declares rather than a copy of it.
mkdir -p "$collect_bin_directory"
cat > "$collect_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

# The endpoint is the first bare argument: `graphql`, or the path. Everything else is a flag, its
# value, or the `--jq` filter, and only the filter is read back out.
endpoint=''
filter=''
reading_filter='false'

for argument in "$@"; do
  if [[ "$reading_filter" == 'true' ]]; then
    filter="$argument"
    reading_filter='false'
    continue
  fi

  case "$argument" in
    --jq) reading_filter='true' ;;
    api | -*) ;;
    *) [[ -n "$endpoint" ]] || endpoint="$argument" ;;
  esac
done

case "$endpoint" in
  graphql)
    # Two queries reach this endpoint, told apart by the field they ask for. The closing issues are
    # the ones GitHub resolved rather than the ones the body spells, which is why the answer here is
    # a list rather than a body for something else to parse.
    if [[ "$*" == *closingIssuesReferences* ]]; then
      response='{"data":{"repository":{"pullRequest":{"closingIssuesReferences":{"nodes":[
        {"number":11,"repository":{"nameWithOwner":"Krzysztof318/MailFathom"}},
        {"number":12,"repository":{"nameWithOwner":"Krzysztof318/MailFathom"}}]}}}}}'
    else
      response='{"data":{"repository":{"pullRequest":{"reviewThreads":{"nodes":[]}}}}}'
    fi
    ;;
  */issues/*/labels)
    # The one write these steps make, recorded rather than sent so a contract can read what would
    # have been posted, and refused on demand so the branch that carries on without the label runs
    # too — which on a fork is the ordinary path rather than a failure.
    if [[ -n "${FAKE_LABEL_REQUEST:-}" ]]; then
      printf '%s\n' "$*" > "$FAKE_LABEL_REQUEST"
    fi

    if [[ "${FAKE_LABEL_FAILS:-false}" == 'true' ]]; then
      printf 'gh: Forbidden (HTTP 403)\n' >&2
      exit 1
    fi

    exit 0
    ;;
  */actions/workflows/*/runs*)
    # The labelling runs for this head. A countdown file rather than a constant, so a contract can
    # hold one open for a fixed number of polls and then let it go — which is the only way to assert
    # that the reviewer waited rather than that it happened to read late.
    #
    # The run it holds open is `queued` rather than `in_progress`, deliberately: that is a status of
    # its own rather than a phase of running, and a step asking `status=in_progress` sees none of
    # them. A contract that answered `in_progress` would pass against the query that misses the very
    # case the wait exists for.
    pending='0'

    if [[ -n "${FAKE_LABELLING_COUNTDOWN:-}" ]]; then
      remaining="$(cat "$FAKE_LABELLING_COUNTDOWN" 2>/dev/null || printf '0')"

      if ((remaining > 0)); then
        pending='1'
        printf '%s' "$((remaining - 1))" > "$FAKE_LABELLING_COUNTDOWN"
      fi
    fi

    if [[ "$pending" == '0' ]]; then
      runs='[{"status":"completed","conclusion":"success"}]'
    else
      runs='[{"status":"queued","conclusion":null}]'
    fi

    # A `status=` in the query is honoured the way the API honours it: an exact match on the value,
    # never a family of them. That is the whole of what makes the contract above discriminate — a
    # fake that ignored the parameter would answer the same runs to a step asking for the wrong
    # status, and would pass against the query that cannot see a queued run.
    case "$endpoint" in
      *status=*)
        wanted="${endpoint##*status=}"
        wanted="${wanted%%&*}"
        runs="$(printf '%s' "$runs" | jq -c --arg wanted "$wanted" '[.[] | select(.status == $wanted)]')"
        ;;
    esac

    response="$(printf '%s' "$runs" | jq -c '{total_count: length, workflow_runs: .}')"
    ;;
  */contents/*)
    printf 'unchanged\nadded\nunchanged\n'
    exit 0
    ;;
  */compare/*)
    # What moved between the head of the previous review and this one. The narrowing list is written
    # from this, so a contract steers it by name rather than by count.
    response="$(
      jq -nc --arg names "${FAKE_MOVED_FILES:-src/Sample.cs}" \
        '{files: ($names | split(",") | map(select(. != "") | {filename: .}))}'
    )"
    ;;
  */pulls/*/files*)
    # One changed file unless a contract asks for more, which is what lets the count ceiling on the
    # head-content loop be reached — that ceiling is a literal in the step and the only way to a
    # contract about it is a pull request wide enough to cross it.
    response="$(
      jq -nc --argjson count "${FAKE_CHANGED_FILE_COUNT:-1}" \
        '[range($count) | {filename: "backend/src/Sample\(.).cs", previous_filename: null,
                           status: "modified", additions: 1, deletions: 0,
                           patch: "@@ -1,2 +1,3 @@\n unchanged\n+added\n unchanged"}]
         | if length == 1 then [.[0] + {filename: "backend/src/Sample.cs"}] else . end'
    )"
    ;;
  */pulls/*/reviews*)
    # No previous review is the ordinary case, which is the first pass. A contract about a later one
    # names the head its previous review was given for, and that is what the comparison above runs
    # against.
    if [[ -n "${FAKE_PREVIOUS_REVIEW_HEAD:-}" ]]; then
      response="$(
        jq -nc --arg head "$FAKE_PREVIOUS_REVIEW_HEAD" \
          '[{user: {login: "fathom-reviewer[bot]"},
             state: "COMMENTED",
             commit_id: $head,
             submitted_at: "2026-08-14T09:00:00Z",
             body: "# NEEDS CHANGES"}]'
      )"
    else
      response='[]'
    fi
    ;;
  */issues/*/comments*)
    response='[]'
    ;;
  */pulls/*)
    pull_request_labels="${FAKE_PULL_REQUEST_LABELS:-}"

    if [[ -z "$pull_request_labels" ]]; then
      pull_request_labels='["security"]'
    fi

    response="$(
      jq -nc --argjson labels "$pull_request_labels" \
        '{number: 1,
          title: "Refuse an unauthenticated tool call",
          body: "Closes #11\nCloses #12\nPart of #13",
          draft: false,
          user: {login: "Krzysztof318"},
          labels: ($labels | map({name: .})),
          head: {sha: "0123456789abcdef0123456789abcdef01234567"},
          base: {sha: "89abcdef0123456789abcdef0123456789abcdef", ref: "main"},
          changed_files: 1,
          additions: 1,
          deletions: 0}'
    )"
    ;;
  */issues/*)
    # The issues the run cannot reach — deleted, or in a repository this token does not see, named
    # as a comma-separated list. The client fails rather than answering, which is the branch both
    # the collection's fallback record and the model step's default exist for.
    case ",${FAKE_UNFETCHABLE_ISSUES:-}," in
      *",${endpoint##*/},"*)
        printf 'gh: Not Found (HTTP 404)\n' >&2
        exit 1
        ;;
    esac

    labels="${FAKE_ISSUE_LABELS:-}"

    if [[ -z "$labels" ]]; then
      labels='["type:defect","security"]'
    fi

    # A comma-separated list of the issues that carry `security`, for a contract about *which* issue
    # earned a label rather than about whether one did. Everything outside it answers as ordinary
    # work, which is what lets one run distinguish a closing issue from a merely related one.
    if [[ -n "${FAKE_SECURITY_ISSUES:-}" ]]; then
      case ",${FAKE_SECURITY_ISSUES}," in
        *",${endpoint##*/},"*) labels='["type:defect","security"]' ;;
        *) labels='["type:feature"]' ;;
      esac
    fi

    response="$(
      jq -nc --argjson labels "$labels" \
        '{number: 11,
          title: "Refuse an unauthenticated tool call",
          body: "The endpoint accepts a call carrying no token.",
          labels: ($labels | map({name: .}))}'
    )"
    ;;
  *)
    printf 'The fake gh was asked for an endpoint it does not answer: %s\n' "$endpoint" >&2
    exit 1
    ;;
esac

if [[ -n "$filter" ]]; then
  printf '%s' "$response" | jq -rc "$filter"
else
  printf '%s' "$response"
fi
FAKE_GH
chmod +x "$collect_bin_directory/gh"

run_fathom_review_collect() {
  local output_file="$1"
  # How long the head-content loop may spend, from the step's own `env` block. The contracts about
  # the collection give it a window nothing reaches; the one about the window itself gives it none.
  local head_content_limit_seconds="${2:-120}"
  # The same for the loop that fetches the issues the change closes, which calls once per record for
  # the same reason and carries a window of its own.
  local closing_issue_limit_seconds="${3:-120}"
  # How many changed files the pull request carries, which decides whether the head-content loop
  # reaches its count ceiling.
  local changed_file_count="${4:-1}"
  # Whether somebody asked for this review, which decides whether the step narrows a later pass to
  # what moved since the previous one. A push is the ordinary case, and the narrowing contracts are
  # the ones that name the other.
  local explicit="${5:-false}"
  local step_script="$test_directory/fathom-review-collect.sh"
  local step_output_file="$test_directory/fathom-review-collect-step-output"

  collect_review_directory="$test_directory/fathom-review-collect-review"

  extract_fathom_review_step 'collect' "$step_script"
  rm -rf "$collect_review_directory"
  mkdir -p "$collect_review_directory"
  : > "$step_output_file"

  (
    export PATH="$collect_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export REVIEW_DIRECTORY="$collect_review_directory"
    # The step runs the real `collect-closing-issues.sh` out of the workspace, which on the runner
    # holds the base commit and here holds the checkout the suite is testing.
    export GITHUB_WORKSPACE="$source_repository_root"
    export GITHUB_OUTPUT="$step_output_file"
    export FAKE_UNFETCHABLE_ISSUES='12'
    export HEAD_CONTENT_LIMIT_SECONDS="$head_content_limit_seconds"
    export CLOSING_ISSUE_LIMIT_SECONDS="$closing_issue_limit_seconds"
    export FAKE_CHANGED_FILE_COUNT="$changed_file_count"
    export_api_retry_environment
    export REVIEWER_LOGIN='fathom-reviewer[bot]'
    export EXPLICIT="$explicit"
    bash "$step_script"
  ) > "$output_file" 2>&1
}

# The contract the security pass rests on. Without the labels beside the body, the one thing that
# says this change is to be read as a security review is invisible to the reviewer, and the review
# it produces is indistinguishable from an ordinary one.
fathom_review_collects_the_labels_of_an_issue_the_change_closes() {
  local output_file="$test_directory/fathom-review-collect-labels-output"

  run_fathom_review_collect "$output_file"

  assert_json '[11,12]' '[.[].number]' "$collect_review_directory/issues.json"
  assert_json '["type:defect","security"]' '.[0].labels' "$collect_review_directory/issues.json"
  # The label the prompt actually keys off. It is on the pull request because `Apply pull request
  # labels` put it there, and the reviewer reads it from one place rather than deriving it again.
  assert_json '["security"]' '.labels' "$collect_review_directory/pull-request.json"
}

# An issue the run could not fetch reports unknown rather than none, exactly as its title and body
# already do. An empty array would state that the issue carries no `security` label, which is the one
# thing a failed fetch cannot say — and the reviewer would read the change as ordinary on the
# strength of it.
fathom_review_reports_unknown_labels_for_an_issue_it_could_not_fetch() {
  local output_file="$test_directory/fathom-review-collect-unfetchable-output"

  run_fathom_review_collect "$output_file"

  assert_json '12' '.[1].number' "$collect_review_directory/issues.json"
  assert_json 'null' '.[1].title' "$collect_review_directory/issues.json"
  assert_json 'null' '.[1].labels' "$collect_review_directory/issues.json"
}

# The head-content loop is the one call the collection makes per path, so a retry budget spent on
# each of a hundred of them would burn the reviewing job's thirty minutes before the model starts —
# the regression this window closes. What it reports is the count of files whose content is in the
# bundle, never a prefix of the changed files: a file dropped for exceeding the byte limit is read
# and left out, so a note claiming "the first N" would name a run of files that does not exist.
fathom_review_stops_reading_head_content_when_its_window_is_gone() {
  local output_file="$test_directory/fathom-review-collect-window-output"

  run_fathom_review_collect "$output_file" 0

  assert_contains 'the content of the changed files after that point was not read' \
    "$collect_review_directory/truncation.txt"
  assert_contains '0 of them have content here' "$collect_review_directory/truncation.txt"
  [[ ! -e "$collect_review_directory/head/backend/src/Sample.cs" ]]
}

# The window is a ceiling like every other one in the step, so an ordinary collection never reaches
# it and writes nothing about it. A truncation file that always carried a line would put a sentence
# about incomplete coverage into every review body.
fathom_review_reads_head_content_within_its_window() {
  local output_file="$test_directory/fathom-review-collect-within-window-output"

  run_fathom_review_collect "$output_file"

  assert_file_content '' "$collect_review_directory/truncation.txt"
  [[ -s "$collect_review_directory/head/backend/src/Sample.cs" ]]
}

# The count ceiling reports what it cut for the same reason the window does. Without a line, a pull
# request of sixty-one to a hundred changed files leaves the later paths absent from `head/` with an
# empty `truncation.txt`, and the prompt tells the reviewer that absence means too large or binary —
# so the review says in its summary that files nobody fetched were too big to collect.
fathom_review_reports_the_head_content_files_its_count_ceiling_cut() {
  local output_file="$test_directory/fathom-review-collect-file-ceiling-output"

  run_fathom_review_collect "$output_file" 120 120 61

  assert_contains 'reached its ceiling' "$collect_review_directory/truncation.txt"
  assert_contains '60 of them have content here' "$collect_review_directory/truncation.txt"
}

# The issues the change closes are its stated contract, and the loop that fetches them calls once per
# record exactly as the head-content loop does — five references each spending a whole retry budget
# is minutes of a job whose thirty are mostly meant for the model. What the window cuts is the record
# a failed fetch already writes, because to the reviewer both mean the same thing: the number was
# referenced and what it asks for is unknown.
fathom_review_stops_reading_closing_issues_when_its_window_is_gone() {
  local output_file="$test_directory/fathom-review-collect-issue-window-output"

  run_fathom_review_collect "$output_file" 120 0

  assert_json '[11,12]' '[.[].number]' "$collect_review_directory/issues.json"
  assert_json 'null' '.[0].labels' "$collect_review_directory/issues.json"
  assert_json 'null' '.[1].labels' "$collect_review_directory/issues.json"
  assert_contains 'are here as their number alone' "$collect_review_directory/truncation.txt"
}

# What a later pass may conclude something about. Without this list every pass judges the whole
# change again, which is how #839 spent its fourth, fifth, and sixth rounds raising a first P1 on a
# page no fix in the change had touched. These three fix which passes are narrowed and which are not.
fathom_review_names_what_moved_since_its_previous_pass() {
  local output_file="$test_directory/fathom-review-collect-moved-output"

  FAKE_PREVIOUS_REVIEW_HEAD='89abcdef0123456789abcdef0123456789abcdef' \
    FAKE_MOVED_FILES='backend/src/Sample.cs,docs/operations/sample.md' \
    run_fathom_review_collect "$output_file"

  assert_contains 'backend/src/Sample.cs' "$collect_review_directory/changed-since-last-review.txt"
  assert_contains 'docs/operations/sample.md' "$collect_review_directory/changed-since-last-review.txt"
}

# The first pass has nothing to narrow against, and the file's absence is what the prompt reads as
# the whole change being in scope. Writing an empty one instead would bound the first review to
# nothing at all.
fathom_review_narrows_nothing_on_a_first_pass() {
  local output_file="$test_directory/fathom-review-collect-first-pass-output"

  run_fathom_review_collect "$output_file"

  [[ ! -e "$collect_review_directory/changed-since-last-review.txt" ]]
}

# A maintainer asking for a review wants the change looked at again rather than only the part of it
# that moved, so the narrowing is for automatic passes alone.
fathom_review_narrows_nothing_on_a_requested_pass() {
  local output_file="$test_directory/fathom-review-collect-requested-output"

  FAKE_PREVIOUS_REVIEW_HEAD='89abcdef0123456789abcdef0123456789abcdef' \
    run_fathom_review_collect "$output_file" 120 120 1 'true'

  [[ ! -e "$collect_review_directory/changed-since-last-review.txt" ]]
}

# Which model performs the review is the other half of what the `security` label decides, and the
# reviewer reads that label off the pull request rather than deriving it: `Apply pull request labels`
# owns which conditions earn which label, and a second implementation of that question could
# disagree with the label a reader sees. What these contracts pin is the reading — that the label
# reaches the costlier model, that its absence changes nothing, and that the read waits for the
# labelling run rather than racing the workflow it depends on.
run_fathom_review_model() {
  local pull_request_labels="$1"
  local pending_polls="$2"
  local limit_seconds="$3"
  local output_file="$4"
  local step_output_file="$5"
  local step_script="$test_directory/fathom-review-model.sh"
  local countdown_file="$test_directory/fathom-review-model-countdown"

  extract_fathom_review_step 'security' "$step_script"
  : > "$step_output_file"
  printf '%s' "$pending_polls" > "$countdown_file"

  (
    export PATH="$collect_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export MODEL='claude-sonnet-5'
    export SECURITY_MODEL='claude-opus-5'
    export SECURITY_LABEL='security'
    export LABELLING_WORKFLOW='apply-pull-request-rules.yml'
    # Seconds rather than minutes, for the reason the settle contracts run on short windows: what is
    # asserted is the decision the loop takes, never the values the workflow declares.
    export LABELLING_LIMIT_SECONDS="$limit_seconds"
    export LABELLING_POLL_SECONDS='1'
    export FAKE_PULL_REQUEST_LABELS="$pull_request_labels"
    export FAKE_LABELLING_COUNTDOWN="$countdown_file"
    export GITHUB_OUTPUT="$step_output_file"
    export_api_retry_environment
    bash "$step_script"
  ) > "$output_file" 2>&1
}

# The change whose defect would be a security defect is the one a costlier opinion repays, and the
# label is where the project already says which change that is.
fathom_review_reads_a_security_labelled_change_with_the_costlier_model() {
  local output_file="$test_directory/fathom-review-model-security-output"
  local step_output_file="$test_directory/fathom-review-model-security-step-output"

  run_fathom_review_model '["type:defect","security"]' '0' '10' "$output_file" "$step_output_file"

  assert_contains 'security_review=true' "$step_output_file"
  assert_contains 'model=claude-opus-5' "$step_output_file"
  assert_contains 'carries the security label' "$output_file"
}

# The other direction, and the one that keeps the escalation from becoming the default: an ordinary
# change is reviewed by the model the gate chose, at the cost the gate accounted for.
fathom_review_keeps_the_default_model_for_an_ordinary_change() {
  local output_file="$test_directory/fathom-review-model-ordinary-output"
  local step_output_file="$test_directory/fathom-review-model-ordinary-step-output"

  run_fathom_review_model '["type:feature"]' '0' '10' "$output_file" "$step_output_file"

  assert_contains 'security_review=false' "$step_output_file"
  assert_contains 'model=claude-sonnet-5' "$step_output_file"
  assert_contains 'carries no security label' "$output_file"
}

# The dependency itself. Both workflows start from the same event, so on a freshly opened pull
# request the label is being decided while this runs; a read that did not wait would review a
# security change with the default model and no pass, and would do it on exactly the changes where
# that costs most.
#
# The run this waits through is `queued`, which is the state the fake answers with and the one the
# first version of this step could not see: it asked the API for `status=in_progress`, and `queued`
# is a value of that parameter rather than a phase of it, so a labelling run that had been created
# and not yet started read as nothing to wait for.
fathom_review_waits_for_the_labelling_run_before_reading_the_labels() {
  local output_file="$test_directory/fathom-review-model-waited-output"
  local step_output_file="$test_directory/fathom-review-model-waited-step-output"
  local started_at
  started_at="$(date -u +%s)"

  run_fathom_review_model '["security"]' '2' '10' "$output_file" "$step_output_file"

  assert_contains 'model=claude-opus-5' "$step_output_file"
  assert_seconds_elapsed_at_least 2 "$started_at"
}

# The other end of the dependency: a labelling run that never finishes must not hold a review open or
# fail one, because this decides which model reviews rather than whether a review happens.
fathom_review_reads_the_labels_as_they_stand_at_the_ceiling() {
  local output_file="$test_directory/fathom-review-model-ceiling-output"
  local step_output_file="$test_directory/fathom-review-model-ceiling-step-output"

  run_fathom_review_model '["type:feature"]' '99' '2' "$output_file" "$step_output_file"

  assert_contains 'still running' "$output_file"
  assert_contains 'model=claude-sonnet-5' "$step_output_file"
}

# `Apply pull request labels` is where every condition for a label lives, so that adding one is an
# edit to a single script rather than a rule spread across whichever workflows happen to care. These
# contracts run that script, and then the step that posts what it printed.
run_select_labels() {
  local issue_labels="$1"
  local unfetchable_issues="$2"
  local output_file="$3"
  local security_issues="${4:-}"

  set +e
  (
    export PATH="$collect_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export FAKE_ISSUE_LABELS="$issue_labels"
    export FAKE_UNFETCHABLE_ISSUES="$unfetchable_issues"
    export FAKE_SECURITY_ISSUES="$security_issues"
    bash "$source_repository_root/.github/pull-request/select-labels.sh" \
      'Krzysztof318/MailFathom' '1' \
      "$source_repository_root/.github/pull-request/collect-referenced-issues.sh" '10'
    # Standard output is the label list and standard error is what the client and the ceiling had to
    # say, so the two are kept apart here exactly as the caller keeps them apart. Folding them
    # together would make an issue the run could not fetch look like a label it earned.
  ) > "$output_file" 2> "$output_file.stderr"
  select_labels_status=$?
  set -e
}

select_labels_earns_the_security_label_from_an_issue_the_change_closes() {
  local output_file="$test_directory/select-labels-security-output"

  run_select_labels '["type:defect","security"]' '' "$output_file" '11'

  ((select_labels_status == 0))
  assert_file_content 'security' "$output_file"
}

# The reason this reads references rather than closing references. `#13` is named as *part of* rather
# than closed, so the reviewer's own collection never sees it — and a change touching the work a
# security issue describes is one somebody wants read that way whether or not it finishes the issue.
select_labels_earns_the_security_label_from_an_issue_the_change_is_merely_related_to() {
  local output_file="$test_directory/select-labels-related-output"

  run_select_labels '["type:defect","security"]' '' "$output_file" '13'

  ((select_labels_status == 0))
  assert_file_content 'security' "$output_file"
}

# The condition is a label on an issue and nothing else, which is what keeps the label meaning what
# it says rather than accumulating on everything.
select_labels_earns_nothing_from_an_ordinary_change() {
  local output_file="$test_directory/select-labels-ordinary-output"

  run_select_labels '["type:feature"]' '' "$output_file"

  ((select_labels_status == 0))
  assert_file_content '' "$output_file"
}

# A label nobody could read is not a label that says `security`. The walk carries on past the issue
# it could not fetch and earns nothing from it, rather than failing the run or guessing at it.
select_labels_earns_nothing_from_an_issue_it_could_not_read() {
  local output_file="$test_directory/select-labels-unreadable-output"

  run_select_labels '["type:defect","security"]' '11,12,13' "$output_file"

  ((select_labels_status == 0))
  assert_file_content '' "$output_file"
}

run_apply_pull_request_labels() {
  local issue_labels="$1"
  local label_fails="$2"
  local output_file="$3"
  local request_file="$4"
  local step_script="$test_directory/apply-pull-request-rules.sh"

  extract_workflow_step \
    "$source_repository_root/.github/workflows/apply-pull-request-rules.yml" \
    'label' "$step_script"
  rm -f "$request_file"

  set +e
  (
    export PATH="$collect_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export SELECT_LABELS_SCRIPT="$source_repository_root/.github/pull-request/select-labels.sh"
    export REFERENCED_ISSUES_SCRIPT="$source_repository_root/.github/pull-request/collect-referenced-issues.sh"
    export REFERENCE_LIMIT='10'
    export FAKE_ISSUE_LABELS="$issue_labels"
    export FAKE_LABEL_REQUEST="$request_file"
    export FAKE_LABEL_FAILS="$label_fails"
    bash "$step_script"
  ) > "$output_file" 2>&1
  apply_labels_status=$?
  set -e
}

apply_pull_request_labels_posts_the_labels_the_change_earns() {
  local output_file="$test_directory/apply-labels-security-output"
  local request_file="$test_directory/apply-labels-security-request"

  run_apply_pull_request_labels '["type:defect","security"]' 'false' "$output_file" "$request_file"

  ((apply_labels_status == 0))
  assert_contains 'labels[]=security' "$request_file"
  assert_contains '--method POST' "$request_file"
}

# Nothing earned is nothing posted. A request carrying an empty list would be a call made to say that
# no call was needed.
apply_pull_request_labels_posts_nothing_for_an_ordinary_change() {
  local output_file="$test_directory/apply-labels-ordinary-output"
  local request_file="$test_directory/apply-labels-ordinary-request"

  run_apply_pull_request_labels '["type:feature"]' 'false' "$output_file" "$request_file"

  ((apply_labels_status == 0))
  assert_contains 'earns no label' "$output_file"
  [[ ! -e "$request_file" ]]
}

# A pull request from a fork runs this with a read-only token whatever the workflow declares, which
# is the trigger behaving as documented rather than the pipeline breaking. It says so and ends green.
apply_pull_request_labels_reports_a_write_it_was_refused() {
  local output_file="$test_directory/apply-labels-refused-output"
  local request_file="$test_directory/apply-labels-refused-request"

  run_apply_pull_request_labels '["type:defect","security"]' 'true' "$output_file" "$request_file"

  ((apply_labels_status == 0))
  assert_contains '::notice::' "$output_file"
}

# The submission step is where the reviewer's answer becomes a review, and it is the one step whose
# input is model text. What it decides — which findings can be anchored, which verdict the body
# carries, whether anything is posted at all — is decided from that text alone, so these contracts
# hand it an answer and read the payload it would have posted. The fake `gh` below stands in for the
# one call it makes: it copies the `--input` file rather than sending it, which is what lets a
# contract assert on a payload that never leaves the machine.
mkdir -p "$submit_bin_directory"
cat > "$submit_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

: "${FAKE_REVIEW_PAYLOAD:?FAKE_REVIEW_PAYLOAD must identify where the payload is recorded}"

while (($# > 0)); do
  if [[ "$1" == '--input' ]]; then
    cp "$2" "$FAKE_REVIEW_PAYLOAD"
    exit 0
  fi
  shift
done

echo 'The submission step called gh without an --input payload.' >&2
exit 1
FAKE_GH
chmod +x "$submit_bin_directory/gh"

# The collected inputs the step reads: the anchors every finding is validated against, and whatever
# a ceiling dropped. Both are written by the collection step in the real job, which these contracts
# do not run — what they exercise is everything the submission step does with them.
write_fathom_review_collection() {
  local review_directory="$1"

  mkdir -p "$review_directory"
  printf '[{"filename":"backend/src/Sample.cs","lines":[12,13,14]}]\n' > "$review_directory/lines.json"
  : > "$review_directory/truncation.txt"
}

run_fathom_review_submit() {
  local findings="$1"
  local output_file="$2"
  local payload_file="$3"
  # A reviewer that finished is the ordinary case; the contract about a run that did not names it.
  local review_outcome="${4:-success}"
  # What the coverage step found, absent by default: most reviews name every changed file, and a
  # missing file is also what this step sees when the step that writes it never ran.
  local coverage_note="${5:-}"
  # Which marker the published review carries. A push is the ordinary case, and the gate of the next
  # run counts exactly the reviews carrying this one.
  local trigger_marker="${6:-<!-- fathom-review: automatic -->}"
  # The bar the gate resolved for this pass. `full` is the first three passes and every requested
  # one, which is what most of these contracts are about; the settling contracts name the other.
  local posture="${7:-full}"
  local step_script="$test_directory/fathom-review-submit.sh"
  local review_directory="$test_directory/fathom-review-submit-review"
  local coverage_file="$test_directory/fathom-review-submit-coverage"

  submit_step_output_file="$test_directory/fathom-review-submit-step-output"

  extract_fathom_review_step 'submit' "$step_script"
  write_fathom_review_collection "$review_directory"
  rm -f "$payload_file"
  : > "$submit_step_output_file"

  if [[ -n "$coverage_note" ]]; then
    printf '%s\n' "$coverage_note" > "$coverage_file"
  else
    rm -f "$coverage_file"
  fi

  set +e
  (
    export PATH="$submit_bin_directory:$PATH"
    export GH_TOKEN='ghs_reviewerapptokenthatisnotreal'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export HEAD_SHA='0123456789abcdef0123456789abcdef01234567'
    export WORKFLOW_TOKEN='ghs_workflowtokenthatisnotreal'
    export REVIEWER_TOKEN='ghs_reviewerapptokenthatisnotreal'
    export CLAUDE_CREDENTIAL='sk-ant-oat01-credentialthatisnotreal'
    export REVIEW_OUTCOME="$review_outcome"
    export FINDINGS="$findings"
    export REVIEW_DIRECTORY="$review_directory"
    export COVERAGE_FILE="$coverage_file"
    export TRIGGER_MARKER="$trigger_marker"
    export REVIEW_POSTURE="$posture"
    export FAKE_REVIEW_PAYLOAD="$payload_file"
    # The verdict the board job reads. It is written only where a review was posted, so a contract
    # that asserts on an empty file is asserting that nothing was published.
    export GITHUB_OUTPUT="$submit_step_output_file"
    bash "$step_script"
  ) > "$output_file" 2>&1
  submit_status=$?
  set -e
}

fathom_review_anchors_a_finding_to_its_line() {
  local output_file="$test_directory/fathom-review-submit-anchored-output"
  local payload_file="$test_directory/fathom-review-submit-anchored-payload"

  run_fathom_review_submit \
    '{"summary":"Read the whole change.","findings":[{"severity":"P1","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Refuse the empty case","impact":"An empty list reaches the loop and the guard passes.","correction":"Return early when the list is empty.","rule":"`AGENTS.md`, \"Reliability, security, and performance\""}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '"COMMENT"' '.event' "$payload_file"
  assert_json '["backend/src/Sample.cs"]' '[.comments[].path]' "$payload_file"
  assert_json '[12]' '[.comments[].line]' "$payload_file"
  assert_json '["RIGHT"]' '[.comments[].side]' "$payload_file"
  assert_contains '# NEEDS CHANGES' "$payload_file"
  assert_contains '**Findings** — P1: 1' "$payload_file"
  assert_contains 'verdict=changes_requested' "$submit_step_output_file"
}

# A change settles on what it has, and a P3 never holds it. These three fix that from both sides:
# what a review carrying only deferred findings does, and what still holds a change beside one.
fathom_review_approves_a_pass_carrying_only_deferred_findings() {
  local output_file="$test_directory/fathom-review-submit-settled-output"
  local payload_file="$test_directory/fathom-review-submit-settled-payload"

  run_fathom_review_submit \
    '{"summary":"Nothing owed.","findings":[{"severity":"P3","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Name the guard for what it refuses","impact":"The name says what passes rather than what it stops.","correction":"Rename it.","rule":"`AGENTS.md`, \"Conventions & naming\""}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '"APPROVE"' '.event' "$payload_file"
  # The finding is still published, and still on its line: the verdict it arrives under changed,
  # never whether it arrives. The thread-resolution rule then keeps it answerable.
  assert_json '["backend/src/Sample.cs"]' '[.comments[].path]' "$payload_file"
  assert_contains '# APPROVED' "$payload_file"
  assert_contains '**Findings** — P3: 1' "$payload_file"
  assert_contains 'Nothing above P3 is left' "$payload_file"
  assert_contains 'verdict=approved' "$submit_step_output_file"
}

fathom_review_holds_a_pass_that_still_found_something_owed() {
  local output_file="$test_directory/fathom-review-submit-held-output"
  local payload_file="$test_directory/fathom-review-submit-held-payload"

  run_fathom_review_submit \
    '{"summary":"One thing owed.","findings":[{"severity":"P3","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Name the guard for what it refuses","impact":"The name says what passes.","correction":"Rename it.","rule":"`AGENTS.md`, \"Conventions & naming\""},{"severity":"P2","path":"backend/src/Sample.cs","start_line":null,"line":14,"title":"Bound the sequence","impact":"A remote list is expanded without a ceiling.","correction":"Take the first hundred.","rule":"`AGENTS.md`, \"Reliability, security, and performance\""}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '"COMMENT"' '.event' "$payload_file"
  assert_contains '# NEEDS CHANGES' "$payload_file"
  assert_contains 'verdict=changes_requested' "$submit_step_output_file"
}

# The first pass is the one this rule changed. A P3 on a change nobody has reviewed yet used to hold
# it for three more rounds, and the measurement that removed the threshold is in the workflow beside
# the branch: of 72 reviews that withheld approval, one carried nothing but P3 findings.
fathom_review_approves_a_first_pass_carrying_only_deferred_findings() {
  local output_file="$test_directory/fathom-review-submit-early-output"
  local payload_file="$test_directory/fathom-review-submit-early-payload"

  run_fathom_review_submit \
    '{"summary":"First pass.","findings":[{"severity":"P3","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Name the guard for what it refuses","impact":"The name says what passes.","correction":"Rename it.","rule":"`AGENTS.md`, \"Conventions & naming\""}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '"APPROVE"' '.event' "$payload_file"
  assert_json '["backend/src/Sample.cs"]' '[.comments[].path]' "$payload_file"
  assert_contains 'verdict=approved' "$submit_step_output_file"
}

# From the fourth pass the same review lands the other way round. A P2 is owed by a rule and still
# reported, but by then the author is answering threads rather than writing the change, and the
# measurement in the gate is that 27 of the 33 reviews published that late withheld approval with no
# P1 among them. So it arrives under an approval, exactly as a P3 does at any pass.
fathom_review_approves_a_settling_pass_carrying_a_rule_owed_finding() {
  local output_file="$test_directory/fathom-review-submit-settling-output"
  local payload_file="$test_directory/fathom-review-submit-settling-payload"

  run_fathom_review_submit \
    '{"summary":"Fourth pass.","findings":[{"severity":"P2","path":"backend/src/Sample.cs","start_line":null,"line":14,"title":"Bound the sequence","impact":"A remote list is expanded without a ceiling.","correction":"Take the first hundred.","rule":"`AGENTS.md`, \"Reliability, security, and performance\""}]}' \
    "$output_file" "$payload_file" 'success' '' '<!-- fathom-review: automatic -->' 'settling'

  ((submit_status == 0))
  assert_json '"APPROVE"' '.event' "$payload_file"
  # Reported, anchored, and answerable: what the posture moved is the verdict above the finding, not
  # whether the finding reaches the author.
  assert_json '["backend/src/Sample.cs"]' '[.comments[].path]' "$payload_file"
  assert_contains '# APPROVED' "$payload_file"
  assert_contains '**Findings** — P2: 1' "$payload_file"
  assert_contains 'This is a settling pass, so nothing below P1 holds the change' "$payload_file"
  assert_contains 'verdict=approved' "$submit_step_output_file"
}

fathom_review_holds_a_settling_pass_that_found_something_broken() {
  local output_file="$test_directory/fathom-review-submit-settling-held-output"
  local payload_file="$test_directory/fathom-review-submit-settling-held-payload"

  run_fathom_review_submit \
    '{"summary":"Fourth pass.","findings":[{"severity":"P1","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Refuse the empty case","impact":"An empty list reaches the loop and the guard passes.","correction":"Return early when the list is empty.","rule":"`AGENTS.md`, \"Reliability, security, and performance\""},{"severity":"P2","path":"backend/src/Sample.cs","start_line":null,"line":14,"title":"Bound the sequence","impact":"A remote list is expanded without a ceiling.","correction":"Take the first hundred.","rule":"`AGENTS.md`"}]}' \
    "$output_file" "$payload_file" 'success' '' '<!-- fathom-review: automatic -->' 'settling'

  ((submit_status == 0))
  assert_json '"COMMENT"' '.event' "$payload_file"
  assert_contains '# NEEDS CHANGES' "$payload_file"
  assert_contains 'verdict=changes_requested' "$submit_step_output_file"
}

# The posture reaches this step from the gate through two job outputs, so the value arriving empty is
# a defect in this workflow rather than an attack. It still has a safe reading, and the safe reading
# is the bar that withholds approval for more rather than for less.
fathom_review_reads_an_unset_posture_as_the_full_bar() {
  local output_file="$test_directory/fathom-review-submit-posture-unset-output"
  local payload_file="$test_directory/fathom-review-submit-posture-unset-payload"

  run_fathom_review_submit \
    '{"summary":"Unset posture.","findings":[{"severity":"P2","path":"backend/src/Sample.cs","start_line":null,"line":14,"title":"Bound the sequence","impact":"A remote list is expanded without a ceiling.","correction":"Take the first hundred.","rule":"`AGENTS.md`"}]}' \
    "$output_file" "$payload_file" 'success' '' '<!-- fathom-review: automatic -->' ''

  ((submit_status == 0))
  assert_json '"COMMENT"' '.event' "$payload_file"
  assert_contains '# NEEDS CHANGES' "$payload_file"
}

fathom_review_moves_a_finding_with_no_line_into_the_body() {
  local output_file="$test_directory/fathom-review-submit-unanchored-output"
  local payload_file="$test_directory/fathom-review-submit-unanchored-payload"

  # A line the diff does not carry and a finding that never had one: both reach the author through
  # the body rather than being dropped, which is the property the anchor validation exists for.
  run_fathom_review_submit \
    '{"summary":"Read the whole change.","findings":[{"severity":"P2","path":"backend/src/Sample.cs","start_line":null,"line":99,"title":"Name the moved line","impact":"The anchor is not on the diff.","correction":"Anchor it where the change is.","rule":"`AGENTS.md`"},{"severity":"P3","path":null,"start_line":null,"line":null,"title":"Say what the body claims","impact":"The body promises a rename the diff does not make.","correction":"Correct the body.","rule":"`docs/operations/issue-tracking.md`"}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '[]' '.comments' "$payload_file"
  assert_contains '### Findings with no line to sit on' "$payload_file"
  assert_contains '**P2 — Name the moved line** — `backend/src/Sample.cs`' "$payload_file"
  assert_contains '**P3 — Say what the body claims**' "$payload_file"
  assert_contains '**Findings** — P2: 1, P3: 1' "$payload_file"
}

fathom_review_approves_when_it_finds_nothing() {
  local output_file="$test_directory/fathom-review-submit-approved-output"
  local payload_file="$test_directory/fathom-review-submit-approved-payload"

  run_fathom_review_submit \
    '{"summary":"Read every changed file and found nothing above the bar.","findings":[]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '"APPROVE"' '.event' "$payload_file"
  assert_contains '# APPROVED' "$payload_file"
  assert_contains 'nothing above the bar' "$payload_file"
  assert_contains 'verdict=approved' "$submit_step_output_file"
}

# The marker is what the ceiling counts with, and it is written by the only step that publishes. A
# review carrying the wrong one, or none, is a pass the next gate miscounts — in either direction.
fathom_review_marks_a_review_with_what_started_it() {
  local output_file="$test_directory/fathom-review-submit-marker-output"
  local payload_file="$test_directory/fathom-review-submit-marker-payload"

  run_fathom_review_submit \
    '{"summary":"Found nothing above the bar.","covered":["backend/src/Sample.cs"],"findings":[]}' \
    "$output_file" "$payload_file" 'success' '' '<!-- fathom-review: requested -->'

  ((submit_status == 0))
  # The position, not the presence. The gate counts a review whose *last non-empty line* is the
  # automatic marker, so the submission placing it last is load-bearing and pinned nowhere else: a
  # later change appending a footer after it would leave a presence assertion green while the count
  # went to zero, the ceiling stopped refusing, and every push of a busy branch spent a review.
  assert_json '"<!-- fathom-review: requested -->"' \
    '.body | split("\n") | map(select(. != "")) | last' "$payload_file"
  assert_excludes '<!-- fathom-review: automatic -->' "$payload_file"

  run_fathom_review_submit \
    '{"summary":"Read part of it.","covered":["backend/src/Sample.cs"],"findings":[{"severity":"P1","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Refuse the empty case","impact":"An empty list reaches the loop.","correction":"Return early.","rule":"`AGENTS.md`"}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  # A review carrying findings ends with the marker too, which is the branch the ceiling actually
  # counts: the automatic passes it refuses a seventh of are the ones that found something.
  assert_json '"<!-- fathom-review: automatic -->"' \
    '.body | split("\n") | map(select(. != "")) | last' "$payload_file"
}

fathom_review_publishes_the_coverage_gap_beside_its_findings() {
  local output_file="$test_directory/fathom-review-submit-gap-output"
  local payload_file="$test_directory/fathom-review-submit-gap-payload"

  run_fathom_review_submit \
    '{"summary":"Read part of the change.","covered":["backend/src/Sample.cs"],"findings":[{"severity":"P1","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Refuse the empty case","impact":"An empty list reaches the loop.","correction":"Return early.","rule":"`AGENTS.md`"}]}' \
    "$output_file" "$payload_file" 'success' \
    'The review names 1 of the 3 changed files as read. Not named: `backend/src/Other.cs`.'

  ((submit_status == 0))
  assert_contains 'names 1 of the 3 changed files as read' "$payload_file"
}

fathom_review_publishes_the_coverage_gap_under_an_approval() {
  local output_file="$test_directory/fathom-review-submit-gap-approved-output"
  local payload_file="$test_directory/fathom-review-submit-gap-approved-payload"

  # This is the shape the gap matters most in. An approval asserts the absence of defects across the
  # whole change, so one published by a pass that read half of it says what it actually covered.
  run_fathom_review_submit \
    '{"summary":"Found nothing above the bar.","covered":["backend/src/Sample.cs"],"findings":[]}' \
    "$output_file" "$payload_file" 'success' \
    'The review names 1 of the 3 changed files as read. Not named: `backend/src/Other.cs`.'

  ((submit_status == 0))
  assert_json '"APPROVE"' '.event' "$payload_file"
  assert_contains 'names 1 of the 3 changed files as read' "$payload_file"
}

fathom_review_publishes_nothing_when_the_reviewer_returned_no_answer() {
  local output_file="$test_directory/fathom-review-submit-silent-output"
  local payload_file="$test_directory/fathom-review-submit-silent-payload"

  # The action fails its own step when the reviewer returns no structured answer, so this is what
  # the submission step sees afterwards: an empty output and a step that did not succeed. The cause
  # was reported there, so this reports a notice and posts nothing rather than failing again.
  run_fathom_review_submit '' "$output_file" "$payload_file" 'failure'

  ((submit_status == 0))
  [[ ! -e "$payload_file" ]]
  assert_contains 'no findings to submit' "$output_file"
  # No review was posted, so no verdict is stated and the board job never starts. This is the half
  # of that contract the workflow expression cannot assert on its own.
  [[ ! -s "$submit_step_output_file" ]]
}

fathom_review_fails_when_a_finished_reviewer_returned_no_answer() {
  local output_file="$test_directory/fathom-review-submit-empty-output"
  local payload_file="$test_directory/fathom-review-submit-empty-payload"

  # Unreachable while the action fails its own step on a missing structured answer, which is the
  # point: this is what stops the workflow depending on that promise. A later version that renamed
  # the output or stopped failing would otherwise leave every review posting nothing under a green
  # job, so the contract fixes the loud half here rather than trusting the action to stay as it is.
  run_fathom_review_submit '' "$output_file" "$payload_file" 'success'

  ((submit_status == 1))
  [[ ! -e "$payload_file" ]]
  assert_contains 'returned no valid findings' "$output_file"
}

fathom_review_refuses_findings_that_carry_a_credential() {
  local output_file="$test_directory/fathom-review-submit-credential-output"
  local payload_file="$test_directory/fathom-review-submit-credential-payload"

  run_fathom_review_submit \
    '{"summary":"Read the whole change.","findings":[{"severity":"P1","path":"backend/src/Sample.cs","start_line":null,"line":12,"title":"Quote the token","impact":"The token sk-ant-oat01-credentialthatisnotreal is echoed here.","correction":"Do not.","rule":"`AGENTS.md`"}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 1))
  [[ ! -e "$payload_file" ]]
  assert_contains 'shaped like a credential' "$output_file"
}

# The coverage step is the one place a review is measured against the change rather than read on its
# own terms. It gathers what every reader reported opening, compares it against the collected
# `files.json`, and writes the difference, which the submission step then publishes. What these
# contracts fix is that the difference is composed from the collection and never from a report,
# because a report is model text derived from an untrusted diff and this is a path into a published
# review body — and that a group whose reader never reported is stated as such rather than folded
# into the file count, since the two have different causes and only one of them is the model's.
#
# The reports arrive as the reader jobs leave them: one `group-<n>.json` per group that finished,
# under `candidates/` inside the collected inputs. A group that failed simply has no file, which is
# what the contracts below write by passing fewer reports than groups.
run_fathom_review_coverage() {
  local reports="$1"
  local changed_files="$2"
  local output_file="$3"
  local coverage_file="$4"
  local group_count="${5:-}"
  local groups_json="${6:-}"
  local step_script="$test_directory/fathom-review-coverage.sh"
  local review_directory="$test_directory/fathom-review-coverage-review"
  local index=0
  local report

  extract_fathom_review_step 'coverage' "$step_script"
  rm -rf "$review_directory"
  mkdir -p "$review_directory/candidates"
  printf '%s\n' "$changed_files" > "$review_directory/files.json"
  # The split this pass ran with. A caller that names none gets the first-pass shape — one group
  # holding every changed file, read this pass — which is what every contract below describes except
  # the one about a later pass bounding itself.
  if [[ -n "$groups_json" ]]; then
    printf '%s\n' "$groups_json" > "$review_directory/groups.json"
  else
    jq -c '[{index: 1, files: map(.filename), read_this_pass: true}]' \
      "$review_directory/files.json" > "$review_directory/groups.json"
  fi
  rm -f "$coverage_file"

  # Each argument is one reader's answer, and an empty one is a reader that published nothing.
  while IFS= read -r report; do
    index=$(( index + 1 ))
    [[ -n "$report" ]] || continue
    printf '%s\n' "$report" > "$review_directory/candidates/group-${index}.json"
  done <<< "$reports"

  set +e
  (
    export REVIEW_DIRECTORY="$review_directory"
    export COVERAGE_FILE="$coverage_file"
    export GROUP_COUNT="${group_count:-$index}"
    export GITHUB_OUTPUT="$test_directory/fathom-review-coverage-step-output"
    export NAME_CEILING='10'
    bash "$step_script"
  ) > "$output_file" 2>&1
  coverage_status=$?
  set -e
}

fathom_review_reports_the_files_a_review_never_named() {
  local output_file="$test_directory/fathom-review-coverage-gap-output"
  local coverage_file="$test_directory/fathom-review-coverage-gap"

  run_fathom_review_coverage \
    '{"covered":["backend/src/Sample.cs"],"candidates":[],"notes":""}' \
    '[{"filename":"backend/src/Sample.cs"},{"filename":"backend/src/Other.cs"},{"filename":"docs/features/sample.md"}]' \
    "$output_file" "$coverage_file"

  ((coverage_status == 0))
  assert_contains 'opened 1 of the 3 changed files' "$coverage_file"
  # The whole list, not each path in turn. Asserting them individually passed while the paths were
  # joined by alternating delimiters — `paste -d` reads its argument as a list of them — so what the
  # author read was ``a`,`b` `c``.
  assert_contains 'Not opened: `backend/src/Other.cs`, `docs/features/sample.md`.' "$coverage_file"
}

fathom_review_reports_no_gap_when_the_review_named_every_file() {
  local output_file="$test_directory/fathom-review-coverage-complete-output"
  local coverage_file="$test_directory/fathom-review-coverage-complete"

  # A file named twice counts once, so a ledger that repeats itself is complete rather than
  # over-complete, and the reviewer is not asked to deduplicate what `jq` can.
  run_fathom_review_coverage \
    '{"covered":["backend/src/Sample.cs","backend/src/Sample.cs"],"candidates":[],"notes":""}
{"covered":["backend/src/Other.cs"],"candidates":[],"notes":""}' \
    '[{"filename":"backend/src/Sample.cs"},{"filename":"backend/src/Other.cs"}]' \
    "$output_file" "$coverage_file"

  ((coverage_status == 0))
  [[ ! -s "$coverage_file" ]]
  assert_contains 'opened every one of the 2 changed files' "$output_file"
}

fathom_review_counts_a_named_path_the_change_does_not_contain() {
  local output_file="$test_directory/fathom-review-coverage-unknown-output"
  local coverage_file="$test_directory/fathom-review-coverage-unknown"

  # The path itself is never printed. It came from the answer rather than from the collection, and
  # what this step writes is published into a review body, so an invented name reaches the author as
  # a count of names and not as the names.
  run_fathom_review_coverage \
    '{"covered":["backend/src/Sample.cs","backend/src/Invented.cs"],"candidates":[],"notes":""}' \
    '[{"filename":"backend/src/Sample.cs"}]' \
    "$output_file" "$coverage_file"

  ((coverage_status == 0))
  assert_contains 'named 1 path(s) that the collected change does not contain' "$coverage_file"
  assert_excludes 'backend/src/Invented.cs' "$coverage_file"
}

fathom_review_bounds_how_many_unread_files_it_names() {
  local output_file="$test_directory/fathom-review-coverage-ceiling-output"
  local coverage_file="$test_directory/fathom-review-coverage-ceiling"
  local changed_files
  local index

  # Two digits, because the list is compared in the order `jq` sorts it and `File2` would otherwise
  # fall behind `File13` — which would make the contract assert on something other than the ceiling.
  changed_files='[]'
  for index in $(seq -w 1 13); do
    changed_files="$(jq -c --arg name "backend/src/File${index}.cs" '. + [{filename: $name}]' <<< "$changed_files")"
  done

  run_fathom_review_coverage \
    '{"covered":[],"candidates":[],"notes":""}' \
    "$changed_files" "$output_file" "$coverage_file"

  ((coverage_status == 0))
  assert_contains 'opened 0 of the 13 changed files' "$coverage_file"
  assert_contains 'and 3 more' "$coverage_file"
  assert_excludes 'backend/src/File11.cs' "$coverage_file"
}

fathom_review_reads_a_ledger_of_the_wrong_shape_as_an_empty_one() {
  local output_file="$test_directory/fathom-review-coverage-shape-output"
  local coverage_file="$test_directory/fathom-review-coverage-shape"

  # The schema requires an array of strings and the action refuses an answer that does not conform,
  # so this is unreachable through the action. It is fixed anyway because the step sits between a
  # model and a published review: a ledger of the wrong shape reports the change as unnamed rather
  # than turning a review that was ready to post into a red job.
  run_fathom_review_coverage \
    '{"covered":12,"candidates":[],"notes":""}' \
    '[{"filename":"backend/src/Sample.cs"}]' \
    "$output_file" "$coverage_file"

  ((coverage_status == 0))
  assert_contains 'opened 0 of the 1 changed files' "$coverage_file"
}

# A later pass re-reads only the groups that moved since the last review, so a file nobody re-read is
# one the previous review already covered rather than a hole in this one. Reporting the two the same
# way would describe every later pass as a review that walked past most of the change.
fathom_review_separates_what_moved_from_what_nobody_re_read() {
  local output_file="$test_directory/fathom-review-coverage-bounded-output"
  local coverage_file="$test_directory/fathom-review-coverage-bounded"

  run_fathom_review_coverage \
    '{"covered":["backend/src/Moved.cs"],"candidates":[],"notes":""}' \
    '[{"filename":"backend/src/Moved.cs"},{"filename":"backend/src/Still.cs"},{"filename":"docs/still.md"}]' \
    "$output_file" "$coverage_file" '1' \
    '[{"index":1,"files":["backend/src/Moved.cs"],"read_this_pass":true},{"index":2,"files":["backend/src/Still.cs","docs/still.md"],"read_this_pass":false}]'

  ((coverage_status == 0))
  # Everything in scope was read, so there is no gap line — only the sentence saying what the pass
  # bounded itself to.
  assert_excludes 'Not opened' "$coverage_file"
  assert_contains 're-read the 1 of the 3 changed files that moved since the last review' "$coverage_file"
  assert_contains 'the other 2 have not moved since' "$coverage_file"
}

# The gap that survives the bound: a file the pass *was* asked to re-read and nobody opened. It is
# named, while the files outside the bound are counted, because only the first is something the
# author can act on.
fathom_review_reports_a_gap_inside_what_the_pass_re_read() {
  local output_file="$test_directory/fathom-review-coverage-bounded-gap-output"
  local coverage_file="$test_directory/fathom-review-coverage-bounded-gap"

  run_fathom_review_coverage \
    '{"covered":["backend/src/Moved.cs"],"candidates":[],"notes":""}' \
    '[{"filename":"backend/src/Moved.cs"},{"filename":"backend/src/AlsoMoved.cs"},{"filename":"backend/src/Still.cs"}]' \
    "$output_file" "$coverage_file" '1' \
    '[{"index":1,"files":["backend/src/Moved.cs","backend/src/AlsoMoved.cs"],"read_this_pass":true},{"index":2,"files":["backend/src/Still.cs"],"read_this_pass":false}]'

  ((coverage_status == 0))
  assert_contains 'opened 1 of the 2 changed files this pass covers' "$coverage_file"
  assert_contains 'Not opened: `backend/src/AlsoMoved.cs`.' "$coverage_file"
  assert_excludes 'backend/src/Still.cs' "$coverage_file"
}

# A reader that failed published nothing, and that is a part of the change no session opened. The
# review is still submitted — the owner chose a published verdict that states the gap over a run
# that ends with nothing — so what this fixes is that the gap is said in two registers: how many
# readers came back, and which files that left unopened.
fathom_review_says_when_a_reader_never_reported() {
  local output_file="$test_directory/fathom-review-coverage-missing-reader-output"
  local coverage_file="$test_directory/fathom-review-coverage-missing-reader"

  run_fathom_review_coverage \
    '{"covered":["backend/src/Sample.cs"],"candidates":[],"notes":""}
' \
    '[{"filename":"backend/src/Sample.cs"},{"filename":"backend/src/Other.cs"}]' \
    "$output_file" "$coverage_file" '2'

  ((coverage_status == 0))
  assert_contains 'split between 2 readers and 1 reported back' "$coverage_file"
  assert_contains 'opened 1 of the 2 changed files' "$coverage_file"
}

# Every reader failed, which is the shape a subscription outage takes. The step still writes what it
# knows rather than dying: the review that follows says the whole change went unread, which is a
# verdict a reader can act on and a red job with no review is not.
fathom_review_reports_the_whole_change_when_no_reader_returned() {
  local output_file="$test_directory/fathom-review-coverage-silent-output"
  local coverage_file="$test_directory/fathom-review-coverage-silent"

  run_fathom_review_coverage \
    '' '[{"filename":"backend/src/Sample.cs"}]' "$output_file" "$coverage_file" '1'

  ((coverage_status == 0))
  assert_contains 'split between 1 readers and 0 reported back' "$coverage_file"
  assert_contains 'opened 0 of the 1 changed files' "$coverage_file"
}

# The board step turns a published verdict into the one field the owner's views group by. Its inputs
# are a verdict, a pull request body, and the item's current status, and every decision it takes is
# taken from those three: which issues it reaches, which option it writes, and which statuses it
# refuses to write over. The fake `gh` below answers the calls it makes and records the mutation
# rather than sending it, so a contract asserts on the option id the board would have received.
mkdir -p "$board_bin_directory"
cat > "$board_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

: "${FAKE_BOARD_DIRECTORY:?FAKE_BOARD_DIRECTORY must identify where the board state is recorded}"

arguments="$*"

# The mutation is matched before the item query, because the reply to one names the field the other
# reads and a looser order would answer the wrong call.
if [[ "$arguments" == *'closingIssuesReferences'* ]]; then
  cat "$FAKE_BOARD_DIRECTORY/closing-issues.json"
elif [[ "$arguments" == *'updateProjectV2ItemFieldValue'* ]]; then
  printf '%s\n' "$arguments" >> "$FAKE_BOARD_DIRECTORY/mutations.txt"
  echo '{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_item"}}}}'
elif [[ "$arguments" == *'projectItems'* ]]; then
  cat "$FAKE_BOARD_DIRECTORY/item.json"
elif [[ "$arguments" == *'ProjectV2SingleSelectField'* ]]; then
  cat "$FAKE_BOARD_DIRECTORY/field.json"
else
  echo "The board step made a call these contracts do not answer: $arguments" >&2
  exit 1
fi
FAKE_GH
chmod +x "$board_bin_directory/gh"

# The board every writing step sees: the issues GitHub says the merge will close, one field carrying
# every option the real board carries, and one item at the status the contract is about.
prepare_fathom_review_board_state() {
  local closing_issues="$1"
  local current_status="$2"

  board_directory="$test_directory/fathom-review-board-state"
  board_mutations_file="$board_directory/mutations.txt"

  rm -rf "$board_directory"
  mkdir -p "$board_directory"
  : > "$board_mutations_file"

  jq -n --arg issues "$closing_issues" \
    '{data: {repository: {pullRequest: {closingIssuesReferences: {nodes: (
       $issues
       | split(",")
       | map(select(. != ""))
       | map({number: (. | tonumber),
              repository: {nameWithOwner: "Krzysztof318/MailFathom"}})
     )}}}}}' > "$board_directory/closing-issues.json"

  cat > "$board_directory/field.json" <<'BOARD_FIELD'
{"data":{"user":{"projectV2":{"id":"PVT_board","field":{"id":"PVTSSF_status","options":[
  {"id":"option-todo","name":"Todo"},
  {"id":"option-review","name":"In review"},
  {"id":"option-changes","name":"Changes requested"},
  {"id":"option-conflicts","name":"Conflicts"},
  {"id":"option-ready","name":"Ready to merge"},
  {"id":"option-done","name":"Done"}
]}}}}}
BOARD_FIELD

  jq -n --arg status "$current_status" \
    '{data: {repository: {issue: {projectItems: {nodes: [
       {id: "PVTI_item", project: {id: "PVT_board"},
        status: (if $status == "" then null else {name: $status} end)}
     ]}}}}}' > "$board_directory/item.json"
}

# Both steps hand the same script the same environment, so what a contract varies is the step it
# extracts and the two values that step passes: which status, and which statuses it refuses.
export_fathom_review_board_environment() {
  local board_token="$1"

  export PATH="$board_bin_directory:$PATH"
  export GH_TOKEN='ghs_workflowtokenthatisnotreal'
  export BOARD_TOKEN="$board_token"
  export REPOSITORY='Krzysztof318/MailFathom'
  export PULL_REQUEST_NUMBER='1'
  export BOARD_OWNER='Krzysztof318'
  export BOARD_NUMBER='4'
  export STATUS_FIELD='Status'
  export CLOSING_ISSUES_SCRIPT="$source_repository_root/.github/pull-request/collect-closing-issues.sh"
  export CLOSING_ISSUE_LIMIT='5'
  export BOARD_STATUS_SCRIPT="$source_repository_root/.github/pull-request/write-board-status.sh"
  export FAKE_BOARD_DIRECTORY="$board_directory"
  export_api_retry_environment
}

run_fathom_review_board() {
  local verdict="$1"
  local closing_issues="$2"
  local current_status="$3"
  local output_file="$4"
  # The ordinary case is a configured board; the contract about an unconfigured one names the empty
  # token itself.
  local board_token="${5-classic-token-that-is-not-real}"
  local step_script="$test_directory/fathom-review-board.sh"

  extract_fathom_review_step 'board' "$step_script"
  prepare_fathom_review_board_state "$closing_issues" "$current_status"

  set +e
  (
    export_fathom_review_board_environment "$board_token"
    export VERDICT="$verdict"
    export APPROVED_STATUS='Ready to merge'
    export CHANGES_REQUESTED_STATUS='Changes requested'
    export PRESERVED_STATUSES='Done,Blocked'
    bash "$step_script"
  ) > "$output_file" 2>&1
  board_status=$?
  set -e
}

run_fathom_review_announcement() {
  local closing_issues="$1"
  local current_status="$2"
  local output_file="$3"
  local board_token="${4-classic-token-that-is-not-real}"
  local step_script="$test_directory/fathom-review-announce.sh"

  extract_fathom_review_step 'in-review' "$step_script"
  prepare_fathom_review_board_state "$closing_issues" "$current_status"

  set +e
  (
    export_fathom_review_board_environment "$board_token"
    export IN_REVIEW_STATUS='In review'
    export PRESERVED_STATUSES='Done,Blocked'
    bash "$step_script"
  ) > "$output_file" 2>&1
  board_status=$?
  set -e
}

fathom_review_moves_an_approved_pull_request_to_ready_to_merge() {
  local output_file="$test_directory/fathom-review-board-approved-output"

  run_fathom_review_board 'approved' '12' 'In progress' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-ready' "$board_mutations_file"
  assert_contains 'Issue 12 moved from In progress to Ready to merge' "$output_file"
}

fathom_review_records_findings_as_changes_requested() {
  local output_file="$test_directory/fathom-review-board-changes-output"

  run_fathom_review_board 'changes_requested' '12' 'In progress' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-changes' "$board_mutations_file"
  assert_contains 'to Changes requested' "$output_file"
}

# A verdict that arrives after the merge must not reopen a finished item, and `Blocked` is the one
# status a hand writes — a review says nothing about whether the issue is waiting on something
# outside the project, so it does not get to erase that statement.
fathom_review_leaves_a_finished_item_alone() {
  local output_file="$test_directory/fathom-review-board-done-output"

  run_fathom_review_board 'approved' '12' 'Done' "$output_file"

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'which this write does not overwrite' "$output_file"
}

fathom_review_leaves_a_blocked_item_alone() {
  local output_file="$test_directory/fathom-review-board-blocked-output"

  run_fathom_review_board 'changes_requested' '12' 'Blocked' "$output_file"

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'Issue 12 is Blocked' "$output_file"
}

# A pull request GitHub resolved no closing reference on moves nothing. The reviewer's own
# collection reads the same script, which is what keeps the contract it holds the change to and the
# items this moves as one list.
fathom_review_moves_nothing_for_a_pull_request_that_closes_no_issue() {
  local output_file="$test_directory/fathom-review-board-unlinked-output"

  run_fathom_review_board 'approved' '' 'Todo' "$output_file"

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'closes no issue' "$output_file"
}

# Writing a user-owned project needs a classic token with the `project` scope, which is account-wide.
# Until one is stored the job says so and ends green: the workflow gates nothing, so a missing
# credential must not turn a review red.
# The review that has started is the newest thing true of the item, so the announcement says so from
# an item that was still being written.
fathom_review_announces_a_started_review() {
  local output_file="$test_directory/fathom-review-announce-output"

  run_fathom_review_announcement '12' 'In progress' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-review' "$board_mutations_file"
  assert_contains 'Issue 12 moved from In progress to In review' "$output_file"
}

# The two statuses a review does not get to erase are the same at both ends of it. `Done` is the
# merge and the close and `Blocked` is the one status a hand writes about something outside the
# project, and a review starting is no more a statement about either than a verdict is.
fathom_review_announces_nothing_over_a_finished_or_blocked_item() {
  local output_file
  local previous_status

  for previous_status in Done Blocked; do
    output_file="$test_directory/fathom-review-announce-over-${previous_status}-output"

    run_fathom_review_announcement '12' "$previous_status" "$output_file"

    ((board_status == 0))
    [[ ! -s "$board_mutations_file" ]]
    assert_contains "Issue 12 is ${previous_status}" "$output_file"
  done
}

# Everything else is written over, including an item carrying no status at all: what the board said
# before the review started is what the review has now replaced.
fathom_review_announces_over_every_other_status() {
  local output_file
  local previous_status

  for previous_status in Todo 'Ready to merge' '' ; do
    output_file="$test_directory/fathom-review-announce-over-${previous_status:-none}-output"

    run_fathom_review_announcement '12' "$previous_status" "$output_file"

    ((board_status == 0))
    assert_contains 'option=option-review' "$board_mutations_file"
  done
}

fathom_review_writes_no_status_without_the_board_token() {
  local output_file="$test_directory/fathom-review-board-untokened-output"

  run_fathom_review_board 'approved' '12' 'In progress' "$output_file" ''

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'BOARD_PROJECT_TOKEN is not set' "$output_file"
}

# The other direction of the same question. A caller that names the statuses it may act on has said
# what it is entitled to move, and the write refuses everything else — including the statuses no
# preserved list mentions, which is what lets a rule about an approved change stay silent about every
# item that is not one.
run_board_status_write() {
  local status="$1"
  local required_statuses="$2"
  local current_status="$3"
  local output_file="$4"
  local closing_issues="${5:-12}"
  # The walk's wall-clock window, which every contract but the one about the window itself leaves at
  # the default, because what those assert is which items moved rather than how long the walk took.
  local limit_seconds="${6:-}"

  prepare_fathom_review_board_state "$closing_issues" "$current_status"

  set +e
  (
    export_fathom_review_board_environment 'classic-token-that-is-not-real'
    [[ -z "$limit_seconds" ]] || export BOARD_WRITE_LIMIT_SECONDS="$limit_seconds"
    bash "$source_repository_root/.github/pull-request/write-board-status.sh" \
      "$status" '' "$required_statuses"
  ) > "$output_file" 2>&1
  board_status=$?
  set -e
}

board_status_moves_an_item_a_rule_is_entitled_to_move() {
  local output_file="$test_directory/board-status-required-match-output"

  run_board_status_write 'Conflicts' 'Ready to merge' 'Ready to merge' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-conflicts' "$board_mutations_file"
  assert_contains 'Issue 12 moved from Ready to merge to Conflicts' "$output_file"
}

# Every other status, including the two a preserved list would have named and the item carrying no
# status at all. A conflict rule that moved those would be reporting the same conflict on every push
# to the base branch for as long as it went unresolved.
board_status_leaves_an_item_outside_the_required_statuses() {
  local output_file
  local current_status

  for current_status in 'In progress' 'Changes requested' 'Blocked' 'Done' '' ; do
    output_file="$test_directory/board-status-required-${current_status:-none}-output"

    run_board_status_write 'Conflicts' 'Ready to merge' "$current_status" "$output_file"

    ((board_status == 0))
    [[ ! -s "$board_mutations_file" ]]
    assert_contains 'so it is left where it stands' "$output_file"
  done
}

# The walk over the closing issues is the third loop that calls once per record, and it spends two
# retry budgets on each — the item read and the mutation — in two workflows that declare no
# `timeout-minutes`. What the window buys is a board write that gives up rather than one that holds a
# job open, and the issues it did not reach are named so a hand can move them.
board_status_stops_writing_when_its_window_is_gone() {
  local output_file="$test_directory/board-status-window-output"

  run_board_status_write 'Conflicts' 'Ready to merge' 'Ready to merge' "$output_file" '12,13' 0

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'issues 12, 13 were left where they stand' "$output_file"
}

# Every condition a pull request's state earns lives in one script, so a rule is an edit there rather
# than a workflow of its own. What these assert is the rule and the authority it declares with it:
# the caller passes both through unread, so a rule that forgot to bound itself would move an item
# from anywhere.
run_select_board_status() {
  local mergeable="$1"
  local output_file="$2"

  jq -n --arg mergeable "$mergeable" \
    '{number: 1, mergeable: $mergeable, isDraft: false, state: "OPEN", labels: []}' \
    > "$test_directory/select-board-status-pull-request.json"

  bash "$source_repository_root/.github/pull-request/select-board-status.sh" \
    "$test_directory/select-board-status-pull-request.json" > "$output_file" 2>&1
}

select_board_status_earns_conflicts_from_ready_to_merge_alone() {
  local output_file="$test_directory/select-board-status-conflicting"

  run_select_board_status 'CONFLICTING' "$output_file"

  assert_file_content $'Conflicts\tReady to merge\t' "$output_file"
}

# `UNKNOWN` is the answer GitHub gives while it is still computing one, which is the state every open
# pull request is in for the seconds after a merge — exactly when this pipeline runs. Reading it as a
# conflict would move an item on every merge.
select_board_status_earns_nothing_until_github_has_decided() {
  local output_file
  local mergeable

  for mergeable in MERGEABLE UNKNOWN; do
    output_file="$test_directory/select-board-status-${mergeable}"

    run_select_board_status "$mergeable" "$output_file"

    assert_file_content '' "$output_file"
  done
}

# The sweep itself, extracted from the workflow and run against a fake `gh` the way the reviewer's
# own steps are. What it exercises is the part no script holds: reading every open pull request at
# once, waiting for GitHub to decide mergeability, and passing each rule's own authority through to
# the write without inspecting it.
mkdir -p "$rules_board_bin_directory"
cat > "$rules_board_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

: "${FAKE_BOARD_DIRECTORY:?FAKE_BOARD_DIRECTORY must identify where the board state is recorded}"

filter=''
reading_filter='false'

for argument in "$@"; do
  if [[ "$reading_filter" == 'true' ]]; then
    filter="$argument"
    reading_filter='false'
    continue
  fi

  [[ "$argument" == '--jq' ]] && reading_filter='true'
done

arguments="$*"

if [[ "$arguments" == *'pullRequests(states: OPEN'* ]]; then
  # A countdown of `UNKNOWN` answers before the settled one, which is what GitHub does after a merge:
  # it computes mergeability when asked and reports `UNKNOWN` until it has. A fake that answered at
  # once would pass whether or not the step waited at all.
  remaining="$(cat "$FAKE_BOARD_DIRECTORY/mergeability-countdown" 2>/dev/null || printf '0')"

  if ((remaining > 0)); then
    printf '%s' "$((remaining - 1))" > "$FAKE_BOARD_DIRECTORY/mergeability-countdown"
    mergeable='UNKNOWN'
  else
    mergeable="$(cat "$FAKE_BOARD_DIRECTORY/mergeable")"
  fi

  response="$(
    jq -nc --arg mergeable "$mergeable" \
      --argjson total "$(cat "$FAKE_BOARD_DIRECTORY/open-pull-requests" 2>/dev/null || printf '1')" \
      '{data: {repository: {pullRequests: {totalCount: $total, nodes: [
         {number: 1, mergeable: $mergeable, isDraft: false, state: "OPEN",
          labels: {nodes: []}}]}}}}'
  )"
elif [[ "$arguments" == *'closingIssuesReferences'* ]]; then
  response="$(cat "$FAKE_BOARD_DIRECTORY/closing-issues.json")"
elif [[ "$arguments" == *'updateProjectV2ItemFieldValue'* ]]; then
  printf '%s\n' "$arguments" >> "$FAKE_BOARD_DIRECTORY/mutations.txt"
  response='{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_item"}}}}'
elif [[ "$arguments" == *'projectItems'* ]]; then
  response="$(cat "$FAKE_BOARD_DIRECTORY/item.json")"
elif [[ "$arguments" == *'ProjectV2SingleSelectField'* ]]; then
  response="$(cat "$FAKE_BOARD_DIRECTORY/field.json")"
else
  echo "The sweep made a call these contracts do not answer: $arguments" >&2
  exit 1
fi

if [[ -n "$filter" ]]; then
  printf '%s' "$response" | jq -rc "$filter"
else
  printf '%s' "$response"
fi
FAKE_GH
chmod +x "$rules_board_bin_directory/gh"

run_pull_request_rules_board() {
  local mergeable="$1"
  local current_status="$2"
  local unknown_answers="$3"
  local output_file="$4"
  local limit_seconds="${5:-5}"
  local step_script="$test_directory/pull-request-rules-board.sh"

  extract_workflow_step \
    "$source_repository_root/.github/workflows/apply-pull-request-rules.yml" \
    'board' "$step_script"

  prepare_fathom_review_board_state '12' "$current_status"
  printf '%s' "$mergeable" > "$board_directory/mergeable"
  printf '%s' "$unknown_answers" > "$board_directory/mergeability-countdown"
  printf '%s' "${open_pull_requests:-1}" > "$board_directory/open-pull-requests"

  set +e
  (
    export PATH="$rules_board_bin_directory:$PATH"
    export GH_TOKEN='ghs_workflowtokenthatisnotreal'
    export BOARD_TOKEN='classic-token-that-is-not-real'
    export REPOSITORY='Krzysztof318/MailFathom'
    export BOARD_OWNER='Krzysztof318'
    export BOARD_NUMBER='4'
    export STATUS_FIELD='Status'
    export BASE_BRANCH='main'
    export SELECT_BOARD_STATUS_SCRIPT="$source_repository_root/.github/pull-request/select-board-status.sh"
    export CLOSING_ISSUES_SCRIPT="$source_repository_root/.github/pull-request/collect-closing-issues.sh"
    export BOARD_STATUS_SCRIPT="$source_repository_root/.github/pull-request/write-board-status.sh"
    export CLOSING_ISSUE_LIMIT='5'
    export PULL_REQUEST_LIMIT='50'
    export MERGEABILITY_LIMIT_SECONDS="$limit_seconds"
    export MERGEABILITY_POLL_SECONDS='1'
    export FAKE_BOARD_DIRECTORY="$board_directory"
    export_api_retry_environment
    bash "$step_script"
  ) > "$output_file" 2>&1
  board_status=$?
  set -e
}

pull_request_rules_move_a_pull_request_that_stopped_merging() {
  local output_file="$test_directory/pull-request-rules-conflicting-output"

  run_pull_request_rules_board 'CONFLICTING' 'Ready to merge' '0' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-conflicts' "$board_mutations_file"
  assert_contains 'Issue 12 moved from Ready to merge to Conflicts' "$output_file"
}

pull_request_rules_move_nothing_for_a_pull_request_that_still_merges() {
  local output_file="$test_directory/pull-request-rules-mergeable-output"

  run_pull_request_rules_board 'MERGEABLE' 'Ready to merge' '0' "$output_file"

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
}

# The wait is the whole difference between a sweep that reports conflicts and one that reports the
# seconds after a merge. Every open pull request reads `UNKNOWN` then, and a step that acted on the
# first answer would move nothing on the merge that caused the conflict and everything on the next.
pull_request_rules_wait_for_github_to_decide_mergeability() {
  local output_file="$test_directory/pull-request-rules-waited-output"

  run_pull_request_rules_board 'CONFLICTING' 'Ready to merge' '2' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-conflicts' "$board_mutations_file"
}

# The ceiling on how many open pull requests one sweep reads reports what it cut, for the reason
# every ceiling here does: a pull request nobody was told about is a board item silently left behind.
pull_request_rules_report_the_pull_requests_the_ceiling_cut() {
  local output_file="$test_directory/pull-request-rules-ceiling-output"
  local open_pull_requests='80'

  run_pull_request_rules_board 'MERGEABLE' 'Ready to merge' '0' "$output_file"

  ((board_status == 0))
  assert_contains '80 pull requests are open against main and this run read the 1' "$output_file"
}

# What the window did not cover is named rather than guessed at, because the pull request GitHub
# never answered for is the one this run did not decide — and the next push to `main` decides it.
pull_request_rules_report_a_pull_request_github_never_decided() {
  local output_file="$test_directory/pull-request-rules-undecided-output"

  run_pull_request_rules_board 'CONFLICTING' 'Ready to merge' '99' "$output_file" 2

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'had not decided whether #1 still merges' "$output_file"
}

# The `describes:` marker is what tells a reviewer which pages a change to the code obliges, and it is
# the half of that mapping nothing derives: a page is written about configuration keys and behavior
# rather than about type names, so no name match finds the edge. The two contracts below are what
# make a declaration that has rotted loud rather than silent, which is the whole reason the marker
# lives in each page instead of in one central index.
#
# Both run against the real repository rather than a fixture. A fixture would prove the check works
# and say nothing about whether this repository's own documentation is declared, which is the
# question worth failing over.
marker_preamble_lines=15

documentation_page_requires_a_marker() {
  local page="$1"
  local name
  name="$(basename "$page")"

  # A `README`, an `AGENTS.md`, and its `CLAUDE.md` pointer describe the documentation set or the
  # rules for writing it rather than any part of the system, and the ADR templates describe nothing
  # at all until they are copied.
  case "$name" in
    README.md | AGENTS.md | CLAUDE.md) return 1 ;;
    adr-template.md | adr-short-template.md) return 1 ;;
  esac

  return 0
}

page_markers() {
  head -n "$marker_preamble_lines" "$1" | grep -oE '<!--[[:space:]]*describes:[^>]*-->' || true
}

marker_patterns() {
  sed -E 's|^<!--[[:space:]]*describes:[[:space:]]*||; s|[[:space:]]*-->$||' \
    | tr ',' '\n' \
    | sed -E 's|^[[:space:]]+||; s|[[:space:]]+$||' \
    | grep -v '^$' || true
}

every_documentation_page_declares_what_it_describes() {
  local page markers marker_count failures=0

  while IFS= read -r page; do
    documentation_page_requires_a_marker "$page" || continue

    markers="$(page_markers "$source_repository_root/$page")"
    marker_count="$(grep -c . <<< "$markers" || true)"

    if [[ "$marker_count" != '1' ]]; then
      printf '%s carries %s describes markers in its first %s lines; it needs exactly one\n' \
        "$page" "$marker_count" "$marker_preamble_lines" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- ':(glob)docs/**/*.md')

  (( failures == 0 ))
}

# The second way a declaration rots: the code it names is renamed or deleted and the page that
# describes it stays behind, pointing at nothing. Git's own glob pathspec is what resolves each
# pattern here, deliberately rather than the converter the index script carries — the two agree on
# `**` crossing a directory separator and `*` not, so a defect in either is a disagreement this test
# can see.
every_describes_pattern_matches_something_that_exists() {
  local page markers pattern failures=0

  while IFS= read -r page; do
    documentation_page_requires_a_marker "$page" || continue

    markers="$(page_markers "$source_repository_root/$page")"
    [[ -n "$markers" ]] || continue

    while IFS= read -r pattern; do
      [[ -n "$pattern" ]] || continue
      [[ "$pattern" == 'none' ]] && continue

      if [[ -z "$(git -C "$source_repository_root" ls-files -- ":(glob)$pattern")" ]]; then
        printf '%s describes %s, which matches no tracked path\n' "$page" "$pattern" >&2
        failures=$(( failures + 1 ))
      fi
    done < <(marker_patterns <<< "$markers")
  done < <(git -C "$source_repository_root" ls-files -- ':(glob)docs/**/*.md')

  (( failures == 0 ))
}

# A page whose steps happen inside somebody else's console opens with a fixed alert saying so, because
# nothing here can notice the day that console is redrawn. `docs/AGENTS.md` decides which pages take one
# — a judgement about a page's subject, which no pattern can read — and the two contracts below cover
# the half that is checkable: a page carries the notice once rather than twice, and it sits where a
# reader meets it before the first instruction rather than partway down.
#
# The wording is read out of `docs/AGENTS.md` rather than repeated here, the same arrangement the
# licensing header has with `.editorconfig`, so the sentence is one decision recorded in one place.
# That is also what makes wording drift visible: a page carrying its own variant fails rather than
# passing quietly.
third_party_notice() {
  sed -n '/^<!-- third-party-notice -->$/,/^```$/p' "$source_repository_root/docs/AGENTS.md" |
    sed -e '1,2d' -e '$d'
}

# The notice opens on `> [!WARNING]`, which any page may use for anything else, so the line a search
# keys on is the sentence below it.
third_party_notice_anchor() {
  third_party_notice | sed -n '2p'
}

third_party_notice_occurrences() {
  local anchor="$1" page="$2"

  grep --count --line-regexp --fixed-strings "$anchor" "$source_repository_root/$page" || true
}

no_documentation_page_carries_the_third_party_notice_twice() {
  local page anchor occurrences failures=0

  anchor="$(third_party_notice_anchor)"
  if [[ -z "$anchor" ]]; then
    printf 'docs/AGENTS.md carries no notice under its <!-- third-party-notice --> marker\n' >&2
    return 1
  fi

  while IFS= read -r page; do
    documentation_page_requires_a_marker "$page" || continue

    occurrences="$(third_party_notice_occurrences "$anchor" "$page")"

    if (( occurrences > 1 )); then
      printf '%s carries the third-party notice %s times; a page carries it once\n' \
        "$page" "$occurrences" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- ':(glob)docs/**/*.md')

  (( failures == 0 ))
}

every_third_party_notice_sits_directly_under_its_marker() {
  local page anchor notice notice_length carried marker_line notice_line first_content_line failures=0

  notice="$(third_party_notice)"
  anchor="$(third_party_notice_anchor)"
  if [[ -z "$anchor" ]]; then
    printf 'docs/AGENTS.md carries no notice under its <!-- third-party-notice --> marker\n' >&2
    return 1
  fi
  notice_length="$(wc -l <<< "$notice")"

  while IFS= read -r page; do
    documentation_page_requires_a_marker "$page" || continue

    notice_line="$(grep --line-number --line-regexp --fixed-strings "$anchor" \
      "$source_repository_root/$page" | head -n 1 | cut -d: -f1 || true)"
    [[ -n "$notice_line" ]] || continue

    # The anchor is the notice's second line, so the alert opens on the line above it.
    notice_line=$(( notice_line - 1 ))

    marker_line="$(grep --line-number --max-count=1 -E '<!--[[:space:]]*describes:' \
      "$source_repository_root/$page" | cut -d: -f1 || true)"
    # A page with no marker at all is the other contract's failure rather than this one's.
    [[ -n "$marker_line" ]] || continue

    first_content_line="$(awk -v marker="$marker_line" \
      'NR > marker && NF { print NR; exit }' "$source_repository_root/$page")"

    if [[ "$notice_line" != "$first_content_line" ]]; then
      printf '%s opens its third-party notice on line %s; it belongs on line %s, directly under the describes marker\n' \
        "$page" "$notice_line" "$first_content_line" >&2
      failures=$(( failures + 1 ))
      continue
    fi

    carried="$(sed -n "${notice_line},$(( notice_line + notice_length - 1 ))p" \
      "$source_repository_root/$page")"

    if [[ "$carried" != "$notice" ]]; then
      printf '%s words its third-party notice differently from the one in docs/AGENTS.md\n' "$page" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- ':(glob)docs/**/*.md')

  (( failures == 0 ))
}

# The pages under `docs/` are published as a site, and the site's navigation is written rather than
# derived: `docs/toc.yml` is the header and a `toc.yml` in each section directory is its sidebar. That
# leaves two ways for the two halves to come apart, and a reader meets each of them as an absence
# rather than as an error — a page nothing links to, or a menu entry that leads nowhere. Both run
# against the real repository, like the marker contracts above and for the same reason.
#
# Neither needs docfx. A build would find the second of these and report it as a warning among a
# hundred lines of progress; asserting it here costs nothing and fails a pull request instead.
published_documentation_pages() {
  git -C "$source_repository_root" ls-files -- ':(glob)docs/**/*.md' |
    grep -v '^docs/README\.md$' |
    grep -v '^docs/decisions/' |
    grep -v '/AGENTS\.md$' |
    grep -v '/CLAUDE\.md$' || true
}

documentation_table_of_contents_files() {
  git -C "$source_repository_root" ls-files -- ':(glob)docs/**/toc.yml'
}

# Every `href:` in a table of contents, resolved to a repository path so that an entry reaching out of
# its own section — `../operations/configuration-reference.md` from the user guide, the repository-root
# changelog from the header — compares against the same names the page list uses.
documentation_table_of_contents_targets() {
  local table href

  while IFS= read -r table; do
    while IFS= read -r href; do
      [[ -n "$href" ]] || continue

      realpath --canonicalize-missing --relative-to="$source_repository_root" \
        "$source_repository_root/$(dirname "$table")/$href"
    done < <(sed -nE 's|^[[:space:]]*-?[[:space:]]*href:[[:space:]]*||p' "$source_repository_root/$table")
  done < <(documentation_table_of_contents_files)
}

every_published_documentation_page_is_in_a_table_of_contents() {
  local page targets failures=0

  targets="$(documentation_table_of_contents_targets)"

  while IFS= read -r page; do
    # The site's landing page, and the introduction to the generated API reference. Neither is listed:
    # docfx makes the first the site root and reaches the second through the header's `api/` entry, so
    # requiring an entry for either would mean writing one that duplicates a link the template already
    # renders.
    case "$page" in
      docs/index.md | docs/api/index.md) continue ;;
    esac

    if ! grep --quiet --line-regexp --fixed-strings "$page" <<< "$targets"; then
      printf '%s is published but appears in no toc.yml, so the site carries a page nothing links to\n' \
        "$page" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(published_documentation_pages)

  (( failures == 0 ))
}

# Three documents are rendered outside the repository, and `AGENTS.md` splits the links in all of them in two: a page
# the site publishes is linked on the site, at the address that names no version, and everything else is linked in the
# repository. Each half fails in a way nobody would notice from the file — a site address that names no page is a 404
# only a reader meets, and a repository link to a published page silently sends somebody to a Markdown file in a tree
# instead of to the readable form.
documentation_site_address='https://krzysztof318.github.io/MailFathom/'
repository_blob_address='https://github.com/Krzysztof318/MailFathom/blob/main/'

# The root README is the page a reader deciding whether to adopt the project meets; `deploy/docker/README.md` is the
# Docker Hub repository overview; `deploy/helm/mailfathom/README.md` is packaged into the chart and is what Artifact Hub
# and every other chart listing renders. All three are read outside the repository, so all three carry absolute links
# and all three are read here.
#
# Only the Docker Hub one takes a length assertion, below. Artifact Hub imposes no limit on a chart's overview and
# Docker Hub's 25000 is what a release already failed on, so the assertion exists where a number does — adding one to
# the chart page would be a limit this repository invented for itself.
readonly externally_rendered_readmes=('README.md' 'deploy/docker/README.md' 'deploy/helm/mailfathom/README.md')

# The two documentation paths a README is allowed to link in the repository, because the site publishes neither.
readonly unpublished_documentation_links='^docs/README\.md$|^docs/decisions/'

every_readme_site_link_names_a_page_that_exists() {
  local readme link page failures=0

  for readme in "${externally_rendered_readmes[@]}"; do
    while IFS= read -r link; do
      page="${link#"$documentation_site_address"}"
      page="${page%.html}.md"

      # `CHANGELOG.md` is published from the repository root; every other page comes from `docs/`.
      [[ "$page" == 'CHANGELOG.md' ]] || page="docs/$page"

      if [[ ! -f "$source_repository_root/$page" ]]; then
        printf '%s links %s, which the site would generate from %s — and that file does not exist\n' \
          "$readme" "$link" "$page" >&2
        failures=$(( failures + 1 ))
      fi
    done < <(
      grep --only-matching --extended-regexp \
        "${documentation_site_address}[A-Za-z0-9_./-]*\.html" "$source_repository_root/$readme" |
        sort --unique
    )
  done

  (( failures == 0 ))
}

no_readme_link_reaches_a_published_page_through_the_repository() {
  local readme link page failures=0

  for readme in "${externally_rendered_readmes[@]}"; do
    while IFS= read -r link; do
      page="${link#"$repository_blob_address"}"

      if [[ "$page" =~ $unpublished_documentation_links ]]; then
        continue
      fi

      printf '%s links %s in the repository, and the site publishes that page\n' "$readme" "$link" >&2
      failures=$(( failures + 1 ))
    done < <(
      grep --only-matching --extended-regexp \
        "${repository_blob_address}docs/[A-Za-z0-9_./-]*\.md" "$source_repository_root/$readme" |
        sort --unique
    )
  done

  (( failures == 0 ))
}

# The release pushes `deploy/docker/README.md` to Docker Hub as the repository overview, and Docker Hub accepts 25000
# characters. The publishing workflow asserts the same number, but it asserts it after the image is already published
# to both registries — which is where the root README's growth was caught while it was serving as the overview, leaving
# a version whose image existed and whose release did not. Reading it here moves that failure onto the pull request
# that wrote the page, where shortening it costs nothing.
readonly docker_hub_overview_limit=25000

the_docker_hub_overview_fits_what_docker_hub_accepts() {
  local overview='deploy/docker/README.md' length

  length="$(wc -c < "$source_repository_root/$overview")"

  if (( length > docker_hub_overview_limit )); then
    printf '%s is %s characters and Docker Hub accepts %s as a repository overview\n' \
      "$overview" "$length" "$docker_hub_overview_limit" >&2
    return 1
  fi
}

every_table_of_contents_entry_names_a_page_that_exists() {
  local target failures=0

  while IFS= read -r target; do
    # A directory entry — `api/`, whose contents docfx generates — names no file here.
    [[ "$target" == *.md ]] || continue

    if [[ ! -f "$source_repository_root/$target" ]]; then
      printf 'A toc.yml entry points at %s, which does not exist\n' "$target" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(documentation_table_of_contents_targets)

  (( failures == 0 ))
}

# The same documentation is published a second time, as the artifacts an AI agent reads rather than browses: a map of
# every page with the line saying what it answers, each page's Markdown source beside the rendered page, and one bundle
# per reader's path through the user guide. `scripts/write-docs-agent-artifacts.sh` writes them from the tables of
# contents above, which is what makes the map a function of the navigation instead of a second index to keep in step.
#
# The two ways that can come apart are the two the tables of contents can, and a reader meets each as an absence: a
# page the map never names is documentation an agent reports as missing, and a map entry naming nothing is a fetch that
# 404s in the middle of an answer. The first two contracts assert both against this repository, where the real
# navigation is; the rest drive each refusal from a fixture, because a repository that has them right cannot show what
# happens to one that does not.
write_documentation_agent_artifacts() {
  local repository="$1" version_directory="$2" output_file="$3"

  rm -rf "$version_directory"
  mkdir -p "$version_directory"

  (
    cd "$repository"
    bash scripts/write-docs-agent-artifacts.sh "$version_directory"
  ) > "$output_file" 2>&1
}

# Every link of an llms.txt file list, which is where the map states what this version carries.
documentation_map_targets() {
  sed --quiet --regexp-extended 's/^- \[[^]]*\]\(([^)]*)\).*/\1/p' "$1"
}

the_documentation_map_lists_every_published_page() {
  local version_directory="$test_directory/docs-agent-artifacts"
  local output_file="$test_directory/docs-agent-artifacts-output"
  local page targets failures=0

  if ! write_documentation_agent_artifacts "$source_repository_root" "$version_directory" "$output_file"; then
    cat "$output_file" >&2
    return 1
  fi

  targets="$(documentation_map_targets "$version_directory/llms.txt")"

  while IFS= read -r page; do
    # The site's landing page, which the map leaves out for the reason the tables of contents leave it out: it says
    # where to start in a browser, and an agent holding the map has already arrived.
    [[ "$page" == 'docs/index.md' ]] && continue

    if ! grep --quiet --line-regexp --fixed-strings "${page#docs/}" <<< "$targets"; then
      printf '%s is published and llms.txt lists no entry for it\n' "$page" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(published_documentation_pages)

  (( failures == 0 ))
}

every_documentation_map_entry_names_a_page_the_version_carries() {
  local version_directory="$test_directory/docs-agent-artifacts-entries"
  local output_file="$test_directory/docs-agent-artifacts-entries-output"
  local target failures=0

  if ! write_documentation_agent_artifacts "$source_repository_root" "$version_directory" "$output_file"; then
    cat "$output_file" >&2
    return 1
  fi

  while IFS= read -r target; do
    if [[ ! -f "$version_directory/$target" ]]; then
      printf 'llms.txt lists %s, and the built version carries no such file\n' "$target" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(documentation_map_targets "$version_directory/llms.txt")

  (( failures == 0 ))
}

# A miniature of the real documentation: the two sections a map needs to have both shapes in it, the six pages the
# bundles name, and the changelog the header lists from outside `docs/`. Both scripts are copied in rather than reached
# through `$source_repository_root`, because the one under test resolves its own repository root and finds the other
# beside it.
create_documentation_artifacts_fixture() {
  local fixture_root="$1" page

  rm -rf "$fixture_root"
  mkdir -p "$fixture_root/docs/users" "$fixture_root/docs/features" "$fixture_root/scripts"

  printf '%s\n' \
    '- name: User guide' \
    '  href: users/' \
    '  description: The guided path.' \
    '- name: Features' \
    '  href: features/' \
    '  description: What the product does.' \
    '- name: Changelog' \
    '  href: ../CHANGELOG.md' \
    '  description: What each release shipped.' \
    > "$fixture_root/docs/toc.yml"

  : > "$fixture_root/docs/users/toc.yml"

  for page in installation getting-started mailbox-providers administering mcp-clients usage; do
    printf '%s\n' \
      "- name: The $page page" \
      "  href: $page.md" \
      "  description: What the $page page answers." \
      >> "$fixture_root/docs/users/toc.yml"

    printf '# The %s page\n' "$page" > "$fixture_root/docs/users/$page.md"
  done

  printf 'It links [a widget](../features/widgets.md) and [a sibling](installation.md).\n' \
    >> "$fixture_root/docs/users/usage.md"

  printf '%s\n' \
    '- name: Mailbox widgets' \
    '  href: widgets.md' \
    '  description: What a widget answers.' \
    > "$fixture_root/docs/features/toc.yml"

  printf '# Mailbox widgets\n' > "$fixture_root/docs/features/widgets.md"
  printf '# Changelog\n' > "$fixture_root/CHANGELOG.md"

  cp "$source_repository_root/scripts/write-docs-agent-artifacts.sh" \
    "$source_repository_root/scripts/rebase-markdown-links.sh" "$fixture_root/scripts/"
  chmod +x "$fixture_root/scripts/write-docs-agent-artifacts.sh" \
    "$fixture_root/scripts/rebase-markdown-links.sh"

  git -C "$fixture_root" init --initial-branch=main --quiet
  git -C "$fixture_root" config user.email agent-workflow@example.invalid
  git -C "$fixture_root" config user.name 'Agent Workflow Tests'
  git -C "$fixture_root" add .
  git -C "$fixture_root" commit --quiet -m 'base'
}

the_documentation_artifacts_refuse_a_published_page_the_map_would_miss() {
  local fixture_root="$test_directory/docs-artifacts-unmapped"
  local version_directory="$test_directory/docs-artifacts-unmapped-version"
  local output_file="$test_directory/docs-artifacts-unmapped-output"

  create_documentation_artifacts_fixture "$fixture_root"

  printf '# Mailbox gadgets\n' > "$fixture_root/docs/features/gadgets.md"
  git -C "$fixture_root" add docs/features/gadgets.md

  if write_documentation_agent_artifacts "$fixture_root" "$version_directory" "$output_file"; then
    printf 'The artifacts were written for a page no table of contents lists\n' >&2
    return 1
  fi

  assert_contains 'docs/features/gadgets.md is published and llms.txt lists no entry for it' "$output_file"
}

the_documentation_artifacts_refuse_a_map_entry_naming_no_page() {
  local fixture_root="$test_directory/docs-artifacts-dangling"
  local version_directory="$test_directory/docs-artifacts-dangling-version"
  local output_file="$test_directory/docs-artifacts-dangling-output"

  create_documentation_artifacts_fixture "$fixture_root"

  printf '%s\n' \
    '- name: Mailbox gadgets' \
    '  href: gadgets.md' \
    '  description: What a gadget answers.' \
    >> "$fixture_root/docs/features/toc.yml"

  if write_documentation_agent_artifacts "$fixture_root" "$version_directory" "$output_file"; then
    printf 'The artifacts were written with a map entry naming no page\n' >&2
    return 1
  fi

  assert_contains 'llms.txt lists features/gadgets.md, and this version carries no such page' "$output_file"
}

# The line beside a link is what the map is for: without it an agent reads a list of titles it has to fetch one by one,
# which is the search over fragments the map exists to replace.
the_documentation_artifacts_refuse_a_page_with_no_description() {
  local fixture_root="$test_directory/docs-artifacts-undescribed"
  local version_directory="$test_directory/docs-artifacts-undescribed-version"
  local output_file="$test_directory/docs-artifacts-undescribed-output"

  create_documentation_artifacts_fixture "$fixture_root"

  printf '%s\n' \
    '- name: Mailbox widgets' \
    '  href: widgets.md' \
    > "$fixture_root/docs/features/toc.yml"

  if write_documentation_agent_artifacts "$fixture_root" "$version_directory" "$output_file"; then
    printf 'The artifacts were written for a page whose table of contents says nothing about it\n' >&2
    return 1
  fi

  assert_contains 'docs/features/toc.yml lists Mailbox widgets with no description' "$output_file"
}

# A bundle is read from the version's root rather than from the section its pages came from, so every relative link in
# it is resolved for that move. Left alone, a link out of the section would climb above the site and one inside it
# would name a page that is not where the reader is.
a_documentation_bundle_resolves_a_link_out_of_its_own_section() {
  local fixture_root="$test_directory/docs-artifacts-bundle"
  local version_directory="$test_directory/docs-artifacts-bundle-version"
  local output_file="$test_directory/docs-artifacts-bundle-output"

  create_documentation_artifacts_fixture "$fixture_root"

  if ! write_documentation_agent_artifacts "$fixture_root" "$version_directory" "$output_file"; then
    cat "$output_file" >&2
    return 1
  fi

  assert_contains '[a widget](features/widgets.md)' "$version_directory/llms-mailbox-user.txt"
  assert_contains '[a sibling](users/installation.md)' "$version_directory/llms-mailbox-user.txt"
}

# The index itself. It calls no API, so unlike the gate and the settle loop it needs no `gh` stub and
# no extraction from the workflow: the fixture is a tree on disk and a `files.json` beside it.
create_obligation_fixture() {
  local fixture_root="$1"

  rm -rf "$fixture_root"
  mkdir -p \
    "$fixture_root/backend/src/Application/Emails" \
    "$fixture_root/backend/src/Domain/Failures" \
    "$fixture_root/backend/tests/Application.UnitTests" \
    "$fixture_root/docs/features"

  printf 'internal sealed class MailboxWidget;\n' > "$fixture_root/backend/src/Application/Emails/MailboxWidget.cs"
  printf 'internal sealed class MailboxGadget;\n' > "$fixture_root/backend/src/Application/Emails/MailboxGadget.cs"
  printf 'public void Reads() => new MailboxGadget();\n' \
    > "$fixture_root/backend/tests/Application.UnitTests/MailboxGadgetTests.cs"

  printf '%s\n' \
    '# Mailbox widgets' \
    '' \
    '<!-- describes: backend/src/Application/Emails/** -->' \
    '' \
    'What a widget answers.' \
    > "$fixture_root/docs/features/widgets.md"
}

run_obligation_index() {
  local fixture_root="$1" files_json="$2" output_file="$3"

  bash "$source_repository_root/.github/fathom-review/index-obligations.sh" \
    "$fixture_root" "$files_json" "$output_file" > /dev/null
}

obligation_index_reports_a_changed_source_no_test_reaches() {
  local fixture_root="$test_directory/obligations-missing-test"
  local files_json="$test_directory/obligations-missing-test-files.json"
  local output_file="$test_directory/obligations-missing-test.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"backend/src/Application/Emails/MailboxWidget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[]' '.tests[0].referencing_tests' "$output_file"
  assert_json '"backend/tests/Application.UnitTests"' '.tests[0].expected_test_project' "$output_file"
}

# The case where reporting a missing test would be most obviously wrong: the change adds the class
# and its test together. The added test is not in the base tree, so only the diff can show it, and an
# index that read the tree alone would report a gap the author had already closed.
obligation_index_credits_a_test_the_change_adds() {
  local fixture_root="$test_directory/obligations-added-test"
  local files_json="$test_directory/obligations-added-test-files.json"
  local output_file="$test_directory/obligations-added-test.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"backend/src/Application/Emails/MailboxWidget.cs","status":"added","patch":"@@ -0,0 +1 @@\n+internal sealed class MailboxWidget;"},{"filename":"backend/tests/Application.UnitTests/MailboxWidgetTests.cs","status":"added","patch":"@@ -0,0 +1 @@\n+public void Reads() => new MailboxWidget();"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[{"path":"backend/tests/Application.UnitTests/MailboxWidgetTests.cs","changed_by_this_pull_request":true}]' \
    '.tests[0].referencing_tests' "$output_file"
}

# A test that exists and was left alone is the interesting middle case: the index has to name it
# rather than stay silent, because a reviewer decides from the file whether the behavior that moved
# is one it reaches.
obligation_index_names_a_test_the_change_left_alone() {
  local fixture_root="$test_directory/obligations-untouched-test"
  local files_json="$test_directory/obligations-untouched-test-files.json"
  local output_file="$test_directory/obligations-untouched-test.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"backend/src/Application/Emails/MailboxGadget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[{"path":"backend/tests/Application.UnitTests/MailboxGadgetTests.cs","changed_by_this_pull_request":false}]' \
    '.tests[0].referencing_tests' "$output_file"
}

obligation_index_maps_a_changed_path_to_the_page_that_describes_it() {
  local fixture_root="$test_directory/obligations-documentation"
  local files_json="$test_directory/obligations-documentation-files.json"
  local output_file="$test_directory/obligations-documentation.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"backend/src/Application/Emails/MailboxWidget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[{"path":"docs/features/widgets.md","changed_by_this_pull_request":false}]' \
    '.documentation[0].describing_documents' "$output_file"
}

# `**` between two slashes matches zero directories as well as many — git documents `a/**/b` as
# matching `a/b`, and `every_describes_pattern_matches_something_that_exists` resolves every marker
# through git's own pathspec. A converter that required a directory there would leave the contract
# suite calling a pattern valid while the index silently skipped the paths it covers, which is the
# one disagreement between the two that nothing else would catch.
obligation_index_credits_a_path_directly_under_a_double_star() {
  local fixture_root="$test_directory/obligations-zero-directories"
  local files_json="$test_directory/obligations-zero-directories-files.json"
  local output_file="$test_directory/obligations-zero-directories.json"

  create_obligation_fixture "$fixture_root"

  printf '%s\n' \
    '# Configuration reference' \
    '' \
    '<!-- describes: backend/src/**/*Options.cs, **/*.slnx -->' \
    '' \
    'Every user-settable option.' \
    > "$fixture_root/docs/features/configuration.md"

  # One path directly under the boundary, one nested, and one at the repository root, which is the
  # case a leading `**/` covers.
  printf '%s\n' '[{"filename":"backend/src/MailboxOptions.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"},{"filename":"backend/src/Application/Emails/TimelineOptions.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"},{"filename":"backend/MailFathom.slnx","status":"modified","patch":"@@ -1 +1,2 @@\n+<Solution />"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '"docs/features/configuration.md"' \
    '[.documentation[] | select(.path == "backend/src/MailboxOptions.cs")][0].describing_documents[0].path' \
    "$output_file"
  assert_json '"docs/features/configuration.md"' \
    '[.documentation[] | select(.path == "backend/src/Application/Emails/TimelineOptions.cs")][0].describing_documents[0].path' \
    "$output_file"
  assert_json '"docs/features/configuration.md"' \
    '[.documentation[] | select(.path == "backend/MailFathom.slnx")][0].describing_documents[0].path' \
    "$output_file"
}

# A page that documents the convention writes a marker out as an example — `docs/AGENTS.md` does —
# and reading the whole file would turn that example into a declaration, so every path it names would
# acquire a page that says nothing about it. Only the preamble counts, and this is the case that says
# so.
obligation_index_ignores_a_marker_below_the_preamble() {
  local fixture_root="$test_directory/obligations-marker-example"
  local files_json="$test_directory/obligations-marker-example-files.json"
  local output_file="$test_directory/obligations-marker-example.json"

  create_obligation_fixture "$fixture_root"
  {
    printf '# How to declare what a page describes\n\n'
    printf 'Filler that pushes the example past the preamble.\n\n%.0s' {1..12}
    printf '<!-- describes: backend/src/Domain/Failures/** -->\n'
  } > "$fixture_root/docs/conventions.md"
  printf '%s\n' '[{"filename":"backend/src/Domain/Failures/MailboxFailure.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '0' '.documentation | length' "$output_file"
}

obligation_index_reports_a_moved_pin_with_no_register_row() {
  local fixture_root="$test_directory/obligations-register"
  local files_json="$test_directory/obligations-register-files.json"
  local output_file="$test_directory/obligations-register.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"backend/Directory.Packages.props","status":"modified","patch":"@@ -1 +1,2 @@\n+<PackageVersion Include=\"Something\" Version=\"1.0.0\" />"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '"THIRD_PARTY_LICENSES.md"' '.registers[0].register' "$output_file"
  assert_json 'false' '.registers[0].register_changed' "$output_file"
}

# The obligation is discharged, so the pair says so rather than disappearing: a reviewer reads
# `register_changed` and checks the row, instead of inferring from an empty section that no pin moved.
obligation_index_records_a_register_the_change_updated() {
  local fixture_root="$test_directory/obligations-register-met"
  local files_json="$test_directory/obligations-register-met-files.json"
  local output_file="$test_directory/obligations-register-met.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"backend/Directory.Packages.props","status":"modified","patch":"@@ -1 +1,2 @@\n+<PackageVersion Include=\"Something\" Version=\"1.0.0\" />"},{"filename":"THIRD_PARTY_LICENSES.md","status":"modified","patch":"@@ -1 +1,2 @@\n+| Something | 1.0.0 | MIT |"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json 'true' '.registers[0].register_changed' "$output_file"
}

# How many tests name a type is a property of how common the name is rather than of the change, so
# the list is capped per entry as well as per section. The count survives the cut, because a reviewer
# reading twenty entries needs to know whether that was all of them.
obligation_index_caps_the_tests_it_lists_for_one_type() {
  local fixture_root="$test_directory/obligations-common-name"
  local files_json="$test_directory/obligations-common-name-files.json"
  local output_file="$test_directory/obligations-common-name.json"
  local index

  create_obligation_fixture "$fixture_root"

  for index in $(seq 1 25); do
    printf 'public void Case%s() => new MailboxWidget();\n' "$index" \
      > "$fixture_root/backend/tests/Application.UnitTests/MailboxWidgetCase${index}Tests.cs"
  done

  printf '%s\n' '[{"filename":"backend/src/Application/Emails/MailboxWidget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '25' '.tests[0].referencing_test_count' "$output_file"
  assert_json '20' '.tests[0].referencing_tests | length' "$output_file"
  assert_json '1' '.notes | length' "$output_file"
}

# Every read `Fathom review` makes goes through one script, so what a dropped connection costs is one
# decision rather than fifteen. That workflow lost a whole run to a single reply that never arrived,
# and the shape of that failure is what these contracts pin: a call that works is not asked twice, a
# call that fails once still returns its answer, a call the API answered is not retried at all, and a
# call that never succeeds fails after exactly the budgeted attempts rather than quietly.
api_retry_bin_directory="$test_directory/api-retry-bin"
mkdir -p "$api_retry_bin_directory"

# A `gh` that fails a stated number of leading attempts and then answers. It writes a line per
# attempt, which is what lets a contract assert how many times the call was actually made rather than
# only what came back.
#
# The failing attempt writes to standard output before it fails, standing in for `--paginate` having
# already streamed a page when a later one drops. That is what the buffering in the script under test
# is for, so it is provoked here rather than assumed.
cat > "$api_retry_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

: "${FAKE_API_ATTEMPT_LOG:?FAKE_API_ATTEMPT_LOG must name where the attempts are recorded}"

printf '%s\n' "$*" >> "$FAKE_API_ATTEMPT_LOG"

# A connection that stalls rather than drops, which is the failure `gh` has no deadline of its own
# for: it answers nothing and never returns, so only the wrapper's own kill ends the attempt.
if [[ -n "${FAKE_API_STALL_SECONDS:-}" ]]; then
  sleep "$FAKE_API_STALL_SECONDS"
fi

if (( "$(wc -l < "$FAKE_API_ATTEMPT_LOG")" <= "${FAKE_API_FAILURES:-0}" )); then
  printf 'partial\n'
  printf '%s\n' "${FAKE_API_ERROR:-invalid character 'u' looking for beginning of value}" >&2
  exit 1
fi

printf 'answered\n'
FAKE_GH
chmod +x "$api_retry_bin_directory/gh"

api_call_status=0

run_github_api_call() {
  local failures="$1"
  local attempt_limit="$2"
  local output_file="$3"
  local error_file="$4"
  # The error text `gh` writes, which is what tells a reply the API produced from one that never
  # arrived. The default is the message the lost run actually failed on.
  local gh_error="${5:-}"
  # How long each attempt stalls before answering, and how long the wrapper lets one run. Both are
  # empty for every contract but the one about the deadline, where the stall is the failure.
  local stall_seconds="${6:-}"
  local timeout_seconds="${7:-30}"
  # The backoff base. Every contract but the one about the wait itself zeroes it, because what those
  # assert is how many attempts were made rather than how long the script waited between them.
  local retry_delay_seconds="${8:-0}"

  : > "$test_directory/api-retry-attempts.log"

  set +e
  (
    export PATH="$api_retry_bin_directory:$PATH"
    export FAKE_API_ATTEMPT_LOG="$test_directory/api-retry-attempts.log"
    export FAKE_API_FAILURES="$failures"
    export FAKE_API_ERROR="$gh_error"
    export FAKE_API_STALL_SECONDS="$stall_seconds"
    export API_ATTEMPT_LIMIT="$attempt_limit"
    export API_TIMEOUT_SECONDS="$timeout_seconds"
    export API_RETRY_DELAY_SECONDS="$retry_delay_seconds"

    bash "$source_repository_root/.github/pull-request/call-github-api.sh" \
      'repos/Krzysztof318/MailFathom/pulls/1' --jq '.head.sha'
  ) > "$output_file" 2> "$error_file"
  api_call_status=$?
  set -e
}

assert_api_attempts() {
  local expected_attempts="$1"
  local actual_attempts

  actual_attempts="$(wc -l < "$test_directory/api-retry-attempts.log")"

  if (( actual_attempts != expected_attempts )); then
    printf 'Expected %s attempts, but the call was made %s times\n' \
      "$expected_attempts" "$actual_attempts" >&2
    return 1
  fi
}

github_api_call_asks_once_when_the_call_succeeds() {
  local output_file="$test_directory/api-retry-first-time"
  local error_file="$test_directory/api-retry-first-time-error"

  run_github_api_call 0 4 "$output_file" "$error_file"

  (( api_call_status == 0 ))
  assert_file_content 'answered' "$output_file"
  assert_api_attempts 1
}

# The whole point of the change: the run that was lost failed on its first call and recovered on
# nobody. What comes back is the successful answer alone — the page the failed attempt had already
# written is dropped, because a caller reading one record per line would otherwise be handed the
# first page twice.
github_api_call_returns_the_answer_after_a_dropped_connection() {
  local output_file="$test_directory/api-retry-recovered"
  local error_file="$test_directory/api-retry-recovered-error"

  run_github_api_call 1 4 "$output_file" "$error_file"

  (( api_call_status == 0 ))
  assert_file_content 'answered' "$output_file"
  assert_excludes 'partial' "$output_file"
  assert_api_attempts 2
}

# A reply carrying a client error is the API answering, and asking again produces the same answer
# more slowly. The head-content loop depends on this: it fetches sixty paths and reads a missing one
# as an ordinary outcome, so a budget spent on each would cost minutes of a run that is already
# bounded.
github_api_call_does_not_retry_an_answer_the_api_produced() {
  local output_file="$test_directory/api-retry-client-error"
  local error_file="$test_directory/api-retry-client-error-message"

  run_github_api_call 9 4 "$output_file" "$error_file" 'gh: Not Found (HTTP 404)'

  (( api_call_status != 0 ))
  assert_api_attempts 1
  assert_contains 'Attempts made: 1 of 4' "$error_file"
}

# The other side of the rule above, and the arm no other contract reaches: `408`, `429`, and every
# `5xx` are statuses that say *ask again*. Without this, deleting that arm or mistyping its glob
# would send a `502` to the `*)` branch and fail it on the first attempt — losing a run to the exact
# class of failure the helper was written for, while every other contract stayed green.
github_api_call_retries_a_status_that_says_ask_again() {
  local output_file="$test_directory/api-retry-server-error"
  local error_file="$test_directory/api-retry-server-error-message"

  run_github_api_call 9 3 "$output_file" "$error_file" 'gh: Server Error (HTTP 502)'

  (( api_call_status != 0 ))
  assert_api_attempts 3
  assert_contains 'Attempts made: 3 of 3' "$error_file"
}

# A connection that stalls rather than drops is the same proxy failure in its other shape, and `gh`
# sets no deadline for it: without the wrapper's own kill the attempt never returns, the budget never
# advances, and a bound that cannot advance is not a bound — the collection would sit there until the
# reviewing job's thirty minutes ran out, which is the outcome the retries exist to remove.
github_api_call_kills_an_attempt_that_stalls() {
  local output_file="$test_directory/api-retry-stalled"
  local error_file="$test_directory/api-retry-stalled-error"

  run_github_api_call 0 2 "$output_file" "$error_file" '' 5 1

  (( api_call_status != 0 ))
  assert_api_attempts 2
  assert_contains 'Attempts made: 2 of 2' "$error_file"
}

# The wait between attempts, which every other contract zeroes so that it can assert a count. Nothing
# would then observe that the helper waits at all: deleting the `sleep`, or the doubling, leaves the
# four requests arriving back-to-back at the proxy that motivated the retries. The jitter is not one
# of the two — removing it leaves exactly 1 + 2 + 4, which still clears the floor below — so nothing
# here holds the property that several calls failing at once do not come back in step.
#
# The budget is the full four rather than the three the other contracts use, because the jitter is
# what decides how long a shorter run takes. With a base of one second the three waits are 1-2s,
# 2-3s and 4-5s, so the whole call takes 7 to 10 — while the same call with the doubling removed
# takes 3 to 6, whatever the jitter rolls. Seven seconds is therefore the floor that separates them,
# and no smaller budget separates them at all.
github_api_call_waits_longer_between_each_attempt() {
  local output_file="$test_directory/api-retry-backoff"
  local error_file="$test_directory/api-retry-backoff-error"
  local started_at
  started_at="$(date -u +%s)"

  run_github_api_call 9 4 "$output_file" "$error_file" '' '' 30 1

  (( api_call_status != 0 ))
  assert_api_attempts 4
  assert_seconds_elapsed_at_least 7 "$started_at"
}

# A retry that exhausts its budget still fails the job. Nothing here turns an unreachable API into a
# review that silently covered less, and the count is reported because the caller's own failure says
# only that the call did not succeed.
github_api_call_fails_after_the_budgeted_attempts() {
  local output_file="$test_directory/api-retry-exhausted"
  local error_file="$test_directory/api-retry-exhausted-error"

  run_github_api_call 9 3 "$output_file" "$error_file"

  (( api_call_status != 0 ))
  assert_api_attempts 3
  assert_contains 'Attempts made: 3 of 3' "$error_file"
}

# Which issues a pull request closes is its stated contract, and merging closes every one of them.
# The answer comes from GitHub rather than from a reading of the body, so what is pinned here is what
# the script does with that answer: which of the returned issues it acts on, and what it says about
# the ones a ceiling cut.
closing_issues_bin_directory="$test_directory/closing-issues-bin"
mkdir -p "$closing_issues_bin_directory"

cat > "$closing_issues_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

: "${FAKE_CLOSING_ISSUES_FILE:?FAKE_CLOSING_ISSUES_FILE must name the answer to return}"

# A stated number of leading attempts drop the connection before answering, which is what lets a
# contract watch what the script does with a call the helper recovered rather than only with one
# that worked first time.
if [[ -n "${FAKE_CLOSING_ISSUES_ATTEMPT_LOG:-}" ]]; then
  printf 'attempt\n' >> "$FAKE_CLOSING_ISSUES_ATTEMPT_LOG"

  if (( "$(wc -l < "$FAKE_CLOSING_ISSUES_ATTEMPT_LOG")" <= "${FAKE_CLOSING_ISSUES_FAILURES:-0}" )); then
    printf "invalid character 'u' looking for beginning of value\n" >&2
    exit 1
  fi
fi

cat "$FAKE_CLOSING_ISSUES_FILE"
FAKE_GH
chmod +x "$closing_issues_bin_directory/gh"

# The nodes GitHub returns, written as the query shapes them. A repository is stated per node because
# a closing reference can name an issue in another project, which this script drops and the rest of
# the pipeline must never be handed.
prepare_closing_issues_answer() {
  local nodes="$1"

  jq -n --argjson nodes "$nodes" \
    '{data: {repository: {pullRequest: {closingIssuesReferences: {nodes: $nodes}}}}}' \
    > "$test_directory/closing-issues-answer.json"
}

run_closing_issues() {
  local nodes="$1" output_file="$2" note_file="${3:-/dev/null}" limit="${4:-0}" failures="${5:-0}"

  prepare_closing_issues_answer "$nodes"
  : > "$test_directory/closing-issues-attempts.log"

  (
    export PATH="$closing_issues_bin_directory:$PATH"
    export FAKE_CLOSING_ISSUES_FILE="$test_directory/closing-issues-answer.json"
    export FAKE_CLOSING_ISSUES_ATTEMPT_LOG="$test_directory/closing-issues-attempts.log"
    export FAKE_CLOSING_ISSUES_FAILURES="$failures"
    export_api_retry_environment

    bash "$source_repository_root/.github/pull-request/collect-closing-issues.sh" \
      'Krzysztof318/MailFathom' '1' "$limit"
  ) > "$output_file" 2> "$note_file"
}

# The superset the labelling pipeline reads, and the reason the two scripts stand side by side: a
# label answers what the change is *about*, so a mention counts, while a closing reference answers
# what merging completes and a mention does not. A change that says "part of #123" against a
# security issue is one somebody wants read that way whether or not it finishes the issue.
run_referenced_issues() {
  local body="$1" output_file="$2" limit="${3:-0}"

  printf '%s\n' "$body" > "$test_directory/referenced-body.md"

  bash "$source_repository_root/.github/pull-request/collect-referenced-issues.sh" \
    "$test_directory/referenced-body.md" 'Krzysztof318/MailFathom' "$limit" > "$output_file" 2>&1
}

referenced_issues_collect_a_mention_as_well_as_a_closing_reference() {
  local output_file="$test_directory/referenced-issues-all"

  run_referenced_issues \
    $'Closes #265\n\nPart of #266, and the ceiling #270 asked for.' \
    "$output_file"

  assert_file_content $'265\n266\n270' "$output_file"
}

referenced_issues_collect_a_link_to_an_issue_in_this_repository() {
  local output_file="$test_directory/referenced-issues-url"

  run_referenced_issues \
    'Related to https://github.com/Krzysztof318/MailFathom/issues/271.' \
    "$output_file"

  assert_file_content '271' "$output_file"
}

# The number in `owner/repo#123` belongs to another project's namespace, so reading the `#123` out of
# it would earn a label from whichever local issue happens to hold that number — an issue nobody
# named. The same holds for a link into another repository.
referenced_issues_ignore_another_repository() {
  local output_file="$test_directory/referenced-issues-foreign"

  run_referenced_issues \
    $'Mirrors Krzysztof318/Other#98.\nSee https://github.com/Krzysztof318/Other/issues/99 too.' \
    "$output_file"

  assert_file_content '' "$output_file"
}

referenced_issues_report_each_issue_once() {
  local output_file="$test_directory/referenced-issues-repeated"

  run_referenced_issues $'Closes #265\n\nAnd #265 again, see also #265.' "$output_file"

  assert_file_content '265' "$output_file"
}

# One issue is fetched per line printed, and a body is untrusted text. What the ceiling cut is said
# rather than dropped, because a label the change did not earn only because a reference fell off the
# end is a decision somebody has to be able to see.
referenced_issues_report_what_the_ceiling_cut() {
  local output_file="$test_directory/referenced-issues-ceiling"
  local note_file="$test_directory/referenced-issues-ceiling-note"

  printf '%s\n' 'Closes #1, part of #2, and see #3' > "$test_directory/referenced-body.md"

  # The two streams are kept apart here for the reason the caller keeps them apart: the list is what
  # decides the labels, and the note is what a reader is told about the part that did not fit.
  bash "$source_repository_root/.github/pull-request/collect-referenced-issues.sh" \
    "$test_directory/referenced-body.md" 'Krzysztof318/MailFathom' 2 \
    > "$output_file" 2> "$note_file"

  assert_file_content $'1\n2' "$output_file"
  assert_contains 'refers to 3 issues and this covers the first 2' "$note_file"
}

closing_issues_collect_every_issue_the_merge_will_close() {
  local output_file="$test_directory/closing-issues-all"

  run_closing_issues \
    '[{"number": 265, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 266, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 270, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}}]' \
    "$output_file"

  assert_file_content $'265\n266\n270' "$output_file"
}

# A pull request that closes nothing is the ordinary shape of a release's changelog half, and every
# caller reads the empty stream as *nothing to act on*. A single blank line would be one issue number
# to each of them.
closing_issues_print_nothing_when_the_merge_closes_nothing() {
  local output_file="$test_directory/closing-issues-none"

  run_closing_issues '[]' "$output_file"

  assert_file_content '' "$output_file"
}

# GitHub resolves a closing reference to an issue in another repository too, and this is the one
# place the script narrows what it returns: every caller acts within this repository, so an issue
# somewhere else is neither a contract this review can fetch nor an item on this board.
closing_issues_ignore_another_repository() {
  local output_file="$test_directory/closing-issues-cross-repository"

  run_closing_issues \
    '[{"number": 999, "repository": {"nameWithOwner": "SomebodyElse/Other"}},
      {"number": 271, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}}]' \
    "$output_file"

  assert_file_content '271' "$output_file"
}

# The ceiling reports what it cut, because the step that applies it promises exactly that of every
# ceiling it defines. An issue nobody was told about is one that closes on merge with its acceptance
# list unread and its board item left behind, which is the failure the ceiling's report exists to
# prevent.
closing_issues_report_what_the_ceiling_cut() {
  local output_file="$test_directory/closing-issues-ceiling"
  local note_file="$test_directory/closing-issues-ceiling-note"

  run_closing_issues \
    '[{"number": 1, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 2, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 3, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 4, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 5, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 6, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 7, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}}]' \
    "$output_file" "$note_file" 5

  assert_file_content $'1\n2\n3\n4\n5' "$output_file"
  assert_contains 'closes 7 issues and this run covers the first 5' "$note_file"
}

# The note is a report of a cut rather than a line the collection always writes, so an answer under
# the ceiling produces none. A truncation file that always had content would put a sentence about
# completeness into every review body it appears in.
closing_issues_report_nothing_when_the_ceiling_is_not_reached() {
  local output_file="$test_directory/closing-issues-under-ceiling"
  local note_file="$test_directory/closing-issues-under-ceiling-note"

  run_closing_issues \
    '[{"number": 1, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 2, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}}]' \
    "$output_file" "$note_file" 5

  assert_file_content $'1\n2' "$output_file"
  assert_file_content '' "$note_file"
}

# Standard error is this script's second output rather than its log: `Fathom review` redirects it
# into `truncation.txt` and pastes that file verbatim into the published review body, under the
# heading for what a ceiling dropped. A recovered retry announcing itself there would arrive in the
# review as coverage the pass did not have, so the helper's notices are held back and forwarded only
# when the call finally fails.
closing_issues_keep_a_recovered_retry_off_standard_error() {
  local output_file="$test_directory/closing-issues-recovered"
  local note_file="$test_directory/closing-issues-recovered-note"

  run_closing_issues \
    '[{"number": 265, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}}]' \
    "$output_file" "$note_file" 0 1

  assert_file_content '265' "$output_file"
  assert_file_content '' "$note_file"
}

closing_issues_report_each_issue_once() {
  local output_file="$test_directory/closing-issues-duplicates"

  run_closing_issues \
    '[{"number": 265, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}},
      {"number": 265, "repository": {"nameWithOwner": "Krzysztof318/MailFathom"}}]' \
    "$output_file"

  assert_file_content '265' "$output_file"
}

# The same index, reached from the working tree instead of from a pull request. `$review-change` runs
# it while the change is still being corrected, which is the point at which an absent test costs the
# least to add, and it reaches the pipeline's own script through an adapter rather than through a
# second implementation — so a rule cannot hold in one and lapse in the other.
create_review_obligations_fixture() {
  local fixture_root="$1"

  create_obligation_fixture "$fixture_root"

  # Both scripts are copied in rather than reached through `$source_repository_root`, because the one
  # under test resolves its own repository root and finds the index beside it. A fixture that
  # borrowed either from outside would be testing a path this arrangement does not have.
  mkdir -p "$fixture_root/.github/fathom-review" "$fixture_root/scripts"
  cp "$source_repository_root/.github/fathom-review/index-obligations.sh" \
    "$fixture_root/.github/fathom-review/index-obligations.sh"
  cp "$source_repository_root/scripts/review-obligations.sh" \
    "$fixture_root/scripts/review-obligations.sh"
  chmod +x "$fixture_root/scripts/review-obligations.sh" \
    "$fixture_root/.github/fathom-review/index-obligations.sh"

  git -C "$fixture_root" init --initial-branch=main --quiet
  git -C "$fixture_root" config user.email agent-workflow@example.invalid
  git -C "$fixture_root" config user.name 'Agent Workflow Tests'
  git -C "$fixture_root" add .
  git -C "$fixture_root" commit --quiet -m 'base'
}

run_review_obligations() {
  local fixture_root="$1" output_file="$2"

  (
    cd "$fixture_root"
    bash scripts/review-obligations.sh main
  ) > "$output_file" 2>&1
}

review_obligations_reports_a_source_the_working_tree_leaves_untested() {
  local fixture_root="$test_directory/review-obligations"
  local output_file="$test_directory/review-obligations-output"

  create_review_obligations_fixture "$fixture_root"

  printf 'internal sealed class MailboxSprocket;\n' \
    > "$fixture_root/backend/src/Application/Emails/MailboxSprocket.cs"
  git -C "$fixture_root" add backend/src/Application/Emails/MailboxSprocket.cs

  run_review_obligations "$fixture_root" "$output_file"

  assert_contains 'Nothing under backend/tests/ names MailboxSprocket.' "$output_file"
  assert_contains 'docs/features/widgets.md' "$output_file"
  assert_contains 'None of this is a finding.' "$output_file"
}

# A file git does not track is in no diff, and a new class owing a test is the shape it takes. The
# report says so rather than describing less than the change while looking complete.
review_obligations_names_the_untracked_paths_no_diff_contains() {
  local fixture_root="$test_directory/review-obligations-untracked"
  local output_file="$test_directory/review-obligations-untracked-output"

  create_review_obligations_fixture "$fixture_root"

  printf 'internal sealed class MailboxSprocket;\n' \
    > "$fixture_root/backend/src/Application/Emails/MailboxSprocket.cs"

  run_review_obligations "$fixture_root" "$output_file"

  assert_contains 'backend/src/Application/Emails/MailboxSprocket.cs' "$output_file"
  assert_contains 'Stage them and run this again.' "$output_file"
}

# It reports and never gates. A row is not a finding until somebody confirms it in the file it points
# at, so exiting non-zero on one would turn "look here" into "this is wrong".
review_obligations_reports_without_gating() {
  local fixture_root="$test_directory/review-obligations-exit"
  local output_file="$test_directory/review-obligations-exit-output"

  create_review_obligations_fixture "$fixture_root"

  printf 'internal sealed class MailboxSprocket;\n' \
    > "$fixture_root/backend/src/Application/Emails/MailboxSprocket.cs"
  git -C "$fixture_root" add backend/src/Application/Emails/MailboxSprocket.cs

  run_review_obligations "$fixture_root" "$output_file"
}

# Linux caps a *single* argv string at 128 KiB however much room the whole argument list has, so a
# patch past that fails `execve` and — under `set -e` — ends the run with `Argument list too long`
# instead of dropping one record. A diff that rewrites or removes a large file is exactly the change
# most likely to owe a test or a page, so the reading is lost where it is worth the most.
review_obligations_reports_on_a_patch_past_the_argument_limit() {
  local fixture_root="$test_directory/review-obligations-large-patch"
  local output_file="$test_directory/review-obligations-large-patch-output"

  create_review_obligations_fixture "$fixture_root"

  # Nothing is staged here, and the omission is the point rather than an oversight: the fixture's
  # base commit already carries `MailboxWidget.cs`, so overwriting it is a tracked modification that
  # `git diff --name-status` reports on its own. The tests above stage what they write because they
  # write `MailboxSprocket.cs`, which the fixture does not carry — an unstaged file there would be
  # reported as untracked instead of diffed, which is the case one of them exists to cover.
  local filler line
  filler="$(printf 'x%.0s' {1..64})"
  {
    printf 'internal sealed class MailboxWidget;\n'
    for ((line = 0; line < 4000; line++)); do
      printf '// %s\n' "$filler"
    done
  } > "$fixture_root/backend/src/Application/Emails/MailboxWidget.cs"

  run_review_obligations "$fixture_root" "$output_file"

  assert_excludes 'Argument list too long' "$output_file"
  assert_contains 'Nothing under backend/tests/ names MailboxWidget.' "$output_file"
  assert_contains 'None of this is a finding.' "$output_file"
}

# A migration owes no unit test. `AGENTS.md` makes migrations append-only and generated, so an index
# that listed them would put the same wrong finding in front of the reviewer on every schema change.
obligation_index_leaves_migrations_out() {
  local fixture_root="$test_directory/obligations-migration"
  local files_json="$test_directory/obligations-migration-files.json"
  local output_file="$test_directory/obligations-migration.json"

  create_obligation_fixture "$fixture_root"
  mkdir -p "$fixture_root/backend/src/Infrastructure/Persistence/Migrations"
  printf '%s\n' '[{"filename":"backend/src/Infrastructure/Persistence/Migrations/20260802_AddWidget.cs","status":"added","patch":"@@ -0,0 +1 @@\n+// generated"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '0' '.tests | length' "$output_file"
}

# The split is what makes coverage a property of the run rather than of the model. Every contract
# below is about one of the two things that must hold whatever the shape of the change: no file is
# lost, and no reader is given so much more than another that the run waits on it.
run_group_split() {
  local files_json="$1"
  local groups_json="$2"
  local max_groups="${3:-6}"
  local target_group_size="${4:-12}"
  # The paths that moved since the last review, absent by default: a first pass is the ordinary case
  # and the contracts about a bounded pass are the ones that name a file.
  local changed_since_last_review="${5:-}"

  bash "$source_repository_root/.github/fathom-review/group-changed-files.sh" \
    "$files_json" "$groups_json" "$max_groups" "$target_group_size" "$changed_since_last_review" \
    > /dev/null
}

# The files of a change, as `files.json` carries them, spread over the directories a real one
# touches. The count is what each contract varies.
write_split_fixture() {
  local files_json="$1"
  local count="$2"
  local directories=('backend/src/Domain/Emails' 'backend/src/Application/Contacts' 'backend/src/Infrastructure/Persistence'
                     'backend/src/Mcp/Tools' 'backend/tests/Domain.UnitTests' 'docs/features'
                     'deploy/helm/mailfathom/templates')
  local index

  {
    printf '['
    for (( index = 0; index < count; index++ )); do
      (( index > 0 )) && printf ','
      printf '{"filename":"%s/File%03d.cs"}' "${directories[$(( index % ${#directories[@]} ))]}" "$index"
    done
    printf ']\n'
  } > "$files_json"
}

# The one property nothing else can recover from. A file in no group is read by nobody, and the
# coverage line would report it as unread without anything saying why — so the script refuses a
# split that loses one, and this is what fixes that it counts.
group_split_gives_every_changed_file_exactly_one_reader() {
  local files_json="$test_directory/split-exhaustive-files.json"
  local groups_json="$test_directory/split-exhaustive-groups.json"

  write_split_fixture "$files_json" 47
  run_group_split "$files_json" "$groups_json"

  assert_json '47' '[.[].files[]] | length' "$groups_json"
  assert_json '47' '[.[].files[]] | unique | length' "$groups_json"
  assert_json 'true' '([.[].files[]] | sort) == ([.[].files[]] | unique)' "$groups_json"
}

# The concurrency is a decision about the owner's subscription, so the ceiling binds however large
# the change is — a hundred files is the collection's own limit and still six readers.
group_split_never_exceeds_the_reader_ceiling() {
  local files_json="$test_directory/split-ceiling-files.json"
  local groups_json="$test_directory/split-ceiling-groups.json"

  write_split_fixture "$files_json" 100
  run_group_split "$files_json" "$groups_json"

  assert_json 'true' 'length <= 6' "$groups_json"
  assert_json '100' '[.[].files[]] | length' "$groups_json"
}

# The run is exactly as long as its slowest reader, so a split that is exhaustive and lopsided has
# spent the concurrency and kept the duration. Twice the mean is the bound: it leaves room for a
# cut pulled to a directory boundary and refuses the shape this replaced, where one group of 38
# stood beside five of 10.
group_split_balances_what_each_reader_is_given() {
  local files_json="$test_directory/split-balance-files.json"
  local groups_json="$test_directory/split-balance-groups.json"

  write_split_fixture "$files_json" 88
  run_group_split "$files_json" "$groups_json"

  assert_json 'true' '([.[].files | length] | max) <= (([.[].files[]] | length) / length * 2 | floor)' \
    "$groups_json"
}

# Below the ceiling the group size is what binds, so a small change is one reader rather than six
# sessions of two files each — the fan-out costs a runner and a session per group, and a change this
# size is read faster than the split saves.
group_split_keeps_a_small_change_whole() {
  local files_json="$test_directory/split-small-files.json"
  local groups_json="$test_directory/split-small-groups.json"

  write_split_fixture "$files_json" 7
  run_group_split "$files_json" "$groups_json"

  assert_json '1' 'length' "$groups_json"
  assert_json '7' '.[0].files | length' "$groups_json"
}

# A collection that returned nothing still has to reach the judge, which is what publishes the
# review that says so. An empty split is the shape that carries it: no reader runs, and the matrix
# the workflow builds from this is empty rather than malformed.
group_split_accepts_a_change_it_was_given_no_files_for() {
  local files_json="$test_directory/split-empty-files.json"
  local groups_json="$test_directory/split-empty-groups.json"

  printf '[]\n' > "$files_json"
  run_group_split "$files_json" "$groups_json"

  assert_json '0' 'length' "$groups_json"
}

# The reader's prompt is composed by the step below rather than handed over whole: the group's file
# list and the shared rubrics are read out of two files and inserted, and everything else is
# substituted. What this runs is that step, against a real `groups.json` and the committed prompt, so
# a template or an insertion that stopped working is caught here rather than by a reader opening
# nothing.
fathom_review_composes_a_reader_prompt_naming_only_its_group() {
  local step_script="$test_directory/fathom-review-reader-prompt.sh"
  local review_directory="$test_directory/fathom-review-reader-prompt-review"
  local prompt_file="$test_directory/fathom-review-reader-prompt.md"
  local output_file="$test_directory/fathom-review-reader-prompt-output"
  local step_output_file="$test_directory/fathom-review-reader-prompt-step-output"
  local status

  extract_fathom_review_step 'reader_prompt' "$step_script"
  rm -rf "$review_directory"
  mkdir -p "$review_directory"
  printf '%s\n' \
    '[{"index":1,"files":["backend/src/Domain/Emails/Email.cs","backend/src/Domain/Emails/EmailAddress.cs"]},{"index":2,"files":["docs/features/mail.md"]}]' \
    > "$review_directory/groups.json"
  : > "$step_output_file"

  set +e
  (
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='930'
    export HEAD_SHA='0123456789abcdef'
    export SNAPSHOT_TAKEN='2026-08-18T09:00:00Z'
    export GROUP_INDEX='1'
    export GROUP_COUNT='2'
    export REVIEW_POSTURE='settling'
    export REVIEW_DIRECTORY="$review_directory"
    export TEMPLATE_FILE="$source_repository_root/.github/fathom-review/reader-prompt.md"
    export RUBRICS_FILE="$source_repository_root/.github/fathom-review/review-rubrics.md"
    export SCHEMA_FILE="$source_repository_root/.github/fathom-review/candidates-schema.json"
    export PROMPT_FILE="$prompt_file"
    export GITHUB_OUTPUT="$step_output_file"
    bash "$step_script"
  ) > "$output_file" 2>&1
  status=$?
  set -e

  # The step froze the collected inputs the way it does on a runner, where the directory dies with
  # the job. Here it outlives the step, so the modes are restored or the suite cannot clean up after
  # itself.
  chmod -R u+w "$review_directory"

  (( status == 0 ))
  # Its own group and nothing else. A reader given another group's paths reviews a file somebody
  # else is already reading, and the coverage the run publishes then counts it twice.
  assert_contains '- `backend/src/Domain/Emails/Email.cs`' "$prompt_file"
  assert_contains '- `backend/src/Domain/Emails/EmailAddress.cs`' "$prompt_file"
  assert_excludes 'docs/features/mail.md' "$prompt_file"
  # The rubrics arrive whole rather than as the placeholder that stands for them.
  assert_contains '### The repository' "$prompt_file"
  assert_contains 'Security and privacy' "$prompt_file"
  assert_excludes '{{' "$prompt_file"
  assert_contains 'GROUP: 1 of 2' "$prompt_file"
  # The bar the gate resolved reaches the reader as the word itself. A reader deriving it from the
  # conversation would be reading a bar out of the very passes it was given to save.
  assert_contains 'REVIEW POSTURE: settling' "$prompt_file"
}

# A group index the split never produced means the matrix and `groups.json` have come apart, and a
# session given no files answers with an empty coverage list that reads exactly like a clean group.
fathom_review_refuses_a_reader_group_that_holds_no_files() {
  local step_script="$test_directory/fathom-review-reader-prompt-empty.sh"
  local review_directory="$test_directory/fathom-review-reader-prompt-empty-review"
  local output_file="$test_directory/fathom-review-reader-prompt-empty-output"
  local status

  extract_fathom_review_step 'reader_prompt' "$step_script"
  rm -rf "$review_directory"
  mkdir -p "$review_directory"
  printf '%s\n' '[{"index":1,"files":["backend/src/Domain/Emails/Email.cs"]}]' > "$review_directory/groups.json"

  set +e
  (
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='930'
    export HEAD_SHA='0123456789abcdef'
    export SNAPSHOT_TAKEN='2026-08-18T09:00:00Z'
    export GROUP_INDEX='4'
    export GROUP_COUNT='1'
    export REVIEW_POSTURE='full'
    export REVIEW_DIRECTORY="$review_directory"
    export TEMPLATE_FILE="$source_repository_root/.github/fathom-review/reader-prompt.md"
    export RUBRICS_FILE="$source_repository_root/.github/fathom-review/review-rubrics.md"
    export SCHEMA_FILE="$source_repository_root/.github/fathom-review/candidates-schema.json"
    export PROMPT_FILE="$test_directory/fathom-review-reader-prompt-empty.md"
    export GITHUB_OUTPUT="$test_directory/fathom-review-reader-prompt-empty-step-output"
    bash "$step_script"
  ) > "$output_file" 2>&1
  status=$?
  set -e

  chmod -R u+w "$review_directory"

  (( status == 1 ))
  assert_contains 'the split and the matrix disagree' "$output_file"
}

# A later pass re-reads the groups that moved and no others. This is the largest saving in the
# workflow — a change is reviewed 2.94 times on average and the readers used to pay full price for
# every pass — so what it must never do is drop a group that did move.
group_split_reads_only_the_groups_that_moved() {
  local files_json="$test_directory/split-bounded-files.json"
  local groups_json="$test_directory/split-bounded-groups.json"
  local moved_file="$test_directory/split-bounded-moved.txt"

  local first_moved later_moved

  write_split_fixture "$files_json" 40
  # One path out of the fixture's first directory and one out of a later one, so the answer cannot
  # come from a group boundary happening to fall in the right place.
  first_moved="$(jq -r '.[0].filename' "$files_json")"
  later_moved="$(jq -r '.[25].filename' "$files_json")"
  printf '%s\n%s\n' "$first_moved" "$later_moved" > "$moved_file"

  run_group_split "$files_json" "$groups_json" 6 12 "$moved_file"

  # Every group holding a moved path is read, and every group holding none is not.
  assert_json 'true' \
    "[.[] | {read: .read_this_pass, holds: ((.files | map(select(. == \"${first_moved}\" or . == \"${later_moved}\")) | length) > 0)}] | all(.read == .holds)" \
    "$groups_json"
  # At least one group is left out, or the contract would pass on a split that read everything.
  assert_json 'true' '[.[] | select(.read_this_pass | not)] | length > 0' "$groups_json"
  # And the split itself is unchanged: the bound decides what is re-read, never what is grouped.
  assert_json '40' '[.[].files[]] | length' "$groups_json"
}

# A first pass, a review somebody asked for, and a comparison the API refused all arrive here as an
# absent file, and all three put the whole change in scope. A bound nobody could establish must not
# silence a reader.
group_split_reads_every_group_when_nothing_bounds_the_pass() {
  local files_json="$test_directory/split-unbounded-files.json"
  local groups_json="$test_directory/split-unbounded-groups.json"

  write_split_fixture "$files_json" 40
  run_group_split "$files_json" "$groups_json" 6 12 "$test_directory/split-unbounded-absent.txt"

  assert_json 'true' 'all(.read_this_pass)' "$groups_json"
}

# The prompts are templates the workflow substitutes into, and a placeholder nobody substitutes
# reaches the model as literal text — a reader told to open `{{GROUP_FILES}}`. Both composing steps
# refuse that at runtime; this refuses it at the point somebody adds a placeholder to a prompt and
# not to the step, which is where the mistake is actually made.
fathom_review_substitutes_every_placeholder_its_prompts_carry() {
  local workflow_file="$source_repository_root/.github/workflows/fathom-review.yml"
  local prompt_file placeholder

  for prompt_file in "$source_repository_root/.github/fathom-review/reader-prompt.md" \
                     "$source_repository_root/.github/fathom-review/reviewer-prompt.md"; do
    while IFS= read -r placeholder; do
      # The rubrics and the file list are inserted whole rather than substituted into a line, so the
      # workflow names them in `awk` rather than in the `sed` script beside it. Either spelling is
      # the step handling the placeholder, which is what this asserts.
      grep -q "{{${placeholder}}}" "$workflow_file"
    done < <(grep -ohE '\{\{[A-Z_]+\}\}' "$prompt_file" | tr -d '{}' | sort -u)
  done
}

# The reader answers with a coverage list and the judge does not, and that split is the whole of why
# coverage stopped being a claim a reviewer makes about itself. A schema that drifted back would put
# the property in the model's hands with every contract above still green.
fathom_review_schemas_keep_coverage_with_the_readers() {
  local candidates_schema="$source_repository_root/.github/fathom-review/candidates-schema.json"
  local findings_schema="$source_repository_root/.github/fathom-review/findings-schema.json"

  assert_json 'true' '.required | index("covered") != null' "$candidates_schema"
  assert_json 'true' '.required | index("candidates") != null' "$candidates_schema"
  assert_json 'true' '.required | index("covered") == null' "$findings_schema"
  assert_json 'true' '.properties | has("covered") | not' "$findings_schema"
}

# `Publish container image` resolves the repository an image belongs to, and both callers hand it tags alone.
# Qualifying those tags is therefore part of the same step, and nightly run 30725904948 is what leaving them bare
# costs: an unqualified name is not a tag to a registry client but an image on the default registry, so the push asked
# `auth.docker.io` for a token instead of writing to GHCR and every gate before it passed. The block is extracted the
# way the ones above are, because it is a step inside YAML rather than a script the job could call.
extract_publish_reference_step() {
  local step_script="$1"

  awk '
    $0 == "        id: reference" { found = 1; next }
    found && !extracting && /^        run: \|$/ { extracting = 1; next }
    extracting {
      if ($0 != "" && $0 !~ /^          /) { exit }
      sub(/^          /, "")
      print
    }
  ' "$source_repository_root/.github/workflows/publish-container-image.yml" > "$step_script"

  [[ -s "$step_script" ]]
  bash -n "$step_script"
}

# The step reads the committed timestamp of the checkout it runs in, so it runs inside the fixture
# repository rather than in the temporary directory beside it.
run_publish_reference_step() {
  local image_tags="$1"
  local output_file="$2"
  local step_output_file="$3"
  local image_channel="${4:-release}"
  local image_version="${5:-0.1.0}"
  local step_script="$test_directory/publish-reference-step.sh"

  extract_publish_reference_step "$step_script"
  : > "$step_output_file"

  (
    export REPOSITORY='Krzysztof318/MailFathom'
    export IMAGE_TAGS="$image_tags"
    export IMAGE_CHANNEL="$image_channel"
    export IMAGE_VERSION="$image_version"
    export GITHUB_OUTPUT="$step_output_file"
    cd "$repository_root"
    bash "$step_script"
  ) > "$output_file" 2>&1
}

# What the pushing step is handed, read out of the multi-line output the way the runner reads it, so
# the assertion is about every reference rather than about one line that happens to be present.
read_published_references() {
  awk '
    /^references<<REFERENCES$/ { inside = 1; next }
    $0 == "REFERENCES" { inside = 0 }
    inside { print }
  ' "$1"
}

publish_qualifies_every_nightly_tag_with_the_repository_it_resolves() {
  local output_file="$test_directory/publish-reference-nightly-output"
  local step_output_file="$test_directory/publish-reference-nightly-step-output"
  local references_file="$test_directory/publish-reference-nightly-references"

  if ! run_publish_reference_step $'0.1.0-nightly.12-616d0a6\nnightly\n' "$output_file" "$step_output_file"; then
    printf 'The publish workflow failed to resolve a nightly tag list\n' >&2
    return 1
  fi

  read_published_references "$step_output_file" > "$references_file"

  assert_contains 'image=ghcr.io/krzysztof318/mailfathom' "$step_output_file"
  assert_contains 'docker-hub-image=docker.io/krzysztof318/mailfathom' "$step_output_file"
  assert_contains 'primary-reference=ghcr.io/krzysztof318/mailfathom:0.1.0-nightly.12-616d0a6' "$step_output_file"
  assert_contains 'docker-hub-primary-reference=docker.io/krzysztof318/mailfathom:0.1.0-nightly.12-616d0a6' "$step_output_file"
  assert_file_content \
    $'ghcr.io/krzysztof318/mailfathom:0.1.0-nightly.12-616d0a6\nghcr.io/krzysztof318/mailfathom:nightly\ndocker.io/krzysztof318/mailfathom:0.1.0-nightly.12-616d0a6\ndocker.io/krzysztof318/mailfathom:nightly' \
    "$references_file"
}

# The Docker Hub account is this owner's login, and a registry namespace is lowercase where a GitHub
# login need not be. One fold produces both, so a mirror cannot come to point at a namespace that is
# somebody else's or at none at all.
publish_folds_the_owner_login_into_the_docker_hub_namespace() {
  local output_file="$test_directory/publish-reference-namespace-output"
  local step_output_file="$test_directory/publish-reference-namespace-step-output"

  if ! run_publish_reference_step $'0.1.0\n' "$output_file" "$step_output_file"; then
    printf 'The publish workflow failed to resolve the Docker Hub namespace\n' >&2
    return 1
  fi

  assert_contains 'docker-hub-namespace=krzysztof318' "$step_output_file"
  assert_contains 'docker-hub-repository=krzysztof318/mailfathom' "$step_output_file"
}

# The release channel's own tag list, and the blank line a heredoc-built list carries. `latest` is the
# tag that made the defect cheap to miss: it looks like a tag everywhere else and resolves to
# `docker.io/library/latest` here.
publish_qualifies_the_release_tags_and_ignores_a_blank_line() {
  local output_file="$test_directory/publish-reference-release-output"
  local step_output_file="$test_directory/publish-reference-release-step-output"
  local references_file="$test_directory/publish-reference-release-references"

  if ! run_publish_reference_step $'0.1.0\n\nlatest\n' "$output_file" "$step_output_file"; then
    printf 'The publish workflow failed to resolve a release tag list\n' >&2
    return 1
  fi

  read_published_references "$step_output_file" > "$references_file"

  assert_file_content \
    $'ghcr.io/krzysztof318/mailfathom:0.1.0\nghcr.io/krzysztof318/mailfathom:latest\ndocker.io/krzysztof318/mailfathom:0.1.0\ndocker.io/krzysztof318/mailfathom:latest' \
    "$references_file"
}

# The same step decides which directory of the documentation site the image's
# `org.opencontainers.image.documentation` label sends a reader to, because a Dockerfile cannot read a
# channel and the label is the one an operator follows without asking anybody. A release is documented
# under its own version.
publish_sends_a_release_to_its_own_documentation_directory() {
  local output_file="$test_directory/publish-reference-documentation-release-output"
  local step_output_file="$test_directory/publish-reference-documentation-release-step-output"

  if ! run_publish_reference_step $'0.1.0\nlatest\n' "$output_file" "$step_output_file" 'release' '0.1.0'; then
    printf 'The publish workflow failed to resolve a release documentation directory\n' >&2
    return 1
  fi

  assert_contains 'documentation-version=v0.1.0' "$step_output_file"
}

# A nightly is named after a release the site publishes nothing for yet, and what it carries is `main`.
# Labelling it with its own version would hand every reader of a nightly image an address that 404s.
publish_sends_a_nightly_to_the_default_branch_documentation() {
  local output_file="$test_directory/publish-reference-documentation-nightly-output"
  local step_output_file="$test_directory/publish-reference-documentation-nightly-step-output"

  if ! run_publish_reference_step \
    $'0.1.0-nightly.12-616d0a6\nnightly\n' "$output_file" "$step_output_file" \
    'nightly' '0.1.0-nightly.12-616d0a6'; then
    printf 'The publish workflow failed to resolve a nightly documentation directory\n' >&2
    return 1
  fi

  assert_contains 'documentation-version=latest' "$step_output_file"
}

# A tag list that is empty once blank lines are dropped would otherwise push `ghcr.io/<repository>:`,
# which the registry answers for reasons that say nothing about the missing tag.
publish_refuses_a_tag_list_with_nothing_to_publish() {
  local output_file="$test_directory/publish-reference-empty-output"
  local step_output_file="$test_directory/publish-reference-empty-step-output"

  if run_publish_reference_step $'\n   \n' "$output_file" "$step_output_file"; then
    printf 'The publish workflow resolved a reference from an empty tag list\n' >&2
    return 1
  fi

  assert_contains 'No image tag was supplied' "$output_file"
  assert_file_content '' "$step_output_file"
}

# The release gate is a script rather than a step inside `release.yml`, so the contracts below run it directly instead
# of extracting it from YAML. Everything it decides — what a release tag may look like, which history it may come from,
# and what the tagged tree has to say about itself — is decided once here, before a workflow builds anything, because
# a published artifact is the one thing in this repository that cannot be corrected by a later commit.
#
# The fixture writes `refs/remotes/origin/main` and `refs/remotes/origin/release/*` directly rather than cloning a
# remote to obtain them. Those refs are the whole of what reachability is judged against, and creating them is a truer
# fixture than a clone whose fetch could succeed for reasons the assertion never reads.
create_release_fixture() {
  local fixture_root="$1"
  local declared_version="$2"
  local changelog_body="$3"

  mkdir -p "$fixture_root/scripts"
  cp \
    "$source_repository_root/scripts/assert-release-tag.sh" \
    "$source_repository_root/scripts/read-changelog-section.sh" \
    "$fixture_root/scripts/"

  git -C "$fixture_root" init --initial-branch=main --quiet
  git -C "$fixture_root" config user.email agent-workflow@example.invalid
  git -C "$fixture_root" config user.name 'Agent Workflow Tests'

  write_declared_version "$fixture_root" '0.1.0'
  printf '# Changelog\n\n## [0.1.0] - 2026-01-01\n\n### Added\n\n- The first release.\n' > "$fixture_root/CHANGELOG.md"
  git -C "$fixture_root" add .
  git -C "$fixture_root" commit --quiet -m 'release 0.1.0'
  git -C "$fixture_root" tag --annotate v0.1.0 --message 'MailFathom 0.1.0'

  write_declared_version "$fixture_root" "$declared_version"
  printf '# Changelog\n\n## [%s] - 2026-02-01\n%s\n## [0.1.0] - 2026-01-01\n\n### Added\n\n- The first release.\n' \
    "$declared_version" "$changelog_body" > "$fixture_root/CHANGELOG.md"
  git -C "$fixture_root" add .
  git -C "$fixture_root" commit --quiet -m "release $declared_version"

  git -C "$fixture_root" update-ref refs/remotes/origin/main refs/heads/main
}

write_declared_version() {
  local fixture_root="$1"
  local declared_version="$2"

  printf '<Project>\n  <PropertyGroup>\n    <VersionPrefix>%s</VersionPrefix>\n  </PropertyGroup>\n</Project>\n' \
    "$declared_version" > "$fixture_root/Version.props"
}

assert_release_tag() {
  local fixture_root="$1"
  local release_tag="$2"
  local output_file="$3"

  (
    cd "$fixture_root"
    bash scripts/assert-release-tag.sh "$release_tag"
  ) > "$output_file" 2>&1
}

release_tag_assertion_accepts_a_tag_that_matches_its_commit() {
  local fixture_root="$test_directory/release-accepts"
  local output_file="$test_directory/release-accepts-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" tag --annotate v0.2.0 --message 'MailFathom 0.2.0'

  assert_release_tag "$fixture_root" 'v0.2.0' "$output_file"

  assert_contains '0.2.0' "$output_file"
  assert_contains 'reachable from origin/main' "$output_file"
}

release_tag_assertion_refuses_a_prerelease_tag() {
  local fixture_root="$test_directory/release-prerelease"
  local output_file="$test_directory/release-prerelease-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" tag --annotate v0.2.0-rc.1 --message 'MailFathom 0.2.0-rc.1'

  if assert_release_tag "$fixture_root" 'v0.2.0-rc.1' "$output_file"; then
    printf 'assert-release-tag.sh released a prerelease tag\n' >&2
    return 1
  fi

  assert_contains 'carries no prerelease identifier' "$output_file"
}

release_tag_assertion_refuses_a_lightweight_tag() {
  local fixture_root="$test_directory/release-lightweight"
  local output_file="$test_directory/release-lightweight-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" tag v0.2.0

  if assert_release_tag "$fixture_root" 'v0.2.0' "$output_file"; then
    printf 'assert-release-tag.sh released a lightweight tag\n' >&2
    return 1
  fi

  assert_contains 'is a lightweight tag' "$output_file"
}

release_tag_assertion_refuses_a_version_the_commit_does_not_declare() {
  local fixture_root="$test_directory/release-disagreeing-version"
  local output_file="$test_directory/release-disagreeing-version-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" tag --annotate v0.3.0 --message 'MailFathom 0.3.0'

  if assert_release_tag "$fixture_root" 'v0.3.0' "$output_file"; then
    printf 'assert-release-tag.sh released a version the tagged tree does not declare\n' >&2
    return 1
  fi

  assert_contains '<VersionPrefix>0.2.0</VersionPrefix>' "$output_file"
}

# A tag from before the version left `Directory.Build.props` for a root `Version.props` points at a commit carrying no
# such file at all. That is the same failure as a commit declaring a different version — the tree does not say what the
# tag claims — so it has to arrive as this script's own message naming the tag rather than as git's `path does not
# exist` under `set -o pipefail`, which would end the run before the reader learned which tag was refused.
release_tag_assertion_refuses_a_commit_that_declares_no_version_at_all() {
  local fixture_root="$test_directory/release-undeclared-version"
  local output_file="$test_directory/release-undeclared-version-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" rm --quiet Version.props
  git -C "$fixture_root" commit --quiet -m 'a tree from before the version moved to its own file'
  git -C "$fixture_root" update-ref refs/remotes/origin/main refs/heads/main
  git -C "$fixture_root" tag --annotate v0.2.0 --message 'MailFathom 0.2.0'

  if assert_release_tag "$fixture_root" 'v0.2.0' "$output_file"; then
    printf 'assert-release-tag.sh released a commit that declares no version at all\n' >&2
    return 1
  fi

  assert_contains 'declares <VersionPrefix>nothing</VersionPrefix>' "$output_file"
}

release_tag_assertion_refuses_a_commit_that_never_merged() {
  local fixture_root="$test_directory/release-unmerged"
  local output_file="$test_directory/release-unmerged-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" checkout --quiet -b agent/unmerged
  printf 'unreviewed\n' > "$fixture_root/unreviewed.txt"
  git -C "$fixture_root" add unreviewed.txt
  git -C "$fixture_root" commit --quiet -m 'work that never merged'
  git -C "$fixture_root" tag --annotate v0.2.0 --message 'MailFathom 0.2.0'

  if assert_release_tag "$fixture_root" 'v0.2.0' "$output_file"; then
    printf 'assert-release-tag.sh released a commit reachable from no protected branch\n' >&2
    return 1
  fi

  assert_contains 'reachable from neither origin/main nor any origin/release/* branch' "$output_file"
}

# A patch is by construction not reachable from `main`, and it is cut after a higher minor has already shipped. Both
# are the ordinary shape of a hotfix rather than something to refuse, which is why the regression rule reads one line
# rather than every tag present.
release_tag_assertion_accepts_a_patch_from_a_release_branch() {
  local fixture_root="$test_directory/release-patch"
  local output_file="$test_directory/release-patch-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" tag --annotate v0.2.0 --message 'MailFathom 0.2.0'
  git -C "$fixture_root" checkout --quiet -b release/0.1.x v0.1.0
  write_declared_version "$fixture_root" '0.1.1'
  printf '# Changelog\n\n## [0.1.1] - 2026-03-01\n\n### Fixed\n\n- A defect the 0.1.x line still has.\n' \
    > "$fixture_root/CHANGELOG.md"
  git -C "$fixture_root" add .
  git -C "$fixture_root" commit --quiet -m 'release 0.1.1'
  git -C "$fixture_root" tag --annotate v0.1.1 --message 'MailFathom 0.1.1'
  git -C "$fixture_root" update-ref refs/remotes/origin/release/0.1.x refs/heads/release/0.1.x

  assert_release_tag "$fixture_root" 'v0.1.1' "$output_file"

  assert_contains '0.1.1' "$output_file"
  assert_contains 'reachable from origin/release/0.1.x' "$output_file"
}

release_tag_assertion_refuses_a_version_already_released_on_its_line() {
  local fixture_root="$test_directory/release-regression"
  local output_file="$test_directory/release-regression-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'
  git -C "$fixture_root" checkout --quiet -b release/0.1.x v0.1.0

  commit_patch_release() {
    write_declared_version "$fixture_root" "$1"
    printf '# Changelog\n\n## [%s] - 2026-03-01\n\n### Fixed\n\n- A defect the 0.1.x line still has.\n' "$1" \
      > "$fixture_root/CHANGELOG.md"
    git -C "$fixture_root" add .
    git -C "$fixture_root" commit --quiet -m "release $1"
  }

  commit_patch_release '0.1.1'
  git -C "$fixture_root" tag --annotate v0.1.1 --message 'MailFathom 0.1.1'
  commit_patch_release '0.1.2'
  git -C "$fixture_root" tag --annotate v0.1.2 --message 'MailFathom 0.1.2'

  # The line has moved past 0.1.1, and the tag is dragged forward onto a newer commit anyway. Everything else about it
  # is consistent — the tree declares 0.1.1 and the changelog describes it — so the line's own history is the only
  # thing left that can refuse it.
  commit_patch_release '0.1.1'
  git -C "$fixture_root" tag --annotate --force v0.1.1 --message 'MailFathom 0.1.1 again' > /dev/null 2>&1
  git -C "$fixture_root" update-ref refs/remotes/origin/release/0.1.x refs/heads/release/0.1.x

  if assert_release_tag "$fixture_root" 'v0.1.1' "$output_file"; then
    printf 'assert-release-tag.sh released a number the line had already used\n' >&2
    return 1
  fi

  assert_contains 'does not advance the 0.1.x line' "$output_file"
}

release_tag_assertion_refuses_an_empty_changelog_section() {
  local fixture_root="$test_directory/release-empty-changelog"
  local output_file="$test_directory/release-empty-changelog-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n'
  git -C "$fixture_root" tag --annotate v0.2.0 --message 'MailFathom 0.2.0'

  if assert_release_tag "$fixture_root" 'v0.2.0' "$output_file"; then
    printf 'assert-release-tag.sh released a version the changelog says nothing about\n' >&2
    return 1
  fi

  assert_contains "section of CHANGELOG.md is empty" "$output_file"
}

changelog_section_reading_returns_only_the_requested_release() {
  local fixture_root="$test_directory/changelog-section"
  local output_file="$test_directory/changelog-section-output"

  create_release_fixture "$fixture_root" '0.2.0' $'\n### Added\n\n- Something an operator notices.\n'

  (
    cd "$fixture_root"
    bash scripts/read-changelog-section.sh '0.2.0'
  ) > "$output_file" 2>&1

  assert_contains 'Something an operator notices.' "$output_file"
  assert_excludes 'The first release.' "$output_file"
}

# The schema artifact is the one release asset an operator runs by hand, against their own database, from a command a
# page here gives them. Nothing downstream of the build applies the published file — the integration suite generates the
# same SQL through EF Core and applies it over ADO.NET, where a reader consumes a byte-order mark that psql would send
# to the server — so what the file's first bytes are is asserted here or nowhere, and a marked artifact reaches whoever
# downloads it next as a syntax error naming a character that cannot be seen.
#
# The fixture stands in for `aspire publish` rather than running it: what these contracts are about is the file the
# script writes from the publish output, and a real publish would need the SDK, the tool manifest, and a minute of build
# time to produce a fixture this one writes in three lines.
create_schema_artifact_fixture() {
  local fixture_root="$1"
  local published_sql="$2"

  mkdir -p "$fixture_root/scripts" "$fixture_root/stubs"
  cp "$source_repository_root/scripts/build-schema-artifact.sh" "$fixture_root/scripts/"

  git -C "$fixture_root" init --initial-branch=main --quiet

  # `aspire publish` writes one SQL script under `efmigrations/`, and the guard the script asserts on is the migration
  # history check that makes the artifact idempotent. Both are what the stub reproduces; the SQL itself is a stand-in.
  cat > "$fixture_root/stubs/aspire" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail

output_path=''
previous=''
for argument in "$@"; do
  if [[ "$previous" == '--output-path' ]]; then
    output_path="$argument"
  fi
  previous="$argument"
done

if [[ "${1:-}" == '--version' ]]; then
  printf 'stub\n'
  exit 0
fi

mkdir -p "$output_path/efmigrations"
cp "$PUBLISHED_SQL" "$output_path/efmigrations/mailfathom.sql"
STUB

  # The manifest-local EF Core tooling is restored before the publish, and nothing in these contracts depends on it.
  printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture_root/stubs/dotnet"

  chmod +x "$fixture_root/stubs/aspire" "$fixture_root/stubs/dotnet"

  cp "$published_sql" "$fixture_root/published.sql"
}

write_published_schema_script() {
  local target_file="$1"
  local leading_bytes="$2"

  {
    printf '%b' "$leading_bytes"
    printf 'CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (\n'
    printf '    "MigrationId" character varying(150) NOT NULL\n);\n\n'
    printf 'DO $EF$\nBEGIN\n'
    printf '    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = %s20260101000000_First%s) THEN\n' \
      "'" "'"
    printf '    CREATE TABLE "Emails" ("Id" uuid NOT NULL);\n    END IF;\nEND $EF$;\n'
  } > "$target_file"
}

build_schema_artifact() {
  local fixture_root="$1"
  local output_directory="$2"
  local output_file="$3"

  (
    cd "$fixture_root"
    PATH="$fixture_root/stubs:$PATH" PUBLISHED_SQL="$fixture_root/published.sql" \
      bash scripts/build-schema-artifact.sh "$output_directory" '9.9.9'
  ) > "$output_file" 2>&1
}

schema_artifact_carries_no_byte_order_mark() {
  local fixture_root="$test_directory/schema-artifact-marked"
  local output_directory="$fixture_root/artifacts"
  local output_file="$test_directory/schema-artifact-marked-output"
  local artifact_path="$output_directory/mailfathom-schema-9.9.9.sql"

  write_published_schema_script "$test_directory/published-marked.sql" '\xef\xbb\xbf'
  create_schema_artifact_fixture "$fixture_root" "$test_directory/published-marked.sql"

  build_schema_artifact "$fixture_root" "$output_directory" "$output_file"

  if [[ "$(head --bytes=6 "$artifact_path")" != 'CREATE' ]]; then
    printf 'Expected the artifact to begin with CREATE, and it begins with: %s\n' \
      "$(head --bytes=6 "$artifact_path" | od --address-radix=n --format=x1)" >&2
    return 1
  fi

  # The mark is the only difference the script may make to the publish output, so the rest is compared byte for byte.
  if ! tail --bytes=+4 "$test_directory/published-marked.sql" | cmp --quiet - "$artifact_path"; then
    printf 'The artifact differs from the published script by more than its byte-order mark.\n' >&2
    return 1
  fi

  assert_contains '20260101000000_First' "$output_file"
}

schema_artifact_checksum_covers_the_file_an_operator_applies() {
  local fixture_root="$test_directory/schema-artifact-checksum"
  local output_directory="$fixture_root/artifacts"
  local output_file="$test_directory/schema-artifact-checksum-output"

  write_published_schema_script "$test_directory/published-checksum.sql" '\xef\xbb\xbf'
  create_schema_artifact_fixture "$fixture_root" "$test_directory/published-checksum.sql"

  build_schema_artifact "$fixture_root" "$output_directory" "$output_file"

  # Exactly what the documentation asks an operator to run before applying the file.
  if ! (cd "$output_directory" && sha256sum --check --status 'mailfathom-schema-9.9.9.sql.sha256'); then
    printf 'The published checksum does not identify the artifact beside it.\n' >&2
    return 1
  fi
}

schema_artifact_leaves_an_unmarked_publish_untouched() {
  local fixture_root="$test_directory/schema-artifact-unmarked"
  local output_directory="$fixture_root/artifacts"
  local output_file="$test_directory/schema-artifact-unmarked-output"

  write_published_schema_script "$test_directory/published-unmarked.sql" ''
  create_schema_artifact_fixture "$fixture_root" "$test_directory/published-unmarked.sql"

  build_schema_artifact "$fixture_root" "$output_directory" "$output_file"

  if ! cmp --quiet "$test_directory/published-unmarked.sql" "$output_directory/mailfathom-schema-9.9.9.sql"; then
    printf 'A publish that carried no byte-order mark was rewritten anyway.\n' >&2
    return 1
  fi
}

# The winget manifest is the one thing this repository produces whose correctness is judged in somebody else's pull
# request, days after the release that submitted it. What is asserted here is what a Windows install actually depends
# on: that `portable` with `Commands` is what makes the binary reachable as `mfctl` rather than under the versioned
# file name a release attaches it as, and that each hash is taken over the bytes this pipeline built. The schema is
# validated by the community repository against a document only it publishes, so nothing here fetches one.
winget_manifests_name_the_release_assets_they_hash() {
  local binaries_directory="$test_directory/winget-binaries"
  local output_directory="$test_directory/winget-output"
  local package_directory="$output_directory/manifests/m/MailFathom/mfctl/9.9.9"
  local installer_manifest="$package_directory/MailFathom.mfctl.installer.yaml"
  local expected_checksum
  local rendered_count

  mkdir -p "$binaries_directory"
  printf 'x64 bytes' > "$binaries_directory/mfctl-9.9.9-win-x64.exe"
  printf 'arm64 bytes' > "$binaries_directory/mfctl-9.9.9-win-arm64.exe"

  (
    cd "$source_repository_root"
    bash scripts/build-winget-manifests.sh "$binaries_directory" "$output_directory" '9.9.9' '2026-01-02'
  ) > /dev/null 2>&1

  expected_checksum="$(sha256sum "$binaries_directory/mfctl-9.9.9-win-x64.exe" |
    cut --delimiter=' ' --fields=1 | tr '[:lower:]' '[:upper:]')"

  assert_contains 'InstallerType: portable' "$installer_manifest"
  assert_contains '- mfctl' "$installer_manifest"
  assert_contains 'ReleaseDate: 2026-01-02' "$installer_manifest"
  assert_contains 'Architecture: arm64' "$installer_manifest"
  assert_contains "InstallerSha256: $expected_checksum" "$installer_manifest"
  assert_contains \
    'InstallerUrl: https://github.com/Krzysztof318/MailFathom/releases/download/v9.9.9/mfctl-9.9.9-win-x64.exe' \
    "$installer_manifest"

  # Three files and no more. The community repository takes one package version per pull request as a multi-file set,
  # so a fourth file here would be a submission it refuses rather than a manifest with something extra in it.
  rendered_count="$(find "$package_directory" -type f | wc -l)"

  if ((rendered_count != 3)); then
    printf 'The manifest set is three files; %s were rendered.\n' "$rendered_count" >&2
    return 1
  fi
}

# What a Windows operator sees before they install anything: `winget search` prints `PackageName` in its `Name` column
# and the identifier beside it, so the name has to carry the product while the command reaches them through `Moniker`
# and `Commands`. Naming the command in both places instead would leave a listing that says `mfctl` and nothing else.
winget_manifest_names_the_product_and_the_command() {
  local binaries_directory="$test_directory/winget-listing-binaries"
  local output_directory="$test_directory/winget-listing-output"
  local package_directory="$output_directory/manifests/m/MailFathom/mfctl/9.9.9"
  local locale_manifest="$package_directory/MailFathom.mfctl.locale.en-US.yaml"

  mkdir -p "$binaries_directory"
  printf 'x64 bytes' > "$binaries_directory/mfctl-9.9.9-win-x64.exe"
  printf 'arm64 bytes' > "$binaries_directory/mfctl-9.9.9-win-arm64.exe"

  (
    cd "$source_repository_root"
    bash scripts/build-winget-manifests.sh "$binaries_directory" "$output_directory" '9.9.9' '2026-01-02'
  ) > /dev/null 2>&1

  assert_contains 'PackageName: MailFathom CLI' "$locale_manifest"
  assert_contains 'Moniker: mfctl' "$locale_manifest"

  # winget's own convention is `Publisher.Package`, and a submission whose `Publisher` disagrees with the identifier it
  # is filed under is a question somebody else's reviewer asks days later.
  assert_contains 'PackageIdentifier: MailFathom.mfctl' "$locale_manifest"
  assert_contains 'Publisher: MailFathom' "$locale_manifest"
}

# A manifest naming a download that does not exist is refused by winget's validation days later, in a pull request
# nobody is watching. Failing while the release run is still on screen is the difference worth having.
winget_manifests_refuse_a_missing_windows_binary() {
  local binaries_directory="$test_directory/winget-incomplete-binaries"
  local output_directory="$test_directory/winget-incomplete-output"
  local output_file="$test_directory/winget-incomplete-log"

  mkdir -p "$binaries_directory"
  printf 'x64 bytes' > "$binaries_directory/mfctl-9.9.9-win-x64.exe"

  if (
    cd "$source_repository_root"
    bash scripts/build-winget-manifests.sh "$binaries_directory" "$output_directory" '9.9.9' '2026-01-02'
  ) > "$output_file" 2>&1; then
    printf 'The manifest set was rendered against binaries the release does not attach\n' >&2
    return 1
  fi

  assert_contains 'mfctl-9.9.9-win-arm64.exe' "$output_file"
}

# `install-mfctl.sh` is the one script here that runs on somebody else's machine, from a URL rather than a checkout, so
# what these three cover is what a reader cannot check before running it: that the checksum is enforced rather than
# fetched, that a refusal installs nothing, and that a platform no release publishes is told so instead of being handed
# a download. The release itself is a fixture — `curl` and `uname` are replaced on the PATH — because a test that
# reached GitHub would assert the network rather than the script.
stage_install_script_release() {
  local case_name="$1"
  local checksum_source="$2"
  local release_directory="$test_directory/$case_name-release"
  local stub_directory="$test_directory/$case_name-bin"

  mkdir -p "$release_directory" "$stub_directory"
  printf 'mfctl bytes' > "$release_directory/mfctl-9.9.9-linux-x64"

  # `sha256sum ./*` is how the release builds this file, which is why every entry names its file as `./mfctl-…` and
  # why the script checks from the directory it downloaded into rather than from wherever it was invoked.
  ( cd "$release_directory" && sha256sum ./mfctl-9.9.9-linux-x64 > 'mfctl-9.9.9.sha256' )

  if [[ "$checksum_source" == 'tampered' ]]; then
    printf 'mfctl bytes, altered after the release published them' > "$release_directory/mfctl-9.9.9-linux-x64"
  fi

  # Serves the fixture directory by asset name and answers 22 for anything else, which is what curl reports for a 404
  # under `-f`. Nothing here parses a URL beyond its last segment: the script builds one download base and the test
  # asserts what it downloaded, not how it spelled the address.
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'set -euo pipefail' \
    'output=""' \
    'url=""' \
    'while [[ $# -gt 0 ]]; do' \
    '  case "$1" in' \
    '    --output) output="$2"; shift 2 ;;' \
    '    -*) shift ;;' \
    '    *) url="$1"; shift ;;' \
    '  esac' \
    'done' \
    "release_directory='$release_directory'" \
    'asset="${url##*/}"' \
    '[[ -f "$release_directory/$asset" ]] || exit 22' \
    'cat "$release_directory/$asset" > "$output"' \
    > "$stub_directory/curl"

  # Pinned rather than read from the runner, so the asset the script asks for is the same one on every machine the
  # suite runs on. `uname` is also what the platform refusal below is asserted through.
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'case "$1" in' \
    "  -s) printf '%s\\n' \"\${FAKE_UNAME_SYSTEM:-Linux}\" ;;" \
    "  -m) printf '%s\\n' \"\${FAKE_UNAME_MACHINE:-x86_64}\" ;;" \
    'esac' \
    > "$stub_directory/uname"

  chmod +x "$stub_directory/curl" "$stub_directory/uname"

  printf '%s\n' "$stub_directory"
}

install_script_installs_the_binary_the_release_published() {
  local stub_directory
  local install_directory="$test_directory/install-verified-target"
  local output_file="$test_directory/install-verified-log"

  stub_directory="$(stage_install_script_release 'install-verified' 'published')"

  (
    cd "$source_repository_root"
    PATH="$stub_directory:$PATH" bash scripts/install-mfctl.sh \
      --version 9.9.9 --directory "$install_directory"
  ) > "$output_file" 2>&1

  assert_file_content 'mfctl bytes' "$install_directory/mfctl"
  assert_contains "Installed mfctl 9.9.9 to $install_directory/mfctl" "$output_file"

  if [[ ! -x "$install_directory/mfctl" ]]; then
    printf 'The command was installed without the execute bit, so it cannot be run.\n' >&2
    return 1
  fi
}

# The failure the script exists to make impossible to skip. Nothing may be installed, because a binary that fails its
# checksum is either a broken download or bytes somebody else substituted, and the two are indistinguishable here.
install_script_refuses_a_binary_the_checksum_file_disowns() {
  local stub_directory
  local install_directory="$test_directory/install-tampered-target"
  local output_file="$test_directory/install-tampered-log"

  stub_directory="$(stage_install_script_release 'install-tampered' 'tampered')"

  if (
    cd "$source_repository_root"
    PATH="$stub_directory:$PATH" bash scripts/install-mfctl.sh \
      --version 9.9.9 --directory "$install_directory"
  ) > "$output_file" 2>&1; then
    printf 'A binary that does not match the published checksum was installed\n' >&2
    return 1
  fi

  assert_contains 'does not match the checksum' "$output_file"

  if [[ -e "$install_directory/mfctl" ]]; then
    printf 'The refusal left a command behind at %s\n' "$install_directory/mfctl" >&2
    return 1
  fi
}

# A release publishes four binaries and no macOS build, so the useful failure names what exists. The alternative is a
# download that 404s under an asset name the reader has no way to check against.
install_script_refuses_a_platform_no_release_publishes() {
  local stub_directory
  local install_directory="$test_directory/install-unsupported-target"
  local output_file="$test_directory/install-unsupported-log"

  stub_directory="$(stage_install_script_release 'install-unsupported' 'published')"

  if (
    cd "$source_repository_root"
    PATH="$stub_directory:$PATH" FAKE_UNAME_SYSTEM='Darwin' FAKE_UNAME_MACHINE='arm64' \
      bash scripts/install-mfctl.sh --version 9.9.9 --directory "$install_directory"
  ) > "$output_file" 2>&1; then
    printf 'The script installed a Linux binary on a system no release publishes one for\n' >&2
    return 1
  fi

  assert_contains 'linux-x64, linux-arm64, win-x64, and win-arm64' "$output_file"
}

# The guided Compose setup, which is the one script here that writes a deployment's credentials. Every contract below
# runs the committed script against a checkout of its own — a fixture carrying `deploy/compose/`, never this
# repository's own — because what it writes is exactly what must never be written over somebody's prepared deployment.
#
# None of them reaches the network or a Docker daemon: `--version` is what the release resolution would otherwise ask
# GitHub for, and `--no-start` stops before the stack, the schema step, and the probes. What that leaves asserted is
# the half a reader cannot check by eye — the modes, the two postures, and the refusals.
stage_quick_start_checkout() {
  local case_name="$1"
  local checkout_root="$test_directory/$case_name-checkout"

  mkdir -p "$checkout_root/deploy/compose/config" "$checkout_root/deploy/compose/secrets/mailfathom"

  # Only its existence is read, which is what says this checkout carries the Compose deployment at all.
  printf 'name: mailfathom\n' > "$checkout_root/deploy/compose/compose.yaml"

  # A clone made under a strict umask is what leaves `config/` unlistable by the container's account, so the fixture
  # starts from one rather than from whatever the runner's umask happens to be.
  chmod 700 "$checkout_root/deploy/compose/config" "$checkout_root/deploy/compose/secrets/mailfathom"
  printf 'the mailbox password\n' > "$checkout_root/password"

  git -C "$checkout_root" init --initial-branch=main --quiet

  printf '%s\n' "$checkout_root"
}

run_quick_start() {
  local checkout_root="$1"
  shift

  (
    cd "$checkout_root/deploy/compose"
    bash "$source_repository_root/scripts/quick-start-compose.sh" \
      --user-name 'you@example.test' --display-name 'Personal mail' \
      --password-file "$checkout_root/password" --version 9.9.9 --no-start --non-interactive "$@"
  )
}

# The whole of the manual preparation, asserted where it is invisible: a mode nobody sees is what makes a secret
# reference resolve, and getting one wrong reports itself at startup as material that could not be found.
quick_start_prepares_the_deployment_the_documentation_describes() {
  local checkout_root compose_directory
  local output_file="$test_directory/quick-start-prepared-log"

  checkout_root="$(stage_quick_start_checkout 'quick-start-prepared')"
  compose_directory="$checkout_root/deploy/compose"

  run_quick_start "$checkout_root" --provider fastmail > "$output_file" 2>&1

  assert_contains 'MAILFATHOM_IMAGE=ghcr.io/krzysztof318/mailfathom:9.9.9' "$compose_directory/.env"
  # The image and how it is obtained are one decision: a release tag with the build policy left in place would build
  # the checkout and ignore the pin.
  assert_contains 'MAILFATHOM_PULL_POLICY=missing' "$compose_directory/.env"

  assert_file_content 'the mailbox password' "$compose_directory/secrets/mailfathom/imap-primary-password"
  assert_contains '"Host": "imap.fastmail.com"' "$compose_directory/config/10-mailfathom.json"
  assert_contains '"SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password"' \
    "$compose_directory/config/10-mailfathom.json"

  local expected_modes='700 secrets
711 secrets/mailfathom
755 config
444 secrets/postgres-superuser-password
444 secrets/mailfathom-database-password
444 secrets/mailfathom/imap-primary-password
644 config/10-mailfathom.json'
  local actual_modes

  actual_modes="$(
    cd "$compose_directory"
    stat --format '%a %n' \
      secrets secrets/mailfathom config \
      secrets/postgres-superuser-password secrets/mailfathom-database-password \
      secrets/mailfathom/imap-primary-password config/10-mailfathom.json
  )"

  if [[ "$actual_modes" != "$expected_modes" ]]; then
    printf 'The prepared deployment carries modes the container cannot read:\nExpected:\n%s\nActual:\n%s\n' \
      "$expected_modes" "$actual_modes" >&2
    return 1
  fi

  # Two database passwords generated independently. Equal ones would mean a single generation reused, which is what a
  # refactor of the generation would silently produce.
  if [[ "$(cat "$compose_directory/secrets/postgres-superuser-password")" == \
    "$(cat "$compose_directory/secrets/mailfathom-database-password")" ]]; then
    printf 'The superuser and the service share one database password.\n' >&2
    return 1
  fi
}

# The credential is in a file, and the point of that is which places it is therefore not in.
quick_start_keeps_the_mailbox_password_out_of_everything_but_its_own_file() {
  local checkout_root compose_directory
  local output_file="$test_directory/quick-start-password-log"

  checkout_root="$(stage_quick_start_checkout 'quick-start-password')"
  compose_directory="$checkout_root/deploy/compose"

  run_quick_start "$checkout_root" --provider gmail > "$output_file" 2>&1

  assert_excludes 'the mailbox password' "$compose_directory/config/10-mailfathom.json"
  assert_excludes 'the mailbox password' "$compose_directory/.env"
  assert_excludes 'the mailbox password' "$output_file"
}

# A prepared deployment is somebody's running instance. Replacing its credentials because a script was run twice is the
# one failure this must not have, so the refusal comes before anything is written rather than after.
quick_start_refuses_to_overwrite_a_prepared_deployment() {
  local checkout_root compose_directory
  local first_run="$test_directory/quick-start-overwrite-first"
  local second_run="$test_directory/quick-start-overwrite-second"

  checkout_root="$(stage_quick_start_checkout 'quick-start-overwrite')"
  compose_directory="$checkout_root/deploy/compose"

  run_quick_start "$checkout_root" --provider zoho > "$first_run" 2>&1

  local original_key
  original_key="$(cat "$compose_directory/secrets/mailfathom/mcp-workstation-key")"

  if run_quick_start "$checkout_root" --provider zoho > "$second_run" 2>&1; then
    printf 'A second run replaced a prepared deployment instead of refusing.\n' >&2
    return 1
  fi

  assert_contains 'deploy/compose/.env already exists' "$second_run"
  assert_file_content "$original_key" "$compose_directory/secrets/mailfathom/mcp-workstation-key"
}

# A mailbox whose provider accepts no password cannot be prepared by a script that asks for one, and a configuration
# written anyway fails at the first synchronization with an authentication error that says nothing about why.
quick_start_refuses_a_mailbox_that_accepts_no_password() {
  local checkout_root compose_directory
  local output_file="$test_directory/quick-start-oauth-log"

  checkout_root="$(stage_quick_start_checkout 'quick-start-oauth')"
  compose_directory="$checkout_root/deploy/compose"

  if run_quick_start "$checkout_root" --provider outlook > "$output_file" 2>&1; then
    printf 'A mailbox that accepts no password was prepared with one.\n' >&2
    return 1
  fi

  assert_contains 'accepts no password' "$output_file"
  assert_contains 'mailbox-oauth' "$output_file"

  if [[ -e "$compose_directory/.env" || -e "$compose_directory/config/10-mailfathom.json" ]]; then
    printf 'The refusal left a prepared deployment behind.\n' >&2
    return 1
  fi
}

# The unauthenticated posture is legal and is never what a run that did not ask for it produces.
quick_start_authenticates_the_mcp_endpoint_unless_asked_otherwise() {
  local guarded_root open_root
  local guarded_log="$test_directory/quick-start-guarded-log"
  local open_log="$test_directory/quick-start-open-log"

  guarded_root="$(stage_quick_start_checkout 'quick-start-guarded')"
  run_quick_start "$guarded_root" --provider icloud > "$guarded_log" 2>&1

  assert_contains '"SecretReference": "file:/etc/mailfathom/secrets/mcp-workstation-key"' \
    "$guarded_root/deploy/compose/config/10-mailfathom.json"

  if [[ ! -s "$guarded_root/deploy/compose/secrets/mailfathom/mcp-workstation-key" ]]; then
    printf 'The default posture wrote no MCP credential.\n' >&2
    return 1
  fi

  open_root="$(stage_quick_start_checkout 'quick-start-open')"
  run_quick_start "$open_root" --provider icloud --mcp-authentication none > "$open_log" 2>&1

  assert_contains '"Authentication": [],' "$open_root/deploy/compose/config/10-mailfathom.json"

  if [[ -e "$open_root/deploy/compose/secrets/mailfathom/mcp-workstation-key" ]]; then
    printf 'An endpoint asked to serve without authentication was given a key anyway.\n' >&2
    return 1
  fi
}

# A value carrying a control character would reach the configuration file as broken JSON, and the deployment would then
# fail to start on a file nobody edited. The check that prevents it cannot live in the function that writes the string,
# because that runs inside a command substitution whose exit the heredoc calling it does not propagate.
quick_start_refuses_a_value_that_would_write_broken_configuration() {
  local checkout_root
  local output_file="$test_directory/quick-start-control-character-log"

  checkout_root="$(stage_quick_start_checkout 'quick-start-control-character')"

  if run_quick_start "$checkout_root" --provider fastmail --display-name "$(printf 'Personal\tmail')" \
    > "$output_file" 2>&1; then
    printf 'A control character was written into the configuration instead of stopping the run.\n' >&2
    return 1
  fi

  assert_contains 'control character' "$output_file"

  if [[ -e "$checkout_root/deploy/compose/config/10-mailfathom.json" ]]; then
    printf 'The refusal left a configuration file behind.\n' >&2
    return 1
  fi
}

# What this script produces is a deployment to find out what the product does, and the difference between that and one
# somebody depends on is four decisions nobody has taken yet. A convenience path that stopped saying so would be read as
# the recommended installation, which is the one thing it must never become — so the framing is asserted rather than
# left to whoever edits the text next.
quick_start_says_it_is_an_evaluation_rather_than_a_recommended_deployment() {
  local checkout_root
  local output_file="$test_directory/quick-start-framing-log"
  local help_file="$test_directory/quick-start-help-log"

  bash "$source_repository_root/scripts/quick-start-compose.sh" --help > "$help_file" 2>&1

  checkout_root="$(stage_quick_start_checkout 'quick-start-framing')"
  run_quick_start "$checkout_root" --provider fastmail > "$output_file" 2>&1

  assert_contains 'Not the recommended way to run one' "$help_file"
  assert_contains 'evaluate MailFathom with' "$output_file"

  local missing_decision
  for missing_decision in 'Transport' 'Credentials' 'Grants' 'Backups'; do
    assert_contains "  $missing_decision " "$output_file"
  done

  assert_contains 'users/installation.html' "$output_file"
}

# The administrative endpoint's own default port is the socket the MCP endpoint is served on, and compose.yaml
# publishes nothing for it. So enabling it means a port of its own, published from an override rather than by editing a
# tracked file — and on loopback, like every other port this deployment publishes.
quick_start_serves_the_administrative_endpoint_on_a_port_of_its_own() {
  local checkout_root compose_directory
  local off_root
  local output_file="$test_directory/quick-start-admin-log"

  checkout_root="$(stage_quick_start_checkout 'quick-start-admin')"
  compose_directory="$checkout_root/deploy/compose"

  run_quick_start "$checkout_root" --provider yahoo --admin-endpoint api-key > "$output_file" 2>&1

  assert_contains '"Port": 8090' "$compose_directory/config/10-mailfathom.json"
  assert_contains '"SecretReference": "file:/etc/mailfathom/secrets/admin-workstation-key"' \
    "$compose_directory/config/10-mailfathom.json"
  assert_contains '"127.0.0.1:8090:8090"' "$compose_directory/compose.override.yaml"

  # Off unless it is asked for, which is the product's own default and what makes the override file the marker of a
  # deliberate answer rather than of a run having happened.
  off_root="$(stage_quick_start_checkout 'quick-start-admin-off')"
  run_quick_start "$off_root" --provider yahoo > /dev/null 2>&1

  assert_excludes 'AdminEndpoint' "$off_root/deploy/compose/config/10-mailfathom.json"

  if [[ -e "$off_root/deploy/compose/compose.override.yaml" ]]; then
    printf 'An administrative endpoint nobody asked for was published.\n' >&2
    return 1
  fi
}

# The Actions policy, as far as a committed file can state it. Half of that policy lives in GitHub's
# repository settings — which action owners are allowed at all, whether a mutable `uses:` is
# accepted, how long an artifact is kept — and settings do not fail a pull request. These five
# contracts are the half that can, and they run against the real `.github/workflows/` rather than a
# fixture for the same reason the `describes:` marker contracts do: what is worth failing over is
# whether *this* repository still satisfies the policy, not whether the check works.
#
# `docs/operations/local-development.md` § "GitHub Actions policy" records the settings half and why
# each value is what it is. A change to either half reads the other.
workflow_files() {
  find "$source_repository_root/.github/workflows" -name '*.yml' | sort
}

# The owners whose actions this repository executes. Each was reviewed when the workflow that needs
# it landed and carries a row in `THIRD_PARTY_LICENSES.md`; `actions` and `github` are GitHub's own.
# Adding a name here is a supply-chain decision, so it fails here first and is argued in the pull
# request rather than discovered in a run.
every_external_action_names_an_approved_owner() {
  local approved_owners=' actions github Krzysztof318 dorny anthropics docker crate-ci aquasecurity oras-project peter-evans '
  local owner
  local unapproved=''

  while read -r owner; do
    [[ -n "$owner" ]] || continue

    if [[ "$approved_owners" != *" $owner "* ]]; then
      unapproved+="$owner "
    fi
  done < <(
    grep -rhoE '^[[:space:]]+uses:[[:space:]]+[^./][^@[:space:]]+' "$source_repository_root/.github/workflows" |
      sed -E 's#^[[:space:]]+uses:[[:space:]]+##; s#/.*##' |
      sort --unique
  )

  if [[ -n "$unapproved" ]]; then
    printf 'Workflows reference actions from unapproved owners: %s\n' "$unapproved" >&2
    return 1
  fi
}

# A job with no `permissions:` above it inherits the repository default, which is a setting rather
# than a file — so the least-privilege contract would live outside Git and change without a diff.
# Either form satisfies this: a workflow-level block covering every job, or a block on each job.
every_workflow_job_declares_its_permissions() {
  local workflow_file
  local undeclared=''

  while read -r workflow_file; do
    grep -q '^permissions:' "$workflow_file" && continue

    if ! awk '
      /^jobs:/ { in_jobs = 1; next }
      in_jobs && /^  [a-zA-Z0-9_-]+:[[:space:]]*$/ { jobs++; declared_here = 0 }
      in_jobs && /^    permissions:/ && !declared_here { declared++; declared_here = 1 }
      END { exit !(jobs > 0 && jobs == declared) }
    ' "$workflow_file"; then
      undeclared+="$(basename "$workflow_file") "
    fi
  done < <(workflow_files)

  if [[ -n "$undeclared" ]]; then
    printf 'Workflows leave a job on the repository default permissions: %s\n' "$undeclared" >&2
    return 1
  fi
}

# Every write scope in the repository, named here so a new one is a deliberate edit to this list
# rather than a line nobody reviewed. The publishing jobs need a registry write and the two an
# attestation takes; `announce` needs to write the release it announces. `release.yml` states them for
# each workflow it calls, because a caller states the permissions it hands down, and it calls two
# that need any: the image and the chart. So it carries `packages: write`, `id-token: write`, and
# `attestations: write` twice each, beside the `contents: write` its own announcing job holds. One
# scope belongs to no publishing job:
# `codeql.yml` holds `security-events: write` and runs for a pull request, which is the one exception
# to the rule the rest of this list describes. It writes code-scanning alerts and nothing else, and an
# analysis that cannot record one is a log line rather than a check.
#
# `publish-documentation.yml` publishes too, and what it publishes is a GitHub Pages deployment rather
# than a package: `pages: write` creates the deployment and `id-token: write` is what the deployment is
# claimed with. Both sit on the deploying job alone, and neither reaches the repository — the site is
# deployed as an artifact rather than pushed to a branch, which is what keeps a documentation build off
# the list of things that can write here. Nothing else writes at all.
every_write_scope_is_one_the_policy_records() {
  local recorded_scopes
  local declared_scopes

  recorded_scopes="$(
    printf '%s\n' \
      'apply-pull-request-rules.yml pull-requests: write' \
      'codeql.yml security-events: write' \
      'nightly.yml attestations: write' \
      'nightly.yml id-token: write' \
      'nightly.yml packages: write' \
      'nightly.yml packages: write' \
      'publish-container-image.yml attestations: write' \
      'publish-container-image.yml id-token: write' \
      'publish-container-image.yml packages: write' \
      'publish-documentation.yml id-token: write' \
      'publish-documentation.yml pages: write' \
      'publish-helm-chart.yml attestations: write' \
      'publish-helm-chart.yml id-token: write' \
      'publish-helm-chart.yml packages: write' \
      'release.yml attestations: write' \
      'release.yml attestations: write' \
      'release.yml contents: write' \
      'release.yml id-token: write' \
      'release.yml id-token: write' \
      'release.yml packages: write' \
      'release.yml packages: write' |
      sort
  )"

  declared_scopes="$(
    grep -rnE '^[[:space:]]+(actions|attestations|checks|contents|deployments|discussions|id-token|issues|packages|pages|pull-requests|repository-projects|security-events|statuses): write$' \
      "$source_repository_root/.github/workflows" |
      sed -E 's#^.*/([^/:]+):[0-9]+:[[:space:]]+#\1 #' |
      sort
  )"

  if [[ "$recorded_scopes" != "$declared_scopes" ]]; then
    printf 'Write scopes differ from the recorded policy.\nRecorded:\n%s\nDeclared:\n%s\n' \
      "$recorded_scopes" "$declared_scopes" >&2
    return 1
  fi
}

# A checkout that persists its credential leaves the workflow token in `.git/config` for every step
# after it, including anything a build script runs. Nothing here needs that, and a job that grows a
# reason to needs the reason written down rather than the default quietly changing under it.
every_checkout_refuses_to_persist_credentials() {
  local checkout_count
  local refusing_count

  checkout_count="$(grep -rcE '^[[:space:]]+uses:[[:space:]]+actions/checkout' "$source_repository_root/.github/workflows" |
    awk -F: '{ total += $2 } END { print total + 0 }')"

  # Counted within the seven lines a checkout step's `with:` block occupies, so a
  # `persist-credentials: false` belonging to some other step cannot stand in for a missing one.
  refusing_count="$(grep -rhA7 -E '^[[:space:]]+uses:[[:space:]]+actions/checkout' "$source_repository_root/.github/workflows" |
    grep -cE '^[[:space:]]+persist-credentials:[[:space:]]+false$')"

  if ((checkout_count == 0 || checkout_count != refusing_count)); then
    printf 'Checkout steps: %s, of which %s set persist-credentials: false.\n' \
      "$checkout_count" "$refusing_count" >&2
    return 1
  fi
}

# `actions/checkout` resolves a tag ref and then force-fetches the commit into that same ref name, so
# `refs/tags/<tag>` is left pointing straight at a commit — which is what a lightweight tag is.
# `assert-release-tag.sh` requires an annotated tag, so without the restoring fetch every correctly
# pushed release tag is rejected and nothing can publish at all. The contracts above run that script
# directly and cannot see this: a local annotated tag stays annotated, so they pass either way, which
# is why the guard has to be asserted here against the workflow instead.
the_release_restores_the_annotated_tag_before_asserting_it() {
  local workflow="$source_repository_root/.github/workflows/release.yml"
  local restore_line
  local assert_line

  restore_line="$(grep -nF 'git fetch --force origin "refs/tags/${RELEASE_TAG}:refs/tags/${RELEASE_TAG}"' \
    "$workflow" | head -n 1 | cut -d: -f1)"
  assert_line="$(grep -nF 'bash scripts/assert-release-tag.sh' "$workflow" | head -n 1 | cut -d: -f1)"

  if [[ -z "$restore_line" || -z "$assert_line" ]]; then
    printf 'release.yml restores the tag at line %s and asserts it at line %s; both are required.\n' \
      "${restore_line:-<missing>}" "${assert_line:-<missing>}" >&2
    return 1
  fi

  # Order is the whole point. A restoration after the assertion repairs a ref nothing reads again.
  if ((restore_line >= assert_line)); then
    printf 'release.yml restores the tag at line %s, which is not before the assertion at line %s.\n' \
      "$restore_line" "$assert_line" >&2
    return 1
  fi
}

# Reads a workflow's job graph as `<job> <the job it waits for>` pairs, one per line. Comment lines
# inside a `needs:` block are ignored rather than ending it, because both publishing workflows explain
# individual dependencies where they are declared.
extract_workflow_job_dependencies() {
  local workflow_file="$1"

  awk '
    /^jobs:/ { in_jobs = 1; next }
    !in_jobs { next }
    /^[^[:space:]#]/ { in_jobs = 0; next }
    /^  [a-zA-Z0-9_-]+:[[:space:]]*$/ { job = substr($1, 1, length($1) - 1); in_needs = 0; next }
    /^    needs:[[:space:]]*$/ { in_needs = 1; next }
    in_needs && /^      - / { printf "%s %s\n", job, $2; next }
    /^    [a-zA-Z0-9_-]+:/ { in_needs = 0 }
  ' "$workflow_file"
}

# The jobs that build something from the commit being published, read off the workflow rather than
# listed here: each of them hands the resolved revision down to the workflow it calls, which is what
# separates a job that consumes this commit from one that only reasons about the run. A fourth
# artifact is therefore covered by being added rather than by this list being remembered.
extract_workflow_jobs_consuming_the_commit() {
  local workflow_file="$1"

  awk '
    /^jobs:/ { in_jobs = 1; next }
    !in_jobs { next }
    /^[^[:space:]#]/ { in_jobs = 0; next }
    /^  [a-zA-Z0-9_-]+:[[:space:]]*$/ { job = substr($1, 1, length($1) - 1); next }
    /^      ref:[[:space:]]+\$\{\{[[:space:]]*needs\.[a-zA-Z0-9_-]+\.outputs\.revision[[:space:]]*\}\}$/ { print job }
  ' "$workflow_file"
}

# The reusable workflow a job calls, which is what makes the job names below mean something: a `verify`
# job that stopped calling the verification workflow would satisfy every dependency and gate nothing.
extract_workflow_job_uses() {
  local workflow_file="$1"
  local job="$2"

  awk -v job_header="  ${job}:" '
    $0 == job_header { in_job = 1; next }
    in_job && /^  [a-zA-Z0-9_-]+:[[:space:]]*$/ { exit }
    in_job && /^    uses:[[:space:]]/ { print $2; exit }
  ' "$workflow_file"
}

# Whether a job waits for another, directly or through anything in between. Transitive rather than
# direct, because a publishing job reaching the gate through the artifact it already waits for is
# gated just as firmly as one naming it.
workflow_job_waits_for() {
  local dependencies="$1"
  local job="$2"
  local awaited="$3"
  local reached=" $job "
  local grew=1
  local dependent
  local dependency

  while ((grew)); do
    grew=0

    while read -r dependent dependency; do
      [[ -n "$dependent" ]] || continue

      if [[ "$reached" == *" $dependent "* && "$reached" != *" $dependency "* ]]; then
        reached+="$dependency "
        grew=1
      fi
    done <<< "$dependencies"
  done

  [[ "$reached" == *" $awaited "* ]]
}

# Every artifact a channel publishes is built from a commit that has verified, and the dependency
# saying so lives in the workflow that publishes rather than inside the reusable workflow one of the
# artifacts happens to call. A gate belonging to the image would gate the image: `Schema artifact` and
# `CLI binaries` would start beside it from a commit whose unit tests had not run, so a release would
# spend four `dotnet publish` invocations and a schema generation before the failure was legible and
# would start them minutes before the integration suite had said anything. A job graph makes none of
# that visible on a green run, which is why a fourth artifact is gated by this assertion rather than
# by whoever reviews it.
no_channel_builds_an_artifact_before_the_commit_has_verified() {
  local workflow_directory="$source_repository_root/.github/workflows"
  local release_dependencies
  local nightly_dependencies
  local release_consumers
  local nightly_consumers
  local job
  local failures=''

  release_dependencies="$(extract_workflow_job_dependencies "$workflow_directory/release.yml")"
  nightly_dependencies="$(extract_workflow_job_dependencies "$workflow_directory/nightly.yml")"
  release_consumers=" $(extract_workflow_jobs_consuming_the_commit "$workflow_directory/release.yml" | tr '\n' ' ')"
  nightly_consumers=" $(extract_workflow_jobs_consuming_the_commit "$workflow_directory/nightly.yml" | tr '\n' ' ')"

  # A gate is what the rest waits for, so it is what the rest is measured against rather than
  # something to measure. There are four, one per stack plus the suite that starts a container and the
  # contracts neither stack's build can see, and `verify-client` is a gate rather than an artifact for
  # a reason worth stating: making it wait for the server's would leave a commit that breaks both
  # reporting only the server's, and would put a two-minute client build behind an integration suite it
  # shares nothing with. `verify-contracts` is beside them on the same argument, and more plainly: it
  # is twenty seconds that install nothing, and behind any of the other three it would report minutes
  # after it had the answer. Everything else that reads the commit is checked below whatever it is
  # named.
  local verification_jobs=' verify verify-client verify-contracts integration-tests '

  for job in $release_consumers; do
    [[ "$verification_jobs" == *" $job "* ]] && continue

    workflow_job_waits_for "$release_dependencies" "$job" verify ||
      failures+="release.yml: ${job} does not wait for verify. "
    workflow_job_waits_for "$release_dependencies" "$job" integration-tests ||
      failures+="release.yml: ${job} does not wait for integration-tests. "
  done

  for job in $nightly_consumers; do
    [[ "$verification_jobs" == *" $job "* ]] && continue

    workflow_job_waits_for "$nightly_dependencies" "$job" verify ||
      failures+="nightly.yml: ${job} does not wait for verify. "
  done

  # A derived list that derives nothing asserts nothing, and it would do so silently: the loops above
  # are vacuous the moment the `ref:` expression they read is spelled some other way. These three are
  # the floor rather than the coverage.
  for job in schema-artifact cli-binaries desktop-client publish; do
    [[ "$release_consumers" == *" $job "* ]] ||
      failures+="release.yml: ${job} was not recognized as building from the released commit. "
    [[ "$nightly_consumers" == *" $job "* ]] ||
      failures+="nightly.yml: ${job} was not recognized as building from the previewed commit. "
  done

  [[ "$(extract_workflow_job_uses "$workflow_directory/release.yml" verify)" == './.github/workflows/build-test-format-and-migrations.yml' ]] ||
    failures+='release.yml: verify does not call build-test-format-and-migrations.yml. '
  [[ "$(extract_workflow_job_uses "$workflow_directory/release.yml" integration-tests)" == './.github/workflows/integration-tests.yml' ]] ||
    failures+='release.yml: integration-tests does not call integration-tests.yml. '
  [[ "$(extract_workflow_job_uses "$workflow_directory/nightly.yml" verify)" == './.github/workflows/build-test-format-and-migrations.yml' ]] ||
    failures+='nightly.yml: verify does not call build-test-format-and-migrations.yml. '

  # The client's gate is exempted above by name, so what that name means is asserted here rather than
  # trusted: a `verify-client` job that stopped calling the client's build would be a gate skipped by
  # the loop and gating nothing, which is the one way the exemption could go wrong quietly.
  [[ "$(extract_workflow_job_uses "$workflow_directory/release.yml" verify-client)" == './.github/workflows/build-test-frontend.yml' ]] ||
    failures+='release.yml: verify-client does not call build-test-frontend.yml. '
  [[ "$(extract_workflow_job_uses "$workflow_directory/nightly.yml" verify-client)" == './.github/workflows/build-test-frontend.yml' ]] ||
    failures+='nightly.yml: verify-client does not call build-test-frontend.yml. '

  # The same reasoning for the contract gate, which is exempted by name above like the two before it.
  [[ "$(extract_workflow_job_uses "$workflow_directory/release.yml" verify-contracts)" == './.github/workflows/repository-contracts.yml' ]] ||
    failures+='release.yml: verify-contracts does not call repository-contracts.yml. '
  [[ "$(extract_workflow_job_uses "$workflow_directory/nightly.yml" verify-contracts)" == './.github/workflows/repository-contracts.yml' ]] ||
    failures+='nightly.yml: verify-contracts does not call repository-contracts.yml. '

  # A gate producing no artifact is reached by nothing above: the loops measure the jobs that consume the commit, and a
  # publishing job inherits a dependency by waiting for what it builds from. So what a release blocks on is asserted
  # here instead. The two channels differ, and deliberately: a nightly publishes a server image and holds neither the
  # client nor the chart in it, so it runs both gates and blocks on the one whose stack the image carries, while a
  # release is one claim about one commit and blocks on both.
  for job in verify-client verify-contracts; do
    workflow_job_waits_for "$release_dependencies" publish "$job" ||
      failures+="release.yml: publish does not wait for ${job}. "
  done

  workflow_job_waits_for "$nightly_dependencies" publish verify-client ||
    failures+='nightly.yml: publish does not wait for verify-client. '

  # The gate has one home. A copy of it back inside the publishing workflow would run the whole thing
  # twice per publication while gating one artifact of the three.
  if grep -qE '^[[:space:]]+uses:[[:space:]]+\./\.github/workflows/(build-test-format-and-migrations|integration-tests)\.yml$' \
    "$workflow_directory/publish-container-image.yml"; then
    failures+='publish-container-image.yml calls a verification workflow its callers already run. '
  fi

  if [[ -n "$failures" ]]; then
    printf 'Publication is not gated as recorded: %s\n' "$failures" >&2
    return 1
  fi
}

# The mutation score answers what coverage stopped answering, and it is worth that only while nobody
# has to reach it. Every way it could quietly become a gate is checked here rather than left to
# whoever next edits one of the three files it lives in: the runtime is tens of minutes, the test
# runner it needs is preview, and a score somebody must raise stops being evidence about the suite and
# becomes a number. `docs/operations/agent-workflow.md` § *The mutation score is read, never enforced*
# carries the reasoning.
the_mutation_score_is_reported_and_never_gated() {
  local workflow_directory="$source_repository_root/.github/workflows"
  local script="$source_repository_root/scripts/mutation-score.sh"
  local failures=''
  local offenders
  local measured

  [[ "$(extract_workflow_job_uses "$workflow_directory/nightly.yml" mutation-score)" == './.github/workflows/mutation-score.yml' ]] ||
    failures+='nightly.yml: mutation-score does not call mutation-score.yml. '

  # The one flag deciding whether a score can fail a run. Stryker already defaults it to 0, which is
  # exactly why the explicit value is asserted: a default nobody wrote down is not a decision anybody
  # reads, and it moves when the tool moves.
  grep -qE '^[[:space:]]+--break-at 0( \\)?$' "$script" ||
    failures+='scripts/mutation-score.sh does not pass --break-at 0, so the score can decide an exit status. '

  # The flag covers the score and nothing else, so everything the run can still fail on is covered here instead — a
  # preview test runner that crashes, a report that is never written. The hot-path benchmarks one job above are swallowed
  # the same way and for the reason `backend/tests/AGENTS.md` § *Cost claims* gives.
  grep -qE '^    continue-on-error: true$' "$workflow_directory/mutation-score.yml" ||
    failures+='mutation-score.yml does not swallow its own failures, so a broken diagnostic reads as a red nightly. '

  offenders="$(grep -l 'mutation-score' \
    "$source_repository_root/scripts/verify-fast.sh" \
    "$source_repository_root/scripts/verify-full.sh" || true)"

  if [[ -n "$offenders" ]]; then
    failures+="these verification scripts reach the mutation score: $(tr '\n' ' ' <<< "$offenders"). "
  fi

  # The nightly and the workflow it calls, and nothing else. A pull request reaching this would put
  # the runtime and the preview runner's false positives in front of a merge.
  offenders="$(grep -rl 'mutation-score' "$workflow_directory" |
    grep -vE '/(nightly|mutation-score)\.yml$' || true)"

  if [[ -n "$offenders" ]]; then
    failures+="these workflows reach the mutation score outside the nightly channel: $(tr '\n' ' ' <<< "$offenders"). "
  fi

  # Two projects, because a surviving mutant names a missing assertion in the invariants and the use
  # cases and an adapter detail nobody would act on anywhere else.
  measured="$(awk '
    /^        project:$/ { in_matrix = 1; next }
    in_matrix && /^          - / { printf "%s ", $2; next }
    in_matrix { exit }
  ' "$workflow_directory/mutation-score.yml")"

  [[ "$measured" == 'Domain Application ' ]] ||
    failures+="mutation-score.yml measures '${measured}' rather than Domain and Application. "

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

# Calling an AI provider is what costs MailFathom money per unit of mail, and ADR 0006 makes a paid
# call never the default — in the running service and in verification alike. One switch covers every
# provider the suite reaches, embeddings and chat alike, so a provider added later cannot arrive with
# a gate of its own that nobody turns off. Three properties carry that in the pipeline, and none of
# them is visible in a diff that only reads the input's name: the dispatch input defaults to off, the
# environment falls back to `false` when no input supplied a value, and `workflow_call` declares no
# such input at all. The third is what keeps a release from ever spending provider credit, because
# `release.yml` reaches this suite through that trigger.
a_paid_provider_run_is_never_the_default() {
  local workflow="$source_repository_root/.github/workflows/integration-tests.yml"
  local dispatch_inputs
  local call_inputs
  local failures=''

  # Each half of `on:` read on its own, because the two triggers differ deliberately and a check over
  # the whole file could not tell an input declared for dispatch from one declared for a caller.
  dispatch_inputs="$(sed -n '/^  workflow_dispatch:/,/^  workflow_call:/p' "$workflow")"
  call_inputs="$(sed -n '/^  workflow_call:/,/^concurrency:/p' "$workflow")"

  if [[ "$dispatch_inputs" != *'run_ai_provider_contract_tests:'* ]]; then
    failures+='workflow_dispatch declares no run_ai_provider_contract_tests input. '
  elif ! printf '%s' "$dispatch_inputs" |
    sed -n '/run_ai_provider_contract_tests:/,/type:/p' | grep -qE '^[[:space:]]*default:[[:space:]]*false[[:space:]]*$'; then
    failures+='run_ai_provider_contract_tests does not default to false, so a dispatch spends provider credit unless the operator turns it off. '
  fi

  if [[ "$call_inputs" == *'run_ai_provider_contract_tests:'* ]]; then
    failures+='workflow_call declares run_ai_provider_contract_tests, so a calling workflow — release.yml among them — can spend provider credit. '
  fi

  if ! grep -qF "MAILFATHOM_AI_CONTRACT_TESTS: \${{ inputs.run_ai_provider_contract_tests || 'false' }}" "$workflow"; then
    failures+='MAILFATHOM_AI_CONTRACT_TESTS does not fall back to false, so a trigger that supplies no input leaves it unset rather than off. '
  fi

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

# `pull_request_target` runs the base branch's workflow with the repository's secrets against a
# contribution nobody has reviewed. Two workflows use it deliberately and
# `docs/operations/agent-workflow.md` argues each case separately: `fathom-review.yml` since #189,
# which checks out `base.sha`, executes nothing from the contribution, and starts on a maintainer's
# act alone; and `contributor-licence.yml` since #1077, which needs a write-capable token on a
# fork's pull request to publish `license/cla` and to record an acceptance. A third would be neither,
# and this is what stops it appearing unnoticed.
#
# The licence workflow's whole argument is that nothing from the contribution is ever executed, so
# the three assertions below carry that property rather than leaving it to the prose: it checks
# nothing out, the one action it reaches is the token mint, and no step of it runs a command that
# fetches content. Each would be the first step of handing the App's token to whoever opened the
# pull request, and the third is the one an added `uses:` line would not announce — a `run:` step
# shelling out to `git`, `curl`, `wget`, or `gh` against the fork introduces no action reference at
# all, so the first two assertions would both pass over it. The pattern reads the whole file with
# its comment lines removed, which is why the prose here names those commands rather than using
# them: a comment writing `git fetch` as an example would fail the test it is explaining.
only_the_recorded_workflows_use_pull_request_target() {
  local using_workflows
  local licence_workflow="$source_repository_root/.github/workflows/contributor-licence.yml"
  local reached_actions
  local fetching_commands
  local failures=''

  using_workflows="$(grep -rlE '^[[:space:]]*pull_request_target:' "$source_repository_root/.github/workflows" |
    xargs -r -n1 basename | sort | tr '\n' ' ')"

  if [[ "$using_workflows" != 'contributor-licence.yml fathom-review.yml ' ]]; then
    printf 'pull_request_target is used by: %s(expected contributor-licence.yml and fathom-review.yml alone)\n' \
      "$using_workflows" >&2
    return 1
  fi

  if grep -qE '^[[:space:]]*uses:[[:space:]]*actions/checkout' "$licence_workflow"; then
    failures+='contributor-licence.yml checks something out, so a contributed tree reaches a job holding the App token. '
  fi

  reached_actions="$(sed -nE 's|^[[:space:]]*uses:[[:space:]]*([^@[:space:]]+).*|\1|p' "$licence_workflow" |
    sort -u | tr '\n' ' ')"

  if [[ "$reached_actions" != 'actions/create-github-app-token ' ]]; then
    failures+="contributor-licence.yml reaches actions beyond the token mint: $reached_actions "
  fi

  fetching_commands="$(grep -vE '^[[:space:]]*#' "$licence_workflow" |
    grep -nE "(^|[^[:alnum:]_/-])(git|curl|wget)[[:space:]]|gh[[:space:]]+pr[[:space:]]+checkout|gh[[:space:]]+repo[[:space:]]+clone|/(tar|zip)ball" |
    tr '\n' ' ' || true)"

  if [[ -n "$fetching_commands" ]]; then
    failures+="contributor-licence.yml runs a command that fetches content, so a contributed tree can reach a job holding the App token: $fetching_commands"
  fi

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

# `issue_comment` fires for every comment on a pull request, a bot's included, and the gate that
# tells a review request apart from any other comment runs *inside* the run — after the run has
# already entered the concurrency group. An unconditional `cancel-in-progress: true` therefore means
# a comment ends the review already running and then declines to start one, spending the whole cost
# of a review to publish nothing. Only the two events that stop the running review from describing
# the head worth a verdict may cancel: a push replaces that head, and a close removes it.
a_comment_never_cancels_a_review_in_flight() {
  local reviewer_workflow="$source_repository_root/.github/workflows/fathom-review.yml"
  local cancel_in_progress
  local required_term

  cancel_in_progress="$(sed -nE 's/^  cancel-in-progress:[[:space:]]*(.*)$/\1/p' "$reviewer_workflow")"

  if [[ -z "$cancel_in_progress" ]]; then
    printf 'fathom-review.yml declares no cancel-in-progress, so a superseded head finishes its review\n' >&2
    return 1
  fi

  if [[ "$cancel_in_progress" == 'true' ]]; then
    printf 'fathom-review.yml cancels unconditionally, so any comment on a pull request ends the review in flight\n' >&2
    return 1
  fi

  for required_term in "github.event_name == 'pull_request_target'" 'synchronize' 'closed'; do
    if [[ "$cancel_in_progress" != *"$required_term"* ]]; then
      printf 'fathom-review.yml cancel-in-progress does not name %s, so it no longer cancels exactly the two events that replace or remove the head\n' \
        "$required_term" >&2
      return 1
    fi
  done
}

# The reviewer's subscription credential is named in five places: the workflow-level `env` that
# carries the secret's *name* into the annotation reporting a missing one, the two action inputs
# that spend it — a reader's and the judge's — and the two leak checks that refuse to publish
# findings containing it. Declaring the secret once would also hand it to every step that has no
# business holding it, so the five spellings are separate by design and this is what keeps them
# agreeing.
#
# The failure they can otherwise produce is silent in the worst way. A leak check comparing a review
# against the token the run never spent matches nothing, passes every review it is given, and is
# green while doing it — so the one step that stands between a credential and a published review
# stops standing there with nothing to say so.
the_reviewer_resolves_one_claude_credential_everywhere() {
  local reviewer_workflow="$source_repository_root/.github/workflows/fathom-review.yml"
  local selector="vars.CLAUDE_CODE_PROFILE == 'secondary'"
  local value_expression="\${{ ${selector} && secrets.CLAUDE_CODE_OAUTH_TOKEN_SECONDARY || secrets.CLAUDE_CODE_OAUTH_TOKEN }}"
  local name_expression="\${{ ${selector} && 'CLAUDE_CODE_OAUTH_TOKEN_SECONDARY' || 'CLAUDE_CODE_OAUTH_TOKEN' }}"
  local selecting_reads
  local unselected_reads

  selecting_reads="$(grep -cF "$value_expression" "$reviewer_workflow" || true)"

  # Four: the reader action's input, the judge action's input, and the two leak checks that refuse
  # to print or publish anything matching the credential the run actually spent. The readers made it
  # four — a session per group is a second holder of the same secret — and the number is asserted
  # rather than left open because a fifth would be a step nobody argued holding it.
  if [[ "$selecting_reads" != '4' ]]; then
    printf 'fathom-review.yml resolves the Claude credential through the profile in %s place(s), expected 4: both action inputs and both leak checks\n' \
      "$selecting_reads" >&2
    return 1
  fi

  # Every other read of a Claude secret, whatever it is spelled as. A step reaching one without the
  # selector is either the profile being ignored or a fifth holder of the credential, and both are
  # this contract's subject. A comment naming the secrets context is not: the file argues this
  # arrangement at length, and a contract that failed on the argument would be one nobody could
  # explain in the file it guards.
  unselected_reads="$(grep -nF 'secrets.CLAUDE_CODE_OAUTH_TOKEN' "$reviewer_workflow" |
    grep -vE '^[0-9]+:[[:space:]]*#' |
    grep -vF "$value_expression" || true)"

  if [[ -n "$unselected_reads" ]]; then
    printf 'fathom-review.yml reads a Claude credential without selecting it by profile:\n%s\n' \
      "$unselected_reads" >&2
    return 1
  fi

  if ! grep -qF "CLAUDE_CREDENTIAL_SECRET: ${name_expression}" "$reviewer_workflow"; then
    printf 'fathom-review.yml does not name the selected secret in CLAUDE_CREDENTIAL_SECRET, so a run missing one reports the other\n' >&2
    return 1
  fi
}

# `backend/tools/` holds development tooling that must never ship. `backend/tools/SyntheticMail`
# fabricates mail and submits it under a stored credential, which is not an operator capability and has
# no business in `mfctl`, in the container image, or in a release asset. Being outside `backend/src/` is
# what makes that true today — the release publishes `backend/src/Cli/Cli.csproj` by name and the image's
# build context is an allow-list — and this is what keeps it true: three ways the boundary could be
# crossed, each checked rather than trusted to a convention nobody restates.
the_development_tooling_never_reaches_a_published_artifact() {
  local failures=''
  local offenders

  # A project under backend/src/ referencing one under backend/tools/ would put the tool in whatever that
  # project publishes, image and command binaries alike.
  offenders="$(grep -rlE 'ProjectReference[^>]*tools[/\\]' "$source_repository_root/backend/src" || true)"

  if [[ -n "$offenders" ]]; then
    failures+="these projects under backend/src/ reference backend/tools/: $(tr '\n' ' ' <<< "$offenders"). "
  fi

  # A workflow *step* naming a path under backend/tools/ would build, publish, or attach it as part of a
  # channel. A `paths-filter` entry is the one legitimate mention and reads as a quoted glob on a
  # list item of its own: it decides which jobs a change starts, which is the opposite of publishing.
  offenders="$(grep -rn 'tools/' "$source_repository_root/.github/workflows" |
    grep -vE ":[[:space:]]*-[[:space:]]*'backend/tools/\*\*'$" |
    grep -vE ":[[:space:]]*#" || true)"

  if [[ -n "$offenders" ]]; then
    failures+="these workflow lines name a path under backend/tools/ outside a change filter: $(tr '\n' ' ' <<< "$offenders"). "
  fi

  # The image's build context is an allow-list, so the tool reaches it only if a line says so.
  if grep -qE '^!/backend/tools' "$source_repository_root/deploy/docker/Dockerfile.dockerignore"; then
    failures+='deploy/docker/Dockerfile.dockerignore admits backend/tools/ into the container build context. '
  fi

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

# `Required CI` is the one check the `main` ruleset waits for, so a job of that workflow it does not
# depend on and does not read is a job whose failure merges. Both halves are asserted: a `needs:`
# entry is what makes the job run before the aggregate, and a `needs.<job>.result` reference is what
# makes its conclusion part of the verdict — a dependency listed and never read reports green while
# the job that failed sits red beside it.
the_required_check_aggregates_every_job_in_ci() {
  local ci_workflow="$source_repository_root/.github/workflows/ci.yml"
  local declared_jobs aggregated_jobs job unaggregated='' unread=''

  declared_jobs="$(awk '
    /^jobs:/ { in_jobs = 1; next }
    in_jobs && /^  [a-zA-Z0-9_-]+:[[:space:]]*$/ { name = $1; sub(/:$/, "", name); print name }
  ' "$ci_workflow")"

  aggregated_jobs="$(awk '
    /^  required-ci:[[:space:]]*$/ { in_aggregate = 1; next }
    in_aggregate && /^  [a-zA-Z0-9_-]+:[[:space:]]*$/ { exit }
    in_aggregate && /^    needs:[[:space:]]*$/ { in_needs = 1; next }
    in_needs && /^      - / { print $2; next }
    in_needs { in_needs = 0 }
  ' "$ci_workflow")"

  while read -r job; do
    [[ -n "$job" && "$job" != 'required-ci' ]] || continue

    grep -qx -- "$job" <<< "$aggregated_jobs" || unaggregated+="$job "
    grep -qF -- "needs.$job.result" "$ci_workflow" || unread+="$job "
  done <<< "$declared_jobs"

  if [[ -n "$unaggregated" || -n "$unread" ]]; then
    [[ -n "$unaggregated" ]] && printf 'required-ci does not depend on these ci.yml jobs: %s\n' "$unaggregated" >&2
    [[ -n "$unread" ]] && printf 'required-ci never reads the result of these ci.yml jobs: %s\n' "$unread" >&2
    return 1
  fi
}

# The two stacks share the required check and nothing else, and the change filters are where that either holds or
# quietly stops holding. A `frontend/` path in one of the server's filters would make every client change pay for a
# server build, and a `backend/` path in the client's would do the reverse — neither fails anything, so neither is
# visible in a run, and both are exactly the edit somebody makes while adding a path they were unsure where to put.
# The disjointness is asserted rather than the whole list, because which paths belong to a stack is a decision the
# workflow's own comments carry; what may never happen is one stack's directory appearing in the other's filter.
# One `<filter>\t<path>` line per path `ci.yml`'s `filters:` block declares, with the block's own comment lines and
# everything after it dropped. Inside the block literal a filter name sits at twelve spaces and a path at fourteen,
# which is what tells the two apart; the first line indented less than that is the end of the literal.
list_ci_change_filters() {
  awk '
    /^          filters: \|$/ { in_filters = 1; next }
    in_filters && /^[[:space:]]*$/ { next }
    in_filters && !/^            / { exit }
    in_filters && /^            [a-zA-Z0-9_-]+:[[:space:]]*$/ { name = $1; sub(/:$/, "", name); next }
    in_filters && /^              - / {
      path = $2
      gsub(/^.|.$/, "", path)
      print name "\t" path
    }
  ' "$1"
}

the_stacks_change_filters_name_no_path_in_each_other() {
  local ci_workflow="$source_repository_root/.github/workflows/ci.yml"
  local filters filter_name path failures=''

  filters="$(list_ci_change_filters "$ci_workflow")"

  [[ -n "$filters" ]] || {
    printf 'ci.yml declares no change filters, or the block moved.\n' >&2
    return 1
  }

  while IFS=$'\t' read -r filter_name path; do
    [[ -n "$filter_name" ]] || continue

    if [[ "$filter_name" == 'frontend' && "$path" == backend/* ]]; then
      failures+="the frontend filter names $path. "
    fi

    if [[ "$filter_name" != 'frontend' && "$path" == frontend/* ]]; then
      failures+="the $filter_name filter names $path. "
    fi
  done <<< "$filters"

  grep -qxF $'frontend\tfrontend/**' <<< "$filters" ||
    failures+='ci.yml has no frontend filter covering frontend/**. '

  [[ "$(extract_workflow_job_uses "$ci_workflow" frontend)" == './.github/workflows/build-test-frontend.yml' ]] ||
    failures+='ci.yml: the frontend job does not call build-test-frontend.yml. '

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

# The pipeline and the two local gates answer *which stack does this change reach* from the same
# lists, and they hold those lists in two files because neither can read the other's: a shell script
# cannot ask `dorny/paths-filter` what it would match, and a workflow cannot source a script. What
# keeps the copies one decision is this contract. Without it the drift is silent in the direction
# that matters most — a path added to `ci.yml` alone leaves the local gate skipping a stack the
# pipeline is about to build, so the branch passes locally and fails minutes later on a file the
# developer had in front of them.
the_gates_decide_from_the_change_filters_ci_declares() {
  local ci_workflow="$source_repository_root/.github/workflows/ci.yml"
  local filters pairing stack_name ci_filter_name failures=''
  local ci_paths script_paths

  filters="$(list_ci_change_filters "$ci_workflow")"

  [[ -n "$filters" ]] || {
    printf 'ci.yml declares no change filters, or the block moved.\n' >&2
    return 1
  }

  # Each stack's list beside the `ci.yml` filter it is the copy of. The two names differ because the
  # workflow names the server's filter after what it gates rather than after the stack.
  for pairing in 'service build' 'client frontend'; do
    read -r stack_name ci_filter_name <<< "$pairing"
    ci_paths="$(awk -F'\t' -v name="$ci_filter_name" '$1 == name { print $2 }' <<< "$filters" | sort)"

    # Sourced in a subshell of its own, so the arrays this asserts on cannot leak into the suite.
    script_paths="$(
      # shellcheck source=scripts/resolve-changed-stacks.sh
      source "$source_repository_root/scripts/resolve-changed-stacks.sh"

      case "$stack_name" in
        service) printf '%s\n' "${service_stack_filter[@]}" ;;
        client) printf '%s\n' "${client_stack_filter[@]}" ;;
      esac
    )"
    script_paths="$(sort <<< "$script_paths")"

    if [[ "$ci_paths" != "$script_paths" ]]; then
      failures+="the $stack_name stack filter in scripts/resolve-changed-stacks.sh disagrees with ci.yml's $ci_filter_name filter: $(diff <(printf '%s\n' "$ci_paths") <(printf '%s\n' "$script_paths") | tr '\n' ' '). "
    fi
  done

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

workflow_scripts_use_flat_manual_layout() {
  [[ -x "$source_repository_root/scripts/inspect-workspace.sh" ]]
  [[ -x "$source_repository_root/scripts/assert-release-tag.sh" ]]
  [[ -x "$source_repository_root/scripts/read-changelog-section.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-fast.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-full.sh" ]]
  [[ -x "$source_repository_root/scripts/test-agent-workflow.sh" ]]
  [[ -x "$source_repository_root/scripts/review-obligations.sh" ]]
  [[ ! -e "$source_repository_root/eng/agent-workflow" ]]

  # `Fathom review` invokes this one directly rather than through `bash`, so the mode git records is
  # part of the contract. The tests above run it through `bash` and would pass without it.
  [[ -x "$source_repository_root/.github/fathom-review/index-obligations.sh" ]]
  [[ -x "$source_repository_root/.github/fathom-review/group-changed-files.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request/collect-closing-issues.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request/write-board-status.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request/select-labels.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request/collect-referenced-issues.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request/call-github-api.sh" ]]
}

# `scripts/update-dependencies.sh` surveys by default and writes only when it is told to, and the
# three refusals below are what keeps that true from the outside. `--verify` runs the full gate, and
# a gate is worth something only over a tree the run that started it changed — asking for one after a
# read-only survey would prove the branch as it already stood and report it as the update having been
# verified. The unknown-argument refusals are the other half of the same property: a mistyped flag
# that fell through to the default would be a survey silently doing something else.
#
# Nothing here reaches the network, which is what makes it a contract rather than a run: every path
# asserted below is decided before the first pin is read.
# `rewrite_pin` is the only thing that writes a tracked pin file, it does so with `sed`, and the values it splices in
# come from a remote server — which is exactly where an escaping defect already reached this script once. So the test
# runs the real function text against fixtures rather than asserting anything about the survey around it: it sources
# the three functions out of the script and gives them a temporary tree, which needs no network and no repository.
#
# Extracting by name is deliberate. A rename or a reshaped body fails this loudly, which is the right answer for a
# function nothing else covers, and `source` rather than `eval` is what keeps the body from being expanded at the
# moment it is defined.
the_dependency_rewrite_encodes_a_hostile_version_into_a_pin_file() {
  local script="$source_repository_root/scripts/update-dependencies.sh"
  local fixture="$test_directory/dependency-rewrite"
  # Every character the replacement side of a `sed` script gives its own meaning: the delimiter, the whole-match
  # reference, and a backslash. A version this shape is not one nuget.org would publish; the point is that the writer
  # never has to know that.
  local hostile='9.9.9-rc.1&#\x'
  local outcome

  rm -rf "$fixture"
  mkdir -p "$fixture/workflows"

  cat > "$fixture/Directory.Packages.props" << 'PROPS'
<Project>
  <ItemGroup>
    <PackageVersion Include="Some.Package" Version="1.0.0" />
    <PackageVersion Include="Some.Package.Extras" Version="1.0.0" />
  </ItemGroup>
</Project>
PROPS

  cat > "$fixture/workflows/ci.yml" << 'WORKFLOW'
jobs:
  build:
    steps:
      - uses: owner/action@v1
      - uses: owner/action@v1 # pinned for a reason this fixture does not care about
      - uses: owner/action-extras@v1
WORKFLOW

  outcome="$(
    set -uo pipefail
    source <(
      sed -n \
        -e '/^as_pattern() {$/,/^}$/p' \
        -e '/^as_replacement() {$/,/^}$/p' \
        -e '/^rewrite_pin() {$/,/^}$/p' \
        "$script"
    )

    work_directory="$fixture"
    workflow_directory="$fixture/workflows"

    rewrite_pin nuget 'Some.Package' '1.0.0' "$hostile" "$fixture/Directory.Packages.props" \
      && printf 'nuget-moved\n'
    rewrite_pin actions 'owner/action@v1' 'v1' "$hostile" '' \
      && printf 'actions-moved\n'
    # The same rewrite a second time changes nothing, and the answer has to be that nothing moved rather than that
    # `sed` opened the file.
    rewrite_pin nuget 'Some.Package' '1.0.0' "$hostile" "$fixture/Directory.Packages.props" \
      || printf 'nuget-refused-a-second-time\n'
  )"

  local expected='nuget-moved
actions-moved
nuget-refused-a-second-time'

  if [[ "$outcome" != "$expected" ]]; then
    printf 'rewrite_pin reported %q where %q was expected\n' "$outcome" "$expected" >&2
    return 1
  fi

  if ! grep -qF "<PackageVersion Include=\"Some.Package\" Version=\"$hostile\" />" "$fixture/Directory.Packages.props"; then
    printf 'the package version was not written back verbatim:\n%s\n' \
      "$(cat "$fixture/Directory.Packages.props")" >&2
    return 1
  fi

  # The longer identifier shares a prefix with the shorter one, so a pattern that did not close on the quote would
  # take both.
  if ! grep -qF '<PackageVersion Include="Some.Package.Extras" Version="1.0.0" />' "$fixture/Directory.Packages.props"; then
    printf 'a package sharing the rewritten one'"'"'s prefix was rewritten too:\n%s\n' \
      "$(cat "$fixture/Directory.Packages.props")" >&2
    return 1
  fi

  if [[ "$(grep -cF "uses: owner/action@$hostile" "$fixture/workflows/ci.yml")" != '2' ]]; then
    printf 'both references were expected to move, including the one carrying a trailing comment:\n%s\n' \
      "$(cat "$fixture/workflows/ci.yml")" >&2
    return 1
  fi

  if ! grep -qF '# pinned for a reason this fixture does not care about' "$fixture/workflows/ci.yml"; then
    printf 'the trailing comment did not survive the rewrite:\n%s\n' \
      "$(cat "$fixture/workflows/ci.yml")" >&2
    return 1
  fi

  if ! grep -qF 'uses: owner/action-extras@v1' "$fixture/workflows/ci.yml"; then
    printf 'an action sharing the rewritten one'"'"'s prefix was rewritten too:\n%s\n' \
      "$(cat "$fixture/workflows/ci.yml")" >&2
    return 1
  fi
}

the_dependency_survey_refuses_what_would_write_without_being_asked() {
  local script="$source_repository_root/scripts/update-dependencies.sh"

  [[ -x "$script" ]] || {
    printf 'scripts/update-dependencies.sh is not executable\n' >&2
    return 1
  }

  if "$script" --verify > /dev/null 2>&1; then
    printf 'update-dependencies.sh accepted --verify with no --apply beside it\n' >&2
    return 1
  fi

  if "$script" --only nothing > /dev/null 2>&1; then
    printf 'update-dependencies.sh accepted an unknown family\n' >&2
    return 1
  fi

  if "$script" --rewrite-everything > /dev/null 2>&1; then
    printf 'update-dependencies.sh accepted an unknown argument\n' >&2
    return 1
  fi

  "$script" --help > /dev/null
}

# The per-file licensing mark, everywhere the analyzer that applies it cannot reach. IDE0073 reads
# C# and rewrites a `.cs` header to match `file_header_template`, so the source files stay consistent
# without anyone typing one; the workflows, the scripts, the chart, the unit sources, and the skills
# get no such repair, and a file added to any of them would travel out of this repository stating
# neither who owns it nor what terms it arrives under. These four cases are that missing analyzer.
#
# The expected text is read from `.editorconfig` rather than restated here, which is what keeps the
# three forms one header: an edit to the template that leaves these files behind fails as a
# disagreement instead of quietly splitting the mark in two.
#
# They read `git ls-files` against the real repository rather than the filesystem, so the fixture
# checkouts this suite builds under `$test_directory` neither fail them nor satisfy them.
license_header_lines() {
  sed -n 's/^file_header_template = //p' "$source_repository_root/.editorconfig" | sed 's/\\n/\n/g'
}

comment_license_header() {
  license_header_lines | sed 's/^/# /'
}

# A `#` line in a Helm template is emitted into the rendered manifest, which would put the header
# into every Kubernetes object the chart applies in somebody else's cluster. The template comment
# states the same three lines about the source file and renders to nothing.
template_license_header() {
  printf '{{- /*\n%s\n*/ -}}\n' "$(license_header_lines)"
}

every_yaml_file_carries_the_license_header() {
  local file expected actual failures=0

  while IFS= read -r file; do
    if [[ "$file" == deploy/helm/mailfathom/templates/* ]]; then
      expected="$(template_license_header)"
      actual="$(head -n 5 "$source_repository_root/$file")"
    else
      expected="$(comment_license_header)"
      actual="$(head -n 3 "$source_repository_root/$file")"
    fi

    if [[ "$actual" != "$expected" ]]; then
      printf '%s does not open with the license header\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- \
    '*.yml' '*.yaml' 'deploy/helm/mailfathom/templates/*.tpl')

  (( failures == 0 ))
}

# The documentation site's template is the fourth place the analyzer cannot reach, and it carries two
# more forms of the same three lines. A module is JavaScript, where `//` opens a comment; a stylesheet
# has no line-comment syntax at all, so CSS's one block form is what carries it there.
module_license_header() {
  license_header_lines | sed 's|^|// |'
}

stylesheet_license_header() {
  printf '/*\n%s\n */\n' "$(license_header_lines | sed 's|^| * |')"
}

every_browser_asset_carries_the_license_header() {
  local file expected actual failures=0

  while IFS= read -r file; do
    if [[ "$file" == *.css ]]; then
      expected="$(stylesheet_license_header)"
      actual="$(head -n 5 "$source_repository_root/$file")"
    else
      expected="$(module_license_header)"
      actual="$(head -n 3 "$source_repository_root/$file")"
    fi

    if [[ "$actual" != "$expected" ]]; then
      printf '%s does not open with the license header\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- '*.js' '*.mjs' '*.css')

  (( failures == 0 ))
}

# MailFathom.code-workspace is what a contributor opens, because the repository root is not a solution
# directory: each stack owns its own solution and a root opened on its own loads neither. Two things
# about that file are contracts rather than preferences, and both are asserted here because both fail
# silently — as an editor that loads nothing rather than as a build that stops.
#
# The first is the order of the folders. The Uno Platform extension resolves its solution from
# `workspace.workspaceFolders[0]` and from nowhere else, and where it finds none it scans that one
# directory for project files without descending. So `frontend` has to be listed first; with `backend`
# there instead the extension would find a solution naming no Uno project, report `Uno SDK not found`,
# and leave the client unavailable in the editor. Nothing else notices, which is why this does.
#
# The second is the licensing header. A `.code-workspace` is JSON with comments, so it carries the
# module form of the three lines — the same `//` form a script under docfx/ uses — and no glob above
# reaches it.
the_editor_workspace_opens_the_client_first() {
  local workspace="$source_repository_root/MailFathom.code-workspace"
  local expected actual first_folder

  if [[ ! -f "$workspace" ]]; then
    printf 'MailFathom.code-workspace is missing; it is the documented way to open this repository\n' >&2
    return 1
  fi

  expected="$(module_license_header)"
  actual="$(head -n 3 "$workspace")"
  if [[ "$actual" != "$expected" ]]; then
    printf 'MailFathom.code-workspace does not open with the license header\n' >&2
    return 1
  fi

  first_folder="$(awk '
    /"folders"/ { in_folders = 1; next }
    in_folders && /"path"/ {
      match($0, /"path"[[:space:]]*:[[:space:]]*"[^"]*"/)
      chunk = substr($0, RSTART, RLENGTH)
      sub(/.*:[[:space:]]*"/, "", chunk)
      sub(/"$/, "", chunk)
      print chunk
      exit
    }
  ' "$workspace")"

  if [[ "$first_folder" != "frontend" ]]; then
    printf 'MailFathom.code-workspace lists "%s" first. The Uno Platform extension reads workspaceFolders[0], so frontend has to be\n' \
      "$first_folder" >&2
    return 1
  fi

  if [[ ! -f "$source_repository_root/frontend/MailFathom.Client.slnx" ]] ||
     [[ ! -f "$source_repository_root/backend/MailFathom.slnx" ]]; then
    printf 'MailFathom.code-workspace names a folder whose solution is not where it says\n' >&2
    return 1
  fi

  return 0
}

# What a published client head is optimized with. Three parts of one posture, each of which fails
# silently rather than loudly, and `docs/operations/client-publishing.md` carries the reasoning.
#
# The browser head publishes trimmed, and `UnoXamlResourcesTrimming` is what lets Uno's pass drop the
# styles of controls the application never names. Removing it costs payload and reports nothing, so the
# property is asserted where it belongs: inside a property group conditioned on that one head.
#
# The desktop head is neither trimmed nor compiled ahead of time, and that used to be a licence
# condition. `LibVLCSharp.dll` was in every publish of it, is LGPL-2.1-or-later, and had to stay
# unmodified and separately replaceable — which `PublishTrimmed`, `PublishAot`, `PublishReadyToRun`, and
# a single-file bundle each take away. It is now excluded from that publish, so none of the four is
# forbidden any more and all four are still unset: what keeps them unset is a measurement nobody has
# taken, which is #1226's question, and the assertion below holds the posture rather than the licence.
# A change that genuinely wants one of them is a change to this contract with its reason, not a property
# added quietly beside it.
#
# What is a licence condition is the exclusion itself, and the one thing that would break it.
# `Uno.WinUI.Runtime.Skia.X11` declares `LibVLCSharp` unconditionally and nothing in that package calls
# it, so `Client.csproj` names it directly with every asset excluded. `MediaPlayerElement` in
# `UnoFeatures` is what would need it back: that feature adds `Uno.WinUI.MediaPlayer.Skia.X11`, whose own
# assembly calls into the library, and a head built with both publishes the control without it and fails
# at first playback with `FileNotFoundException: Could not load file or assembly 'LibVLCSharp'`, which no
# build reports. So the two may not stand together, neither may be absent alone, and the LGPL notices
# that used to travel with the artifact stay gone while the exclusion stands. Turning the feature on
# restores all of it in one change; THIRD_PARTY_LICENSES.md carries what else it then owes.
#
# And none of it runs in front of a pull request. Both gates and the client's own required job build and
# test the client solution and publish no head, because an ahead-of-time or trimmed publish is minutes a
# required check may not spend.
the_client_publishes_what_its_licences_allow() {
  local project="$source_repository_root/frontend/src/Client/Client.csproj"
  local failures='' trimming offenders file
  local excluded='false' plays_media='false' features leftovers

  # The property, and the head it is conditioned on. A property group whose condition does not name the
  # browser head would apply it to the desktop one too, which is the case the licence above refuses.
  trimming="$(awk '
    /<PropertyGroup/ { inside = index($0, "net10.0-browserwasm") > 0; next }
    /<\/PropertyGroup>/ { inside = 0; next }
    /<UnoXamlResourcesTrimming>true<\/UnoXamlResourcesTrimming>/ { print inside ? "scoped" : "unscoped" }
  ' "$project")"

  if [[ "$trimming" != 'scoped' ]]; then
    failures+="Client.csproj does not enable UnoXamlResourcesTrimming for net10.0-browserwasm alone (found: ${trimming:-nothing}). "
  fi

  # The exclusion and the one feature that would need the library back, in both directions.
  if grep -qE '<PackageReference Include="LibVLCSharp"[^>]*ExcludeAssets="all"' "$project"; then
    excluded='true'
  fi

  features="$(awk '/<UnoFeatures>/, /<\/UnoFeatures>/' "$project")"

  if grep -qE '(MediaPlayerElement|MediaElement);' <<< "$features"; then
    plays_media='true'
  fi

  if [[ "$excluded" == 'true' && "$plays_media" == 'true' ]]; then
    failures+='Client.csproj excludes LibVLCSharp while UnoFeatures asks for MediaPlayerElement, which needs it: '
    failures+='the control would publish without the library it calls and fail at first playback. '
  fi

  if [[ "$excluded" == 'false' && "$plays_media" == 'false' ]]; then
    failures+='Client.csproj no longer excludes LibVLCSharp and no UnoFeature needs it, so the desktop publish '
    failures+='carries an LGPL-2.1-or-later assembly nothing calls. '
  fi

  # The exclusion belongs to the head that resolves the package. Stated for the project, it would enter
  # the browser head's restore for nothing.
  if [[ "$excluded" == 'true' ]] &&
    ! grep -B2 '<PackageReference Include="LibVLCSharp"' "$project" |
      grep -qF "'\$(TargetFramework)' == 'net10.0-desktop'"; then
    failures+='the LibVLCSharp exclusion is not scoped to net10.0-desktop. '
  fi

  # And the obligations that travelled with the assembly go with it: a notice naming a component the head
  # no longer carries is an inaccuracy in something somebody downloads.
  if [[ "$excluded" == 'true' ]]; then
    leftovers="$(find "$source_repository_root/frontend/src/Client" -iname 'LGPL*' -o -iname 'THIRD-PARTY-NOTICES*' || true)"

    if [[ -n "$leftovers" ]]; then
      failures+="the excluded head still carries LGPL notices: $(tr '\n' ' ' <<< "$leftovers"). "
    fi

    if grep -qE 'LGPL-2\.1\.txt|THIRD-PARTY-NOTICES\.md' "$project"; then
      failures+='Client.csproj still attaches an LGPL notice to the desktop publish. '
    fi

    # The notes the archives are downloaded from, which is where section 6's source offer had to be. Read as the
    # `printf` lines that compose them rather than as the whole file, so the comment above them stays free to say
    # what the offer was and why it went.
    if grep -qE '^[[:space:]]*printf .*LGPL' "$source_repository_root/.github/workflows/release.yml"; then
      failures+='release.yml still writes the LGPL source offer into the release notes. '
    fi
  fi

  # The four properties, in the two build files a client project reads and in the two builds that produce
  # an artifact. A comment naming one is not a setting, and every file here argues its own posture at length.
  for file in \
    'frontend/src/Client/Client.csproj' \
    'frontend/Directory.Build.props' \
    '.github/workflows/build-desktop-client.yml' \
    'deploy/docker/Dockerfile'; do
    offenders="$(grep -nE '(<|-p:)(PublishAot|PublishTrimmed|PublishReadyToRun|PublishSingleFile)' \
      "$source_repository_root/$file" | grep -vE '^[0-9]+:[[:space:]]*#' || true)"

    if [[ -n "$offenders" ]]; then
      failures+="$file sets a publish property this posture leaves unset: $(tr '\n' ' ' <<< "$offenders"). "
    fi
  done

  # No publish in front of a pull request. The client's heads are published by the container image build
  # and by build-desktop-client.yml, and by nothing a required check waits for.
  for file in \
    'scripts/verify-fast.sh' \
    'scripts/verify-full.sh' \
    '.github/workflows/build-test-frontend.yml'; do
    offenders="$(grep -nE 'dotnet[[:space:]]+publish' "$source_repository_root/$file" || true)"

    if [[ -n "$offenders" ]]; then
      failures+="$file publishes a head, which a required check may not spend the time on: $(tr '\n' ' ' <<< "$offenders"). "
    fi
  done

  if [[ -n "$failures" ]]; then
    printf '%s\n' "$failures" >&2
    return 1
  fi
}

# The Podman Quadlet sources under deploy/quadlet/. A systemd unit file is an INI document whose
# comment character is `#`, so it carries the same three lines a workflow does rather than a form of
# its own — but no glob above reaches it, and Quadlet reads the extension rather than the content, so
# a `.container`, `.network`, or `.volume` file would otherwise be the one deployment asset that
# states neither who owns it nor what terms it arrives under.
every_container_unit_carries_the_license_header() {
  local file expected actual failures=0
  expected="$(comment_license_header)"

  while IFS= read -r file; do
    actual="$(head -n 3 "$source_repository_root/$file")"

    if [[ "$actual" != "$expected" ]]; then
      printf '%s does not open with the license header\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- '*.container' '*.network' '*.volume' '*.pod')

  (( failures == 0 ))
}

# XAML is the fifth place the analyzer cannot reach, and the first one outside the service stack: a
# `.xaml` file is markup rather than C#, so IDE0073 never sees it, and XML's one comment form is what
# carries the header there. It opens the file above the root element, because a XAML parser reads the
# type of the root from the first element it meets and a comment is not one.
markup_license_header() {
  printf '<!--\n%s\n-->\n' "$(license_header_lines)"
}

every_xaml_file_carries_the_license_header() {
  local file expected actual failures=0
  expected="$(markup_license_header)"

  while IFS= read -r file; do
    actual="$(head -n 5 "$source_repository_root/$file")"

    if [[ "$actual" != "$expected" ]]; then
      printf '%s does not open with the license header\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- '*.xaml')

  (( failures == 0 ))
}

# The shebang has to be the first line for the kernel to read it, so a script is the one place the
# header is second rather than first.
every_shell_script_carries_the_license_header() {
  local file expected actual failures=0
  expected="$(comment_license_header)"

  while IFS= read -r file; do
    if [[ "$(head -n 1 "$source_repository_root/$file")" != '#!'* ]]; then
      printf '%s has no shebang, so where its header belongs is undefined\n' "$file" >&2
      failures=$(( failures + 1 ))
      continue
    fi

    actual="$(sed -n '2,4p' "$source_repository_root/$file")"

    if [[ "$actual" != "$expected" ]]; then
      printf '%s does not carry the license header under its shebang\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- '*.sh')

  (( failures == 0 ))
}

# A skill states the same three facts as frontmatter, because the Agent Skills specification already
# defines where a skill declares its license and leaves `metadata` open for the rest. A comment above
# the frontmatter would be read as content by every client that parses one. No version key joins
# them: `<VersionPrefix>` in `Version.props` is the only version number in this repository.
every_skill_declares_its_license() {
  local file frontmatter holder repository failures=0

  holder="$(license_header_lines | sed -n '1s/^Copyright © [0-9]\{4\} //p')"
  repository="$(license_header_lines | sed -n '3s/^Project repository: //p')"

  while IFS= read -r file; do
    frontmatter="$(awk 'NR == 1 { next } /^---$/ { exit } { print }' "$source_repository_root/$file")"

    if ! grep -qxF 'license: Apache-2.0' <<< "$frontmatter"; then
      printf '%s declares no license in its frontmatter\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi

    if ! grep -qxF "  author: $holder" <<< "$frontmatter" \
      || ! grep -qxF "  repository: $repository" <<< "$frontmatter"; then
      printf '%s does not name the author and the repository in its metadata\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(git -C "$source_repository_root" ls-files -- '.agents/skills/*/SKILL.md')

  (( failures == 0 ))
}

# A literal NUL byte makes a text file binary to everything that reads it as text. `grep` reports no
# match where there is one, so a helper written inside such a file is invisible to the search a later
# session runs before writing its own copy of it; `git diff` renders any change to it as `Binary files
# differ`, so the change arrives unreviewable. The escape that means the same character — `\0` in a
# string, `'\0'` for the char — costs none of that.
#
# The sweep asks `.gitattributes` which files are binary rather than carrying an extension list of its
# own, because the repository declares that once already and a second list would stop silently at the
# first `.props`, `.slnx`, or Quadlet unit nobody remembered to add to it.
#
# Detection is `read -d ''`, which reads to the first NUL and reports whether it reached one, rather
# than git's own binary heuristic: that one looks at the first 8000 bytes alone, so it reads a file
# carrying the byte further in as ordinary text — which is exactly where one of the five sites this
# check was written for sat.
no_tracked_text_file_carries_a_nul_byte() {
  local text_files file failures=0

  text_files="$(git -C "$source_repository_root" ls-files |
    git -C "$source_repository_root" check-attr --stdin binary |
    grep -v ': binary: set$' |
    sed 's/: binary: [^:]*$//')"

  while IFS= read -r file; do
    # A gitlink is tracked and is not a file, and nothing here can read one.
    [[ -f "$source_repository_root/$file" ]] || continue

    if IFS= read -r -d '' _ < "$source_repository_root/$file"; then
      printf '%s carries a literal NUL byte, which makes it binary to grep and to git diff; write the escape instead\n' \
        "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done <<< "$text_files"

  # The floor rather than the coverage. A sweep that reached nothing reports nothing and is
  # indistinguishable from a clean tree, and the extension list this check exists to avoid would leave
  # every one of these three out. The fourth is the declared-binary case, which has to stay out or
  # every image in the repository fails.
  for file in backend/MailFathom.slnx deploy/quadlet/mailfathom.container docfx/template/public/main.css; do
    if ! grep -qxF "$file" <<< "$text_files"; then
      printf '%s is not among the tracked text files the sweep read\n' "$file" >&2
      failures=$(( failures + 1 ))
    fi
  done

  if grep -qxF 'assets/icon-180.png' <<< "$text_files"; then
    printf 'assets/icon-180.png is declared binary and must not be swept as text\n' >&2
    failures=$(( failures + 1 ))
  fi

  (( failures == 0 ))
}

run_test verify_fast_runs_restore_build_tests_and_formatting
run_test verify_fast_runs_the_flow_of_each_stack_the_change_reaches
run_test verify_fast_runs_no_stack_flow_for_a_change_no_build_reads
run_test verify_full_runs_no_stack_flow_for_a_change_no_build_reads
run_test verify_full_runs_both_flows_for_a_change_above_both_stacks
run_test verify_full_runs_tests_once_through_coverage
run_test verify_full_verifies_each_solution_the_change_reaches
run_test verify_full_runs_workflow_contracts_for_a_change_beyond_csharp
run_test verify_full_skips_workflow_contracts_for_a_csharp_only_change
run_test verify_full_runs_workflow_contracts_when_the_branch_removed_a_path
run_test verify_full_fails_when_workflow_contracts_fail_beside_a_running_chain
run_test verify_full_stops_the_contract_suite_once_the_chain_failed
run_test verify_full_relays_what_the_contract_suite_printed
run_test verify_fast_skips_a_tree_it_already_proved
run_test verify_full_leaves_the_record_alone_when_it_skips
run_test verify_fast_runs_again_once_the_tree_changed
run_test verify_fast_accepts_the_record_the_full_gate_wrote
run_test verify_fast_accepts_the_full_gate_record_for_a_client_change
run_test verify_full_refuses_the_fast_loop_record_except_for_the_formatting_pass
run_test verify_fast_records_nothing_when_formatting_rewrote_a_file
run_test verify_full_records_nothing_when_it_failed
run_test verify_force_runs_everything_a_record_would_have_skipped
run_test verify_full_formats_the_whole_solution_when_a_shared_style_input_changed
run_test verify_full_formats_the_whole_solution_when_a_shared_style_input_was_removed
run_test verify_full_formats_nothing_when_no_csharp_file_changed
run_test verify_full_checks_committed_staged_and_unstaged_changes
run_test verify_full_rejects_untracked_files
run_test verify_full_fetches_the_remote_base_before_verifying
run_test verify_full_stops_when_head_is_behind_origin_main
run_test verify_full_stops_when_the_remote_is_unreachable
run_test verify_full_stops_when_a_stale_tracking_ref_hides_remote_movement
run_test verify_fast_refuses_the_main_branch
run_test verify_full_refuses_the_master_branch
run_test verify_fast_accepts_a_detached_head
run_test verify_fast_skips_formatting_when_no_csharp_file_changed
run_test verification_stops_after_first_failure
run_test workspace_inspection_is_read_only_and_labeled
run_test workspace_inspection_reports_unavailable_sdk
run_test fork_workspace_resolves_its_base_against_the_upstream_remote
run_test fork_workspace_reports_an_unresolved_base_without_an_upstream_remote
run_test fork_workspace_refuses_an_owner_whose_name_merely_ends_with_the_canonical_one
run_test verify_full_verifies_a_fork_against_its_upstream_remote
run_test verify_full_stops_when_a_fork_branch_is_behind_upstream_main
run_test verify_full_refuses_a_checkout_with_no_upstream_remote
run_test verify_fast_runs_in_a_fork_with_no_upstream_remote
run_test protected_paths_allows_a_change_that_touches_nothing_protected
run_test protected_paths_refuses_a_contributor_changing_repository_root_configuration
run_test protected_paths_refuses_a_contributor_changing_a_protected_directory
run_test protected_paths_matches_the_configuration_files_at_every_depth
run_test protected_paths_ignores_paths_that_only_resemble_a_protected_one
run_test protected_paths_reports_the_paths_it_found_when_the_owner_is_the_author
run_test protected_paths_allows_dependabot_to_update_the_workflows
run_test protected_paths_refuses_dependabot_outside_the_workflows
run_test protected_paths_refuses_an_author_merely_resembling_dependabot
run_test protected_paths_refuses_a_pull_request_larger_than_the_reportable_limit
run_test protected_paths_lets_the_owner_past_the_reportable_limit
run_test typo_check_passes_the_files_the_pull_request_changed
run_test typo_check_leaves_an_image_out_of_the_file_list
run_test typo_check_checks_nothing_when_a_pull_request_only_changes_images
run_test typo_check_checks_nothing_when_the_pull_request_only_removes_files
run_test typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_whitespace
run_test typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_a_glob_character
run_test typo_check_falls_back_to_the_whole_checkout_for_a_pull_request_beyond_the_reportable_limit
run_test fathom_review_reviews_a_push_to_a_published_pull_request
run_test fathom_review_reviews_a_push_below_the_automatic_ceiling
run_test fathom_review_stops_reviewing_a_push_at_the_automatic_ceiling
run_test fathom_review_never_counts_a_requested_review_against_the_ceiling
run_test fathom_review_counts_the_marker_only_where_the_submission_writes_it
run_test fathom_review_reads_an_early_pass_at_the_full_bar
run_test fathom_review_settles_the_bar_from_the_fourth_pass
run_test fathom_review_keeps_a_requested_review_at_the_full_bar
run_test fathom_review_answers_a_request_past_the_automatic_ceiling
run_test fathom_review_refuses_a_closed_pull_request
run_test fathom_review_refuses_a_pull_request_the_updater_opened
run_test fathom_review_reviews_an_updater_pull_request_the_maintainer_labelled
run_test fathom_review_collects_at_once_when_nobody_has_commented
run_test fathom_review_waits_before_freezing_a_quiet_conversation
run_test fathom_review_stops_waiting_at_the_ceiling
run_test fathom_review_reads_the_newest_comment_whatever_the_order
run_test fathom_review_collects_the_labels_of_an_issue_the_change_closes
run_test fathom_review_reports_unknown_labels_for_an_issue_it_could_not_fetch
run_test fathom_review_reads_head_content_within_its_window
run_test fathom_review_stops_reading_head_content_when_its_window_is_gone
run_test fathom_review_stops_reading_closing_issues_when_its_window_is_gone
run_test fathom_review_reports_the_head_content_files_its_count_ceiling_cut
run_test fathom_review_names_what_moved_since_its_previous_pass
run_test fathom_review_narrows_nothing_on_a_first_pass
run_test fathom_review_narrows_nothing_on_a_requested_pass
run_test fathom_review_reads_a_security_labelled_change_with_the_costlier_model
run_test fathom_review_keeps_the_default_model_for_an_ordinary_change
run_test fathom_review_waits_for_the_labelling_run_before_reading_the_labels
run_test fathom_review_reads_the_labels_as_they_stand_at_the_ceiling
run_test select_labels_earns_the_security_label_from_an_issue_the_change_closes
run_test select_labels_earns_the_security_label_from_an_issue_the_change_is_merely_related_to
run_test select_labels_earns_nothing_from_an_ordinary_change
run_test select_labels_earns_nothing_from_an_issue_it_could_not_read
run_test apply_pull_request_labels_posts_the_labels_the_change_earns
run_test apply_pull_request_labels_posts_nothing_for_an_ordinary_change
run_test apply_pull_request_labels_reports_a_write_it_was_refused
run_test fathom_review_anchors_a_finding_to_its_line
run_test fathom_review_approves_a_pass_carrying_only_deferred_findings
run_test fathom_review_holds_a_pass_that_still_found_something_owed
run_test fathom_review_approves_a_first_pass_carrying_only_deferred_findings
run_test fathom_review_approves_a_settling_pass_carrying_a_rule_owed_finding
run_test fathom_review_holds_a_settling_pass_that_found_something_broken
run_test fathom_review_reads_an_unset_posture_as_the_full_bar
run_test fathom_review_moves_a_finding_with_no_line_into_the_body
run_test fathom_review_approves_when_it_finds_nothing
run_test fathom_review_reports_the_files_a_review_never_named
run_test fathom_review_reports_no_gap_when_the_review_named_every_file
run_test fathom_review_counts_a_named_path_the_change_does_not_contain
run_test fathom_review_bounds_how_many_unread_files_it_names
run_test fathom_review_reads_a_ledger_of_the_wrong_shape_as_an_empty_one
run_test fathom_review_separates_what_moved_from_what_nobody_re_read
run_test fathom_review_reports_a_gap_inside_what_the_pass_re_read
run_test fathom_review_says_when_a_reader_never_reported
run_test fathom_review_reports_the_whole_change_when_no_reader_returned
run_test fathom_review_marks_a_review_with_what_started_it
run_test fathom_review_publishes_the_coverage_gap_beside_its_findings
run_test fathom_review_publishes_the_coverage_gap_under_an_approval
run_test fathom_review_publishes_nothing_when_the_reviewer_returned_no_answer
run_test fathom_review_fails_when_a_finished_reviewer_returned_no_answer
run_test fathom_review_refuses_findings_that_carry_a_credential
run_test fathom_review_moves_an_approved_pull_request_to_ready_to_merge
run_test fathom_review_records_findings_as_changes_requested
run_test fathom_review_leaves_a_finished_item_alone
run_test fathom_review_leaves_a_blocked_item_alone
run_test fathom_review_moves_nothing_for_a_pull_request_that_closes_no_issue
run_test fathom_review_announces_a_started_review
run_test fathom_review_announces_nothing_over_a_finished_or_blocked_item
run_test fathom_review_announces_over_every_other_status
run_test fathom_review_writes_no_status_without_the_board_token
run_test board_status_moves_an_item_a_rule_is_entitled_to_move
run_test board_status_leaves_an_item_outside_the_required_statuses
run_test board_status_stops_writing_when_its_window_is_gone
run_test select_board_status_earns_conflicts_from_ready_to_merge_alone
run_test select_board_status_earns_nothing_until_github_has_decided
run_test pull_request_rules_move_a_pull_request_that_stopped_merging
run_test pull_request_rules_move_nothing_for_a_pull_request_that_still_merges
run_test pull_request_rules_wait_for_github_to_decide_mergeability
run_test pull_request_rules_report_the_pull_requests_the_ceiling_cut
run_test pull_request_rules_report_a_pull_request_github_never_decided
run_test every_documentation_page_declares_what_it_describes
run_test every_describes_pattern_matches_something_that_exists
run_test no_documentation_page_carries_the_third_party_notice_twice
run_test every_third_party_notice_sits_directly_under_its_marker
run_test every_published_documentation_page_is_in_a_table_of_contents
run_test every_table_of_contents_entry_names_a_page_that_exists
run_test the_documentation_map_lists_every_published_page
run_test every_documentation_map_entry_names_a_page_the_version_carries
run_test the_documentation_artifacts_refuse_a_published_page_the_map_would_miss
run_test the_documentation_artifacts_refuse_a_map_entry_naming_no_page
run_test the_documentation_artifacts_refuse_a_page_with_no_description
run_test a_documentation_bundle_resolves_a_link_out_of_its_own_section
run_test every_readme_site_link_names_a_page_that_exists
run_test no_readme_link_reaches_a_published_page_through_the_repository
run_test the_docker_hub_overview_fits_what_docker_hub_accepts
run_test github_api_call_asks_once_when_the_call_succeeds
run_test github_api_call_returns_the_answer_after_a_dropped_connection
run_test github_api_call_does_not_retry_an_answer_the_api_produced
run_test github_api_call_retries_a_status_that_says_ask_again
run_test github_api_call_kills_an_attempt_that_stalls
run_test github_api_call_waits_longer_between_each_attempt
run_test github_api_call_fails_after_the_budgeted_attempts
run_test referenced_issues_collect_a_mention_as_well_as_a_closing_reference
run_test referenced_issues_collect_a_link_to_an_issue_in_this_repository
run_test referenced_issues_ignore_another_repository
run_test referenced_issues_report_each_issue_once
run_test referenced_issues_report_what_the_ceiling_cut
run_test closing_issues_collect_every_issue_the_merge_will_close
run_test closing_issues_print_nothing_when_the_merge_closes_nothing
run_test closing_issues_ignore_another_repository
run_test closing_issues_report_what_the_ceiling_cut
run_test closing_issues_report_nothing_when_the_ceiling_is_not_reached
run_test closing_issues_keep_a_recovered_retry_off_standard_error
run_test closing_issues_report_each_issue_once
run_test obligation_index_reports_a_changed_source_no_test_reaches
run_test obligation_index_credits_a_test_the_change_adds
run_test obligation_index_names_a_test_the_change_left_alone
run_test obligation_index_maps_a_changed_path_to_the_page_that_describes_it
run_test obligation_index_credits_a_path_directly_under_a_double_star
run_test obligation_index_ignores_a_marker_below_the_preamble
run_test obligation_index_reports_a_moved_pin_with_no_register_row
run_test obligation_index_records_a_register_the_change_updated
run_test obligation_index_caps_the_tests_it_lists_for_one_type
run_test review_obligations_reports_a_source_the_working_tree_leaves_untested
run_test review_obligations_names_the_untracked_paths_no_diff_contains
run_test review_obligations_reports_without_gating
run_test review_obligations_reports_on_a_patch_past_the_argument_limit
run_test obligation_index_leaves_migrations_out
run_test group_split_gives_every_changed_file_exactly_one_reader
run_test group_split_never_exceeds_the_reader_ceiling
run_test group_split_balances_what_each_reader_is_given
run_test group_split_keeps_a_small_change_whole
run_test group_split_accepts_a_change_it_was_given_no_files_for
run_test group_split_reads_only_the_groups_that_moved
run_test group_split_reads_every_group_when_nothing_bounds_the_pass
run_test fathom_review_composes_a_reader_prompt_naming_only_its_group
run_test fathom_review_refuses_a_reader_group_that_holds_no_files
run_test fathom_review_substitutes_every_placeholder_its_prompts_carry
run_test fathom_review_schemas_keep_coverage_with_the_readers
run_test publish_qualifies_every_nightly_tag_with_the_repository_it_resolves
run_test publish_qualifies_the_release_tags_and_ignores_a_blank_line
run_test publish_folds_the_owner_login_into_the_docker_hub_namespace
run_test publish_sends_a_release_to_its_own_documentation_directory
run_test publish_sends_a_nightly_to_the_default_branch_documentation
run_test publish_refuses_a_tag_list_with_nothing_to_publish
run_test release_tag_assertion_accepts_a_tag_that_matches_its_commit
run_test release_tag_assertion_refuses_a_prerelease_tag
run_test release_tag_assertion_refuses_a_lightweight_tag
run_test release_tag_assertion_refuses_a_version_the_commit_does_not_declare
run_test release_tag_assertion_refuses_a_commit_that_declares_no_version_at_all
run_test release_tag_assertion_refuses_a_commit_that_never_merged
run_test release_tag_assertion_accepts_a_patch_from_a_release_branch
run_test release_tag_assertion_refuses_a_version_already_released_on_its_line
run_test release_tag_assertion_refuses_an_empty_changelog_section
run_test changelog_section_reading_returns_only_the_requested_release
run_test schema_artifact_carries_no_byte_order_mark
run_test schema_artifact_checksum_covers_the_file_an_operator_applies
run_test schema_artifact_leaves_an_unmarked_publish_untouched
run_test winget_manifests_name_the_release_assets_they_hash
run_test winget_manifest_names_the_product_and_the_command
run_test winget_manifests_refuse_a_missing_windows_binary
run_test install_script_installs_the_binary_the_release_published
run_test install_script_refuses_a_binary_the_checksum_file_disowns
run_test install_script_refuses_a_platform_no_release_publishes
run_test quick_start_prepares_the_deployment_the_documentation_describes
run_test quick_start_keeps_the_mailbox_password_out_of_everything_but_its_own_file
run_test quick_start_refuses_to_overwrite_a_prepared_deployment
run_test quick_start_refuses_a_mailbox_that_accepts_no_password
run_test quick_start_authenticates_the_mcp_endpoint_unless_asked_otherwise
run_test quick_start_serves_the_administrative_endpoint_on_a_port_of_its_own
run_test quick_start_refuses_a_value_that_would_write_broken_configuration
run_test quick_start_says_it_is_an_evaluation_rather_than_a_recommended_deployment
run_test every_external_action_names_an_approved_owner
run_test every_workflow_job_declares_its_permissions
run_test every_write_scope_is_one_the_policy_records
run_test every_checkout_refuses_to_persist_credentials
run_test the_release_restores_the_annotated_tag_before_asserting_it
run_test no_channel_builds_an_artifact_before_the_commit_has_verified
run_test the_mutation_score_is_reported_and_never_gated
run_test a_paid_provider_run_is_never_the_default
run_test only_the_recorded_workflows_use_pull_request_target
run_test a_comment_never_cancels_a_review_in_flight
run_test the_reviewer_resolves_one_claude_credential_everywhere
run_test the_development_tooling_never_reaches_a_published_artifact
run_test the_required_check_aggregates_every_job_in_ci
run_test the_stacks_change_filters_name_no_path_in_each_other
run_test the_gates_decide_from_the_change_filters_ci_declares
run_test workflow_scripts_use_flat_manual_layout
run_test the_dependency_survey_refuses_what_would_write_without_being_asked
run_test the_dependency_rewrite_encodes_a_hostile_version_into_a_pin_file
run_test every_yaml_file_carries_the_license_header
run_test every_browser_asset_carries_the_license_header
run_test every_container_unit_carries_the_license_header
run_test every_xaml_file_carries_the_license_header
run_test the_editor_workspace_opens_the_client_first
run_test the_client_publishes_what_its_licences_allow
run_test every_shell_script_carries_the_license_header
run_test every_skill_declares_its_license
run_test no_tracked_text_file_carries_a_nul_byte

printf '%s passed, %s failed\n' "$passed_count" "$failed_count"

if ((failed_count > 0)); then
  exit 1
fi
