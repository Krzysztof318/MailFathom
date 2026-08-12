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
  "$repository_root/src" \
  "$repository_root/tests"
ln -s "$scripts_directory/test-agent-workflow.sh" "$fake_bin_directory/dotnet"

git -C "$repository_root" init --initial-branch=main --quiet
git -C "$repository_root" config user.email agent-workflow@example.invalid
git -C "$repository_root" config user.name 'Agent Workflow Tests'
printf '<Solution />\n' > "$repository_root/MailFathom.slnx"
printf 'clean\n' > "$repository_root/tracked.txt"
printf '%s\n' \
  '#!/usr/bin/env bash' \
  'set -euo pipefail' \
  ': "${FAKE_WORKFLOW_LOG:?FAKE_WORKFLOW_LOG must identify the invocation log}"' \
  "printf 'workflow-contracts\\n' >> \"\$FAKE_WORKFLOW_LOG\"" \
  'if [[ -n "${FAKE_WORKFLOW_FAIL:-}" ]]; then exit 23; fi' \
  > "$repository_root/scripts/test-agent-workflow.sh"
chmod +x "$repository_root/scripts/test-agent-workflow.sh"
git -C "$repository_root" add MailFathom.slnx scripts/test-agent-workflow.sh tracked.txt
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
printf 'namespace Fixture;\n' > "$repository_root/src/Sample.cs"
git -C "$repository_root" add src/Sample.cs
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

# The `Fathom review` gate makes one API call, counting the reviews its App has already submitted on
# the pull request. This fake `gh` prints the per-page count the real `--jq` would leave, so the
# ceiling arithmetic in the step runs unchanged. It answers zero, which is a pull request the App has
# not reviewed yet — the state both contracts below are about.
mkdir -p "$fathom_review_bin_directory"
cat > "$fathom_review_bin_directory/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '0\n'
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

# One repairing pass and no verifying one. The verifying pass would restate the Release build two
# lines above it: `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` turn every IDE rule the
# `.editorconfig` sets to `warning` into a build error, so a diagnostic with no code fix has already
# failed the run before formatting is reached. What the repairing pass is here for is the part no
# build reports — the ordering of using directives, a missing final newline — and repairing it is
# something only this script does.
verify_fast_runs_restore_build_tests_and_formatting() {
  : > "$invocation_log"

  (
    cd "$repository_root/tests"
    "$scripts_directory/verify-fast.sh"
  )

  assert_file_content \
    $'restore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\ntest --solution MailFathom.slnx --configuration Release --no-build\nformat MailFathom.slnx --no-restore --include src/Sample.cs' \
    "$invocation_log"
}

# The fixture branch changes one C# file and nothing else, which is also the case the scoped
# verification below is about: the gate verifies the file the branch wrote rather than the 1113 the
# solution holds, because formatting is a property of a file and every other one was verified by
# whatever change last touched it.
verify_full_runs_tests_once_through_coverage() {
  : > "$invocation_log"

  (
    cd "$repository_root/src"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content \
    $'tool restore\nrestore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\nmsbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release\nformat MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic --include src/Sample.cs' \
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

# Every invariant the suite asserts is carried by a file no C# change can move: a licensing header
# outside `.cs`, a `describes:` marker, a table-of-contents entry. `CI` runs it on every pull request
# including a draft, so what is skipped here is an earlier verdict rather than the verdict.
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

verify_full_stops_when_workflow_contracts_fail() {
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
  assert_file_content '' "$invocation_log"
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

  assert_contains 'format MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic' "$invocation_log"
  assert_excludes '--include' "$invocation_log"
}

# A deleted style input decides as much as an edited one, and it is the case a list of the files that
# still exist cannot see. The rules a nested `.editorconfig` carried stop applying the moment it is
# gone, so every file beneath it is read against the ones above from that commit on — without any of
# them having been touched.
verify_full_formats_the_whole_solution_when_a_shared_style_input_was_removed() {
  : > "$invocation_log"
  printf 'root = true\n' > "$repository_root/src/.editorconfig"
  git -C "$repository_root" add src/.editorconfig
  git -C "$repository_root" commit --quiet -m 'nested editorconfig'
  git -C "$repository_root" rm --quiet src/.editorconfig

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  git -C "$repository_root" reset --quiet --hard HEAD~1

  assert_contains 'format MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic' "$invocation_log"
  assert_excludes '--include' "$invocation_log"
}

# A change that wrote no C# file has nothing for `dotnet format` to be asked about, in either
# direction: there is no file to verify and no shared input that would widen the scope to the
# solution. The suite still runs, because that change is exactly what it reads.
verify_full_formats_nothing_when_no_csharp_file_changed() {
  : > "$invocation_log"
  : > "$workflow_invocation_log"
  git -C "$repository_root" checkout --quiet --detach origin/main
  stage_documentation_change

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  discard_documentation_change
  git -C "$repository_root" checkout --quiet "$fixture_branch"

  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
  assert_excludes 'format MailFathom.slnx' "$invocation_log"
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
    $'restore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\ntest --solution MailFathom.slnx --configuration Release --no-build\nformat MailFathom.slnx --no-restore --include src/Sample.cs' \
    "$invocation_log"
}

verify_fast_skips_formatting_when_no_csharp_file_changed() {
  local script_status=0

  : > "$invocation_log"
  git -C "$repository_root" checkout --quiet --detach origin/main

  (
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ) > /dev/null 2>&1 || script_status=$?

  git -C "$repository_root" checkout --quiet "$fixture_branch"

  if ((script_status != 0)); then
    printf 'verify-fast.sh failed with nothing to format\n' >&2
    return 1
  fi

  assert_file_content \
    $'restore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\ntest --solution MailFathom.slnx --configuration Release --no-build' \
    "$invocation_log"
}

verification_stops_after_first_failure() {
  : > "$invocation_log"

  if (
    export FAKE_DOTNET_FAIL_MATCH='build MailFathom.slnx'
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ); then
    printf 'verify-fast.sh succeeded despite the configured build failure\n' >&2
    return 1
  fi

  assert_file_content \
    $'restore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore' \
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
  mkdir -p "$fork_root/src"
  printf 'namespace Fork;\n' > "$fork_root/src/ForkSample.cs"

  (
    cd "$fork_root"
    "$scripts_directory/verify-fast.sh"
  )

  assert_contains 'format MailFathom.slnx --no-restore --include src/ForkSample.cs' "$invocation_log"
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
    $'src/Domain/Emails/EmailOccurrenceId.cs\nREADME.md\ndocs/operations/agent-workflow.md' \
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
    $'Directory.Build.props\nLICENSE\nNOTICE\nNuGet.config\nglobal.json\nCHANGELOG.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed a contributor to change the repository-root configuration\n' >&2
    return 1
  fi

  local protected_file
  for protected_file in Directory.Build.props LICENSE NOTICE NuGet.config global.json CHANGELOG.md; do
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
    $'src/Infrastructure/Persistence/Migrations/.editorconfig\ndocs/.gitattributes\ntests/shared/.worktreeinclude\ntests/AGENTS.md\nsrc/CLAUDE.md' \
    "$output_file" \
    "$summary_file"; then
    printf 'Protected paths allowed a contributor to change a nested configuration file\n' >&2
    return 1
  fi

  assert_contains '::error file=src/Infrastructure/Persistence/Migrations/.editorconfig::' "$output_file"
  assert_contains '::error file=docs/.gitattributes::' "$output_file"
  assert_contains '::error file=tests/shared/.worktreeinclude::' "$output_file"
  assert_contains '::error file=tests/AGENTS.md::' "$output_file"
  assert_contains '::error file=src/CLAUDE.md::' "$output_file"
}

protected_paths_ignores_paths_that_only_resemble_a_protected_one() {
  local output_file="$test_directory/protected-paths-resemblance-output"
  local summary_file="$test_directory/protected-paths-resemblance-summary"

  # A protected name is anchored to a path segment and a protected file to the repository root, so a
  # longer name beginning the same way, a suffix of one, and a copy placed elsewhere all pass.
  if ! run_protected_paths_step \
    'outside-contributor' \
    $'docs/my.editorconfig\n.editorconfiguration\ndeploy/global.json\nsrc/Host/NOTICE.md\n.githubbed/stale.yml\ndocs/CONTRIBUTING-AGENTS.md\ndocs/decisions-notes.md' \
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
    'Krzysztof318' \
    'README.md' \
    "$output_file" \
    "$summary_file" \
    3001; then
    printf 'Protected paths passed a pull request too large for the changed-files endpoint\n' >&2
    return 1
  fi

  assert_contains 'cannot be verified' "$output_file"
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
    $'README.md\nsrc/Domain/Emails/EmailAddress.cs\n.github/workflows/typo-check.yml' \
    "$output_file" \
    "$step_output_file"; then
    printf 'Typo check failed to collect an ordinary changed-file list\n' >&2
    return 1
  fi

  assert_file_content \
    'files=README.md src/Domain/Emails/EmailAddress.cs .github/workflows/typo-check.yml' \
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
  local step_script="$test_directory/fathom-review-gate.sh"

  extract_fathom_review_step 'gate' "$step_script"
  : > "$step_output_file"

  (
    export PATH="$fathom_review_bin_directory:$PATH"
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
    export GITHUB_OUTPUT="$step_output_file"
    bash "$step_script"
  ) > "$output_file" 2>&1
}

fathom_review_reviews_a_push_to_a_published_pull_request() {
  local output_file="$test_directory/fathom-review-push-output"
  local step_output_file="$test_directory/fathom-review-push-step-output"

  run_fathom_review_gate 'synchronize' "$output_file" "$step_output_file"

  assert_contains 'review=true' "$step_output_file"
  assert_contains 'the branch was pushed to' "$output_file"
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
    response='{"data":{"repository":{"pullRequest":{"reviewThreads":{"nodes":[]}}}}}'
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
  */pulls/*/files*)
    response='[{"filename":"src/Sample.cs","previous_filename":null,"status":"modified","additions":1,"deletions":0,"patch":"@@ -1,2 +1,3 @@\n unchanged\n+added\n unchanged"}]'
    ;;
  */pulls/*/reviews*)
    response='[]'
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
    # The step runs the real `collect-closing-references.sh` out of the workspace, which on the
    # runner holds the base commit and here holds the checkout the suite is testing.
    export GITHUB_WORKSPACE="$source_repository_root"
    export GITHUB_OUTPUT="$step_output_file"
    export FAKE_UNFETCHABLE_ISSUES='12'
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
    export LABELLING_WORKFLOW='apply-pull-request-labels.yml'
    # Seconds rather than minutes, for the reason the settle contracts run on short windows: what is
    # asserted is the decision the loop takes, never the values the workflow declares.
    export LABELLING_LIMIT_SECONDS="$limit_seconds"
    export LABELLING_POLL_SECONDS='1'
    export FAKE_PULL_REQUEST_LABELS="$pull_request_labels"
    export FAKE_LABELLING_COUNTDOWN="$countdown_file"
    export GITHUB_OUTPUT="$step_output_file"
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
    bash "$source_repository_root/.github/pull-request-labels/select-labels.sh" \
      'Krzysztof318/MailFathom' '1' \
      "$source_repository_root/.github/pull-request-labels/collect-referenced-issues.sh" '10'
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
  local step_script="$test_directory/apply-pull-request-labels.sh"

  extract_workflow_step \
    "$source_repository_root/.github/workflows/apply-pull-request-labels.yml" \
    'label' "$step_script"
  rm -f "$request_file"

  set +e
  (
    export PATH="$collect_bin_directory:$PATH"
    export GH_TOKEN='fake-token'
    export REPOSITORY='Krzysztof318/MailFathom'
    export PULL_REQUEST_NUMBER='1'
    export SELECT_LABELS_SCRIPT="$source_repository_root/.github/pull-request-labels/select-labels.sh"
    export REFERENCED_ISSUES_SCRIPT="$source_repository_root/.github/pull-request-labels/collect-referenced-issues.sh"
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
  printf '[{"filename":"src/Sample.cs","lines":[12,13,14]}]\n' > "$review_directory/lines.json"
  : > "$review_directory/truncation.txt"
}

run_fathom_review_submit() {
  local findings="$1"
  local output_file="$2"
  local payload_file="$3"
  # A reviewer that finished is the ordinary case; the contract about a run that did not names it.
  local review_outcome="${4:-success}"
  local step_script="$test_directory/fathom-review-submit.sh"
  local review_directory="$test_directory/fathom-review-submit-review"

  submit_step_output_file="$test_directory/fathom-review-submit-step-output"

  extract_fathom_review_step 'submit' "$step_script"
  write_fathom_review_collection "$review_directory"
  rm -f "$payload_file"
  : > "$submit_step_output_file"

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
    '{"summary":"Read the whole change.","findings":[{"severity":"P1","path":"src/Sample.cs","start_line":null,"line":12,"title":"Refuse the empty case","impact":"An empty list reaches the loop and the guard passes.","correction":"Return early when the list is empty.","rule":"`AGENTS.md`, \"Reliability, security, and performance\""}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '"COMMENT"' '.event' "$payload_file"
  assert_json '["src/Sample.cs"]' '[.comments[].path]' "$payload_file"
  assert_json '[12]' '[.comments[].line]' "$payload_file"
  assert_json '["RIGHT"]' '[.comments[].side]' "$payload_file"
  assert_contains '# NEEDS CHANGES' "$payload_file"
  assert_contains '**Findings** — P1: 1' "$payload_file"
  assert_contains 'verdict=changes_requested' "$submit_step_output_file"
}

fathom_review_moves_a_finding_with_no_line_into_the_body() {
  local output_file="$test_directory/fathom-review-submit-unanchored-output"
  local payload_file="$test_directory/fathom-review-submit-unanchored-payload"

  # A line the diff does not carry and a finding that never had one: both reach the author through
  # the body rather than being dropped, which is the property the anchor validation exists for.
  run_fathom_review_submit \
    '{"summary":"Read the whole change.","findings":[{"severity":"P2","path":"src/Sample.cs","start_line":null,"line":99,"title":"Name the moved line","impact":"The anchor is not on the diff.","correction":"Anchor it where the change is.","rule":"`AGENTS.md`"},{"severity":"P3","path":null,"start_line":null,"line":null,"title":"Say what the body claims","impact":"The body promises a rename the diff does not make.","correction":"Correct the body.","rule":"`docs/operations/issue-tracking.md`"}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 0))
  assert_json '[]' '.comments' "$payload_file"
  assert_contains '### Findings with no line to sit on' "$payload_file"
  assert_contains '**P2 — Name the moved line** — `src/Sample.cs`' "$payload_file"
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
    '{"summary":"Read the whole change.","findings":[{"severity":"P1","path":"src/Sample.cs","start_line":null,"line":12,"title":"Quote the token","impact":"The token sk-ant-oat01-credentialthatisnotreal is echoed here.","correction":"Do not.","rule":"`AGENTS.md`"}]}' \
    "$output_file" "$payload_file"

  ((submit_status == 1))
  [[ ! -e "$payload_file" ]]
  assert_contains 'shaped like a credential' "$output_file"
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
if [[ "$arguments" == *'/pulls/'* ]]; then
  cat "$FAKE_BOARD_DIRECTORY/body.txt"
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

# The board the two writing steps see: one pull request body, one field carrying every option the
# real board carries, and one item at the status the contract is about.
prepare_fathom_review_board_state() {
  local body="$1"
  local current_status="$2"

  board_directory="$test_directory/fathom-review-board-state"
  board_mutations_file="$board_directory/mutations.txt"

  rm -rf "$board_directory"
  mkdir -p "$board_directory"
  printf '%s' "$body" > "$board_directory/body.txt"
  : > "$board_mutations_file"

  cat > "$board_directory/field.json" <<'BOARD_FIELD'
{"data":{"user":{"projectV2":{"id":"PVT_board","field":{"id":"PVTSSF_status","options":[
  {"id":"option-todo","name":"Todo"},
  {"id":"option-review","name":"In review"},
  {"id":"option-changes","name":"Changes requested"},
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
  export CLOSING_REFERENCES_SCRIPT="$source_repository_root/.github/fathom-review/collect-closing-references.sh"
  export CLOSING_REFERENCE_LIMIT='5'
  export BOARD_STATUS_SCRIPT="$source_repository_root/.github/fathom-review/write-board-status.sh"
  export FAKE_BOARD_DIRECTORY="$board_directory"
}

run_fathom_review_board() {
  local verdict="$1"
  local body="$2"
  local current_status="$3"
  local output_file="$4"
  # The ordinary case is a configured board; the contract about an unconfigured one names the empty
  # token itself.
  local board_token="${5-classic-token-that-is-not-real}"
  local step_script="$test_directory/fathom-review-board.sh"

  extract_fathom_review_step 'board' "$step_script"
  prepare_fathom_review_board_state "$body" "$current_status"

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
  local body="$1"
  local current_status="$2"
  local output_file="$3"
  local board_token="${4-classic-token-that-is-not-real}"
  local step_script="$test_directory/fathom-review-announce.sh"

  extract_fathom_review_step 'in-review' "$step_script"
  prepare_fathom_review_board_state "$body" "$current_status"

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

  run_fathom_review_board 'approved' 'Closes #12' 'In progress' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-ready' "$board_mutations_file"
  assert_contains 'Issue 12 moved from In progress to Ready to merge' "$output_file"
}

fathom_review_records_findings_as_changes_requested() {
  local output_file="$test_directory/fathom-review-board-changes-output"

  run_fathom_review_board 'changes_requested' 'Fixes #12' 'In progress' "$output_file"

  ((board_status == 0))
  assert_contains 'option=option-changes' "$board_mutations_file"
  assert_contains 'to Changes requested' "$output_file"
}

# A verdict that arrives after the merge must not reopen a finished item, and `Blocked` is the one
# status a hand writes — a review says nothing about whether the issue is waiting on something
# outside the project, so it does not get to erase that statement.
fathom_review_leaves_a_finished_item_alone() {
  local output_file="$test_directory/fathom-review-board-done-output"

  run_fathom_review_board 'approved' 'Closes #12' 'Done' "$output_file"

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'which a review does not overwrite' "$output_file"
}

fathom_review_leaves_a_blocked_item_alone() {
  local output_file="$test_directory/fathom-review-board-blocked-output"

  run_fathom_review_board 'changes_requested' 'Closes #12' 'Blocked' "$output_file"

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'Issue 12 is Blocked' "$output_file"
}

# A bare mention is not a contract, and GitHub closes nothing on one, so neither does this. The
# reviewer's own collection reads the same script, which is what keeps the two readings identical.
fathom_review_moves_nothing_for_a_pull_request_that_closes_no_issue() {
  local output_file="$test_directory/fathom-review-board-unlinked-output"

  run_fathom_review_board 'approved' 'Follows #12 and refactors the loop.' 'Todo' "$output_file"

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

  run_fathom_review_announcement 'Closes #12' 'In progress' "$output_file"

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

    run_fathom_review_announcement 'Closes #12' "$previous_status" "$output_file"

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

    run_fathom_review_announcement 'Closes #12' "$previous_status" "$output_file"

    ((board_status == 0))
    assert_contains 'option=option-review' "$board_mutations_file"
  done
}

fathom_review_writes_no_status_without_the_board_token() {
  local output_file="$test_directory/fathom-review-board-untokened-output"

  run_fathom_review_board 'approved' 'Closes #12' 'In progress' "$output_file" ''

  ((board_status == 0))
  [[ ! -s "$board_mutations_file" ]]
  assert_contains 'BOARD_PROJECT_TOKEN is not set' "$output_file"
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

# Two documents are rendered outside the repository, and `AGENTS.md` splits the links in both of them in two: a page the
# site publishes is linked on the site, at the address that names no version, and everything else is linked in the
# repository. Each half fails in a way nobody would notice from the file — a site address that names no page is a 404
# only a reader meets, and a repository link to a published page silently sends somebody to a Markdown file in a tree
# instead of to the readable form.
documentation_site_address='https://krzysztof318.github.io/MailFathom/'
repository_blob_address='https://github.com/Krzysztof318/MailFathom/blob/main/'

# The root README is the chart listing's overview and the page a reader deciding whether to adopt the project meets;
# `deploy/docker/README.md` is the Docker Hub repository overview. Both are copied out of the repository, so both carry
# absolute links and both are read here.
readonly externally_rendered_readmes=('README.md' 'deploy/docker/README.md')

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

# The index itself. It calls no API, so unlike the gate and the settle loop it needs no `gh` stub and
# no extraction from the workflow: the fixture is a tree on disk and a `files.json` beside it.
create_obligation_fixture() {
  local fixture_root="$1"

  rm -rf "$fixture_root"
  mkdir -p \
    "$fixture_root/src/Application/Emails" \
    "$fixture_root/src/Domain/Failures" \
    "$fixture_root/tests/Application.UnitTests" \
    "$fixture_root/docs/features"

  printf 'internal sealed class MailboxWidget;\n' > "$fixture_root/src/Application/Emails/MailboxWidget.cs"
  printf 'internal sealed class MailboxGadget;\n' > "$fixture_root/src/Application/Emails/MailboxGadget.cs"
  printf 'public void Reads() => new MailboxGadget();\n' \
    > "$fixture_root/tests/Application.UnitTests/MailboxGadgetTests.cs"

  printf '%s\n' \
    '# Mailbox widgets' \
    '' \
    '<!-- describes: src/Application/Emails/** -->' \
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
  printf '%s\n' '[{"filename":"src/Application/Emails/MailboxWidget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[]' '.tests[0].referencing_tests' "$output_file"
  assert_json '"tests/Application.UnitTests"' '.tests[0].expected_test_project' "$output_file"
}

# The case where reporting a missing test would be most obviously wrong: the change adds the class
# and its test together. The added test is not in the base tree, so only the diff can show it, and an
# index that read the tree alone would report a gap the author had already closed.
obligation_index_credits_a_test_the_change_adds() {
  local fixture_root="$test_directory/obligations-added-test"
  local files_json="$test_directory/obligations-added-test-files.json"
  local output_file="$test_directory/obligations-added-test.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"src/Application/Emails/MailboxWidget.cs","status":"added","patch":"@@ -0,0 +1 @@\n+internal sealed class MailboxWidget;"},{"filename":"tests/Application.UnitTests/MailboxWidgetTests.cs","status":"added","patch":"@@ -0,0 +1 @@\n+public void Reads() => new MailboxWidget();"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[{"path":"tests/Application.UnitTests/MailboxWidgetTests.cs","changed_by_this_pull_request":true}]' \
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
  printf '%s\n' '[{"filename":"src/Application/Emails/MailboxGadget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '[{"path":"tests/Application.UnitTests/MailboxGadgetTests.cs","changed_by_this_pull_request":false}]' \
    '.tests[0].referencing_tests' "$output_file"
}

obligation_index_maps_a_changed_path_to_the_page_that_describes_it() {
  local fixture_root="$test_directory/obligations-documentation"
  local files_json="$test_directory/obligations-documentation-files.json"
  local output_file="$test_directory/obligations-documentation.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"src/Application/Emails/MailboxWidget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
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
    '<!-- describes: src/**/*Options.cs, **/*.slnx -->' \
    '' \
    'Every user-settable option.' \
    > "$fixture_root/docs/features/configuration.md"

  # One path directly under the boundary, one nested, and one at the repository root, which is the
  # case a leading `**/` covers.
  printf '%s\n' '[{"filename":"src/MailboxOptions.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"},{"filename":"src/Application/Emails/TimelineOptions.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"},{"filename":"MailFathom.slnx","status":"modified","patch":"@@ -1 +1,2 @@\n+<Solution />"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '"docs/features/configuration.md"' \
    '[.documentation[] | select(.path == "src/MailboxOptions.cs")][0].describing_documents[0].path' \
    "$output_file"
  assert_json '"docs/features/configuration.md"' \
    '[.documentation[] | select(.path == "src/Application/Emails/TimelineOptions.cs")][0].describing_documents[0].path' \
    "$output_file"
  assert_json '"docs/features/configuration.md"' \
    '[.documentation[] | select(.path == "MailFathom.slnx")][0].describing_documents[0].path' \
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
    printf '<!-- describes: src/Domain/Failures/** -->\n'
  } > "$fixture_root/docs/conventions.md"
  printf '%s\n' '[{"filename":"src/Domain/Failures/MailboxFailure.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '0' '.documentation | length' "$output_file"
}

obligation_index_reports_a_moved_pin_with_no_register_row() {
  local fixture_root="$test_directory/obligations-register"
  local files_json="$test_directory/obligations-register-files.json"
  local output_file="$test_directory/obligations-register.json"

  create_obligation_fixture "$fixture_root"
  printf '%s\n' '[{"filename":"Directory.Packages.props","status":"modified","patch":"@@ -1 +1,2 @@\n+<PackageVersion Include=\"Something\" Version=\"1.0.0\" />"}]' \
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
  printf '%s\n' '[{"filename":"Directory.Packages.props","status":"modified","patch":"@@ -1 +1,2 @@\n+<PackageVersion Include=\"Something\" Version=\"1.0.0\" />"},{"filename":"THIRD_PARTY_LICENSES.md","status":"modified","patch":"@@ -1 +1,2 @@\n+| Something | 1.0.0 | MIT |"}]' \
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
      > "$fixture_root/tests/Application.UnitTests/MailboxWidgetCase${index}Tests.cs"
  done

  printf '%s\n' '[{"filename":"src/Application/Emails/MailboxWidget.cs","status":"modified","patch":"@@ -1 +1,2 @@\n+// changed"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '25' '.tests[0].referencing_test_count' "$output_file"
  assert_json '20' '.tests[0].referencing_tests | length' "$output_file"
  assert_json '1' '.notes | length' "$output_file"
}

# Which issues a pull request closes is its stated contract, and merging closes every one of them.
# A reviewer given fewer than GitHub acts on can approve a change that finishes one and leaves
# another closed unread, so which keywords and which spellings count is pinned here rather than left
# to a grep nobody rereads.
run_closing_references() {
  local body="$1" output_file="$2"

  printf '%s\n' "$body" > "$test_directory/closing-body.md"

  bash "$source_repository_root/.github/fathom-review/collect-closing-references.sh" \
    "$test_directory/closing-body.md" 'Krzysztof318/MailFathom' > "$output_file" 2>&1
}

# The superset the labelling pipeline reads, and the reason the two scripts stand side by side: a
# label answers what the change is *about*, so a mention counts, while a closing reference answers
# what merging completes and a mention does not. A change that says "part of #123" against a
# security issue is one somebody wants read that way whether or not it finishes the issue.
run_referenced_issues() {
  local body="$1" output_file="$2" limit="${3:-0}"

  printf '%s\n' "$body" > "$test_directory/referenced-body.md"

  bash "$source_repository_root/.github/pull-request-labels/collect-referenced-issues.sh" \
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
  bash "$source_repository_root/.github/pull-request-labels/collect-referenced-issues.sh" \
    "$test_directory/referenced-body.md" 'Krzysztof318/MailFathom' 2 \
    > "$output_file" 2> "$note_file"

  assert_file_content $'1\n2' "$output_file"
  assert_contains 'refers to 3 issues and this covers the first 2' "$note_file"
}

closing_references_collect_every_issue_the_body_closes() {
  local output_file="$test_directory/closing-references-all"

  run_closing_references \
    $'Closes #265\n\nThis also Fixed #266 and resolves: #270.' \
    "$output_file"

  assert_file_content $'265\n266\n270' "$output_file"
}

# GitHub acts on nine spellings, not the three a body usually uses. One this script missed would
# close its issue on merge with nothing having read what it asked for.
closing_references_match_every_keyword_github_acts_on() {
  local output_file="$test_directory/closing-references-keywords"

  run_closing_references \
    $'close #1\ncloses #2\nclosed #3\nfix #4\nfixes #5\nfixed #6\nresolve #7\nresolves #8\nresolved #9' \
    "$output_file"

  assert_file_content $'1\n2\n3\n4\n5\n6\n7\n8\n9' "$output_file"
}

# A keyword has to stand as its own word. Unanchored, `resolve[sd]?` matches the tail of
# `unresolved` and `fix(e[sd])?` the tail of `prefixes`, so ordinary prose would be read as a closing
# reference the author never wrote — and over-collecting is worse here than missing one, because the
# reviewer then judges the change against an acceptance list nothing obliged it to meet and reports
# it failing a contract that does not exist.
closing_references_ignore_a_keyword_inside_another_word() {
  local output_file="$test_directory/closing-references-word-boundary"

  run_closing_references \
    $'Something unresolved #125 in the design.\nThe pattern prefixes #124 with docs/.\nCloses #200' \
    "$output_file"

  assert_file_content '200' "$output_file"
}

# A bare reference is a mention rather than a contract — GitHub closes nothing on it — and a link to
# another project's issue is one this reviewer cannot fetch and must not hold the change to.
closing_references_ignore_a_mention_and_another_repository() {
  local output_file="$test_directory/closing-references-mentions"

  run_closing_references \
    $'Depends on #123, as #124 describes.\nFixes https://github.com/SomebodyElse/Other/issues/999\nCloses https://github.com/Krzysztof318/MailFathom/issues/271' \
    "$output_file"

  assert_file_content '271' "$output_file"
}

# The ceiling reports what it cut, because the step that applies it promises exactly that of every
# ceiling it defines. A reference nobody was told about is an issue that closes on merge with its
# acceptance list unread, which is the failure the whole collection exists to prevent.
closing_references_report_what_the_ceiling_cut() {
  local output_file="$test_directory/closing-references-ceiling"
  local note_file="$test_directory/closing-references-ceiling-note"

  printf '%s\n' 'Closes #1 closes #2 fixes #3 resolved #4 close #5 fixed #6 resolves #7' \
    > "$test_directory/closing-body.md"

  bash "$source_repository_root/.github/fathom-review/collect-closing-references.sh" \
    "$test_directory/closing-body.md" 'Krzysztof318/MailFathom' 5 \
    > "$output_file" 2> "$note_file"

  assert_file_content $'1\n2\n3\n4\n5' "$output_file"
  assert_contains 'closes 7 issues and this review covers the first 5' "$note_file"
}

# The note is a report of a cut rather than a line the collection always writes, so a body under the
# ceiling produces none. A truncation file that always had content would put a sentence about
# completeness into every review body it appears in.
closing_references_report_nothing_when_the_ceiling_is_not_reached() {
  local output_file="$test_directory/closing-references-under-ceiling"
  local note_file="$test_directory/closing-references-under-ceiling-note"

  printf '%s\n' 'Closes #1 and closes #2' > "$test_directory/closing-body.md"

  bash "$source_repository_root/.github/fathom-review/collect-closing-references.sh" \
    "$test_directory/closing-body.md" 'Krzysztof318/MailFathom' 5 \
    > "$output_file" 2> "$note_file"

  assert_file_content $'1\n2' "$output_file"
  assert_file_content '' "$note_file"
}

closing_references_report_each_issue_once() {
  local output_file="$test_directory/closing-references-duplicates"

  run_closing_references \
    $'Closes #265\n\nAnd again, closes #265.' \
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
    > "$fixture_root/src/Application/Emails/MailboxSprocket.cs"
  git -C "$fixture_root" add src/Application/Emails/MailboxSprocket.cs

  run_review_obligations "$fixture_root" "$output_file"

  assert_contains 'Nothing under tests/ names MailboxSprocket.' "$output_file"
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
    > "$fixture_root/src/Application/Emails/MailboxSprocket.cs"

  run_review_obligations "$fixture_root" "$output_file"

  assert_contains 'src/Application/Emails/MailboxSprocket.cs' "$output_file"
  assert_contains 'Stage them and run this again.' "$output_file"
}

# It reports and never gates. A row is not a finding until somebody confirms it in the file it points
# at, so exiting non-zero on one would turn "look here" into "this is wrong".
review_obligations_reports_without_gating() {
  local fixture_root="$test_directory/review-obligations-exit"
  local output_file="$test_directory/review-obligations-exit-output"

  create_review_obligations_fixture "$fixture_root"

  printf 'internal sealed class MailboxSprocket;\n' \
    > "$fixture_root/src/Application/Emails/MailboxSprocket.cs"
  git -C "$fixture_root" add src/Application/Emails/MailboxSprocket.cs

  run_review_obligations "$fixture_root" "$output_file"
}

# A migration owes no unit test. `AGENTS.md` makes migrations append-only and generated, so an index
# that listed them would put the same wrong finding in front of the reviewer on every schema change.
obligation_index_leaves_migrations_out() {
  local fixture_root="$test_directory/obligations-migration"
  local files_json="$test_directory/obligations-migration-files.json"
  local output_file="$test_directory/obligations-migration.json"

  create_obligation_fixture "$fixture_root"
  mkdir -p "$fixture_root/src/Infrastructure/Persistence/Migrations"
  printf '%s\n' '[{"filename":"src/Infrastructure/Persistence/Migrations/20260802_AddWidget.cs","status":"added","patch":"@@ -0,0 +1 @@\n+// generated"}]' \
    > "$files_json"

  run_obligation_index "$fixture_root" "$files_json" "$output_file"

  assert_json '0' '.tests | length' "$output_file"
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
  local step_script="$test_directory/publish-reference-step.sh"

  extract_publish_reference_step "$step_script"
  : > "$step_output_file"

  (
    export REPOSITORY='Krzysztof318/MailFathom'
    export IMAGE_TAGS="$image_tags"
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
    "$declared_version" > "$fixture_root/Directory.Build.props"
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
      'apply-pull-request-labels.yml pull-requests: write' \
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

  # The gate is what the rest waits for, so it is what the rest is measured against rather than
  # something to measure. Everything else that reads the commit is checked below whatever it is named.
  for job in $release_consumers; do
    [[ "$job" == 'verify' || "$job" == 'integration-tests' ]] && continue

    workflow_job_waits_for "$release_dependencies" "$job" verify ||
      failures+="release.yml: ${job} does not wait for verify. "
    workflow_job_waits_for "$release_dependencies" "$job" integration-tests ||
      failures+="release.yml: ${job} does not wait for integration-tests. "
  done

  for job in $nightly_consumers; do
    [[ "$job" == 'verify' ]] && continue

    workflow_job_waits_for "$nightly_dependencies" "$job" verify ||
      failures+="nightly.yml: ${job} does not wait for verify. "
  done

  # A derived list that derives nothing asserts nothing, and it would do so silently: the loops above
  # are vacuous the moment the `ref:` expression they read is spelled some other way. These three are
  # the floor rather than the coverage.
  for job in schema-artifact cli-binaries publish; do
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
# contribution nobody has reviewed. `fathom-review.yml` uses it deliberately, #189 decided so, and
# `docs/operations/agent-workflow.md` records why the purpose of the rule is still met there — it
# checks out `base.sha`, executes nothing from the contribution, and starts on a maintainer's act
# alone. A second one would be none of that, and this is what stops it appearing unnoticed.
only_the_reviewer_workflow_uses_pull_request_target() {
  local using_workflows

  using_workflows="$(grep -rlE '^[[:space:]]*pull_request_target:' "$source_repository_root/.github/workflows" |
    xargs -r -n1 basename | sort | tr '\n' ' ')"

  if [[ "$using_workflows" != 'fathom-review.yml ' ]]; then
    printf 'pull_request_target is used by: %s(expected fathom-review.yml alone)\n' "$using_workflows" >&2
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

# `tools/` holds development tooling that must never ship. `tools/SyntheticMail` fabricates mail and
# submits it under a stored credential, which is not an operator capability and has no business in
# `mfctl`, in the container image, or in a release asset. Being outside `src/` is what makes that true
# today — the release publishes `src/Cli/Cli.csproj` by name and the image's build context is an
# allow-list — and this is what keeps it true: three ways the boundary could be crossed, each checked
# rather than trusted to a convention nobody restates.
the_development_tooling_never_reaches_a_published_artifact() {
  local failures=''
  local offenders

  # A project under src/ referencing one under tools/ would put the tool in whatever that project
  # publishes, image and command binaries alike.
  offenders="$(grep -rlE 'ProjectReference[^>]*tools[/\\]' "$source_repository_root/src" || true)"

  if [[ -n "$offenders" ]]; then
    failures+="these projects under src/ reference tools/: $(tr '\n' ' ' <<< "$offenders"). "
  fi

  # A workflow *step* naming a path under tools/ would build, publish, or attach it as part of a
  # channel. A `paths-filter` entry is the one legitimate mention and reads as a quoted glob on a
  # list item of its own: it decides which jobs a change starts, which is the opposite of publishing.
  offenders="$(grep -rn 'tools/' "$source_repository_root/.github/workflows" |
    grep -vE ":[[:space:]]*-[[:space:]]*'tools/\*\*'$" |
    grep -vE ":[[:space:]]*#" || true)"

  if [[ -n "$offenders" ]]; then
    failures+="these workflow lines name a path under tools/ outside a change filter: $(tr '\n' ' ' <<< "$offenders"). "
  fi

  # The image's build context is an allow-list, so the tool reaches it only if a line says so.
  if grep -qE '^!/tools' "$source_repository_root/deploy/docker/Dockerfile.dockerignore"; then
    failures+='deploy/docker/Dockerfile.dockerignore admits tools/ into the container build context. '
  fi

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
  [[ -x "$source_repository_root/.github/fathom-review/collect-closing-references.sh" ]]
  [[ -x "$source_repository_root/.github/fathom-review/write-board-status.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request-labels/select-labels.sh" ]]
  [[ -x "$source_repository_root/.github/pull-request-labels/collect-referenced-issues.sh" ]]
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
# them: `<VersionPrefix>` in `Directory.Build.props` is the only version number in this repository.
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

run_test verify_fast_runs_restore_build_tests_and_formatting
run_test verify_full_runs_tests_once_through_coverage
run_test verify_full_runs_workflow_contracts_for_a_change_beyond_csharp
run_test verify_full_skips_workflow_contracts_for_a_csharp_only_change
run_test verify_full_runs_workflow_contracts_when_the_branch_removed_a_path
run_test verify_full_stops_when_workflow_contracts_fail
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
run_test typo_check_passes_the_files_the_pull_request_changed
run_test typo_check_leaves_an_image_out_of_the_file_list
run_test typo_check_checks_nothing_when_a_pull_request_only_changes_images
run_test typo_check_checks_nothing_when_the_pull_request_only_removes_files
run_test typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_whitespace
run_test typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_a_glob_character
run_test typo_check_falls_back_to_the_whole_checkout_for_a_pull_request_beyond_the_reportable_limit
run_test fathom_review_reviews_a_push_to_a_published_pull_request
run_test fathom_review_refuses_a_closed_pull_request
run_test fathom_review_refuses_a_pull_request_the_updater_opened
run_test fathom_review_reviews_an_updater_pull_request_the_maintainer_labelled
run_test fathom_review_collects_at_once_when_nobody_has_commented
run_test fathom_review_waits_before_freezing_a_quiet_conversation
run_test fathom_review_stops_waiting_at_the_ceiling
run_test fathom_review_reads_the_newest_comment_whatever_the_order
run_test fathom_review_collects_the_labels_of_an_issue_the_change_closes
run_test fathom_review_reports_unknown_labels_for_an_issue_it_could_not_fetch
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
run_test fathom_review_moves_a_finding_with_no_line_into_the_body
run_test fathom_review_approves_when_it_finds_nothing
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
run_test every_documentation_page_declares_what_it_describes
run_test every_describes_pattern_matches_something_that_exists
run_test no_documentation_page_carries_the_third_party_notice_twice
run_test every_third_party_notice_sits_directly_under_its_marker
run_test every_published_documentation_page_is_in_a_table_of_contents
run_test every_table_of_contents_entry_names_a_page_that_exists
run_test every_readme_site_link_names_a_page_that_exists
run_test no_readme_link_reaches_a_published_page_through_the_repository
run_test the_docker_hub_overview_fits_what_docker_hub_accepts
run_test referenced_issues_collect_a_mention_as_well_as_a_closing_reference
run_test referenced_issues_collect_a_link_to_an_issue_in_this_repository
run_test referenced_issues_ignore_another_repository
run_test referenced_issues_report_each_issue_once
run_test referenced_issues_report_what_the_ceiling_cut
run_test closing_references_collect_every_issue_the_body_closes
run_test closing_references_match_every_keyword_github_acts_on
run_test closing_references_ignore_a_keyword_inside_another_word
run_test closing_references_ignore_a_mention_and_another_repository
run_test closing_references_report_what_the_ceiling_cut
run_test closing_references_report_nothing_when_the_ceiling_is_not_reached
run_test closing_references_report_each_issue_once
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
run_test obligation_index_leaves_migrations_out
run_test publish_qualifies_every_nightly_tag_with_the_repository_it_resolves
run_test publish_qualifies_the_release_tags_and_ignores_a_blank_line
run_test publish_folds_the_owner_login_into_the_docker_hub_namespace
run_test publish_refuses_a_tag_list_with_nothing_to_publish
run_test release_tag_assertion_accepts_a_tag_that_matches_its_commit
run_test release_tag_assertion_refuses_a_prerelease_tag
run_test release_tag_assertion_refuses_a_lightweight_tag
run_test release_tag_assertion_refuses_a_version_the_commit_does_not_declare
run_test release_tag_assertion_refuses_a_commit_that_never_merged
run_test release_tag_assertion_accepts_a_patch_from_a_release_branch
run_test release_tag_assertion_refuses_a_version_already_released_on_its_line
run_test release_tag_assertion_refuses_an_empty_changelog_section
run_test changelog_section_reading_returns_only_the_requested_release
run_test winget_manifests_name_the_release_assets_they_hash
run_test winget_manifest_names_the_product_and_the_command
run_test winget_manifests_refuse_a_missing_windows_binary
run_test every_external_action_names_an_approved_owner
run_test every_workflow_job_declares_its_permissions
run_test every_write_scope_is_one_the_policy_records
run_test every_checkout_refuses_to_persist_credentials
run_test the_release_restores_the_annotated_tag_before_asserting_it
run_test no_channel_builds_an_artifact_before_the_commit_has_verified
run_test a_paid_provider_run_is_never_the_default
run_test only_the_reviewer_workflow_uses_pull_request_target
run_test a_comment_never_cancels_a_review_in_flight
run_test the_development_tooling_never_reaches_a_published_artifact
run_test workflow_scripts_use_flat_manual_layout
run_test every_yaml_file_carries_the_license_header
run_test every_browser_asset_carries_the_license_header
run_test every_container_unit_carries_the_license_header
run_test every_shell_script_carries_the_license_header
run_test every_skill_declares_its_license

printf '%s passed, %s failed\n' "$passed_count" "$failed_count"

if ((failed_count > 0)); then
  exit 1
fi
