#!/usr/bin/env bash
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
remote_repository_root="$test_directory/remote"
fake_bin_directory="$test_directory/bin"
protected_paths_bin_directory="$test_directory/protected-paths-bin"
typo_check_bin_directory="$test_directory/typo-check-bin"
fathom_review_bin_directory="$test_directory/fathom-review-bin"
settle_bin_directory="$test_directory/fathom-review-settle-bin"
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

verify_fast_runs_restore_build_tests_and_formatting() {
  : > "$invocation_log"

  (
    cd "$repository_root/tests"
    "$scripts_directory/verify-fast.sh"
  )

  assert_file_content \
    $'restore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\ntest --solution MailFathom.slnx --configuration Release --no-build\nformat MailFathom.slnx --no-restore --include src/Sample.cs\nformat MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic --include src/Sample.cs' \
    "$invocation_log"
}

verify_full_runs_tests_once_through_coverage() {
  : > "$invocation_log"

  (
    cd "$repository_root/src"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content \
    $'tool restore\nrestore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\nmsbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release\nformat MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic' \
    "$invocation_log"
}

verify_full_runs_workflow_contracts() {
  : > "$workflow_invocation_log"

  (
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
}

verify_full_stops_when_workflow_contracts_fail() {
  : > "$invocation_log"
  : > "$workflow_invocation_log"

  if (
    export FAKE_WORKFLOW_FAIL=1
    cd "$repository_root"
    "$scripts_directory/verify-full.sh"
  ); then
    printf 'verify-full.sh succeeded despite failing workflow contracts\n' >&2
    return 1
  fi

  assert_file_content 'workflow-contracts' "$workflow_invocation_log"
  assert_file_content '' "$invocation_log"
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
  git -C "$repository_root" remote set-url origin "$test_directory/missing-remote"

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
    $'restore MailFathom.slnx --locked-mode\nbuild MailFathom.slnx --configuration Release --no-restore\ntest --solution MailFathom.slnx --configuration Release --no-build\nformat MailFathom.slnx --no-restore --include src/Sample.cs\nformat MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic --include src/Sample.cs' \
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
  assert_contains 'Contains origin/main:' "$output_file"
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
extract_fathom_review_step() {
  local step_id="$1"
  local step_script="$2"

  awk -v step_declaration="        id: $step_id" '
    $0 == step_declaration { found = 1; next }
    found && !extracting && /^        run: \|$/ { extracting = 1; next }
    extracting {
      if ($0 != "" && $0 !~ /^          /) { exit }
      sub(/^          /, "")
      print
    }
  ' "$source_repository_root/.github/workflows/fathom-review.yml" > "$step_script"

  [[ -s "$step_script" ]]
  bash -n "$step_script"
}

# The decision is read from `GITHUB_OUTPUT`, which is where the reviewing job reads it, rather than
# from the log line beside it that no other job consumes.
run_fathom_review_gate() {
  local event_action="$1"
  local output_file="$2"
  local step_output_file="$3"
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
    export HEAD_REPOSITORY='Krzysztof318/MailFathom'
    export ADDED_LABEL=''
    export IS_PULL_REQUEST_COMMENT='false'
    export COMMENT_BODY=''
    export COMMENT_ASSOCIATION=''
    export GH_TOKEN='fake-token'
    export REVIEWER_LOGIN='fathom-reviewer[bot]'
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
  assert_contains 'primary-reference=ghcr.io/krzysztof318/mailfathom:0.1.0-nightly.12-616d0a6' "$step_output_file"
  assert_file_content \
    $'ghcr.io/krzysztof318/mailfathom:0.1.0-nightly.12-616d0a6\nghcr.io/krzysztof318/mailfathom:nightly' \
    "$references_file"
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
    $'ghcr.io/krzysztof318/mailfathom:0.1.0\nghcr.io/krzysztof318/mailfathom:latest' \
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

workflow_scripts_use_flat_manual_layout() {
  [[ -x "$source_repository_root/scripts/inspect-workspace.sh" ]]
  [[ -x "$source_repository_root/scripts/assert-release-tag.sh" ]]
  [[ -x "$source_repository_root/scripts/read-changelog-section.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-fast.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-full.sh" ]]
  [[ -x "$source_repository_root/scripts/test-agent-workflow.sh" ]]
  [[ ! -e "$source_repository_root/eng/agent-workflow" ]]

  # `Fathom review` invokes this one directly rather than through `bash`, so the mode git records is
  # part of the contract. The tests above run it through `bash` and would pass without it.
  [[ -x "$source_repository_root/.github/fathom-review/index-obligations.sh" ]]
}

run_test verify_fast_runs_restore_build_tests_and_formatting
run_test verify_full_runs_tests_once_through_coverage
run_test verify_full_runs_workflow_contracts
run_test verify_full_stops_when_workflow_contracts_fail
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
run_test protected_paths_allows_a_change_that_touches_nothing_protected
run_test protected_paths_refuses_a_contributor_changing_repository_root_configuration
run_test protected_paths_refuses_a_contributor_changing_a_protected_directory
run_test protected_paths_matches_the_configuration_files_at_every_depth
run_test protected_paths_ignores_paths_that_only_resemble_a_protected_one
run_test protected_paths_reports_the_paths_it_found_when_the_owner_is_the_author
run_test protected_paths_refuses_a_pull_request_larger_than_the_reportable_limit
run_test typo_check_passes_the_files_the_pull_request_changed
run_test typo_check_checks_nothing_when_the_pull_request_only_removes_files
run_test typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_whitespace
run_test typo_check_falls_back_to_the_whole_checkout_for_a_path_containing_a_glob_character
run_test typo_check_falls_back_to_the_whole_checkout_for_a_pull_request_beyond_the_reportable_limit
run_test fathom_review_reviews_a_push_to_a_published_pull_request
run_test fathom_review_refuses_a_closed_pull_request
run_test fathom_review_collects_at_once_when_nobody_has_commented
run_test fathom_review_waits_before_freezing_a_quiet_conversation
run_test fathom_review_stops_waiting_at_the_ceiling
run_test fathom_review_reads_the_newest_comment_whatever_the_order
run_test every_documentation_page_declares_what_it_describes
run_test every_describes_pattern_matches_something_that_exists
run_test obligation_index_reports_a_changed_source_no_test_reaches
run_test obligation_index_credits_a_test_the_change_adds
run_test obligation_index_names_a_test_the_change_left_alone
run_test obligation_index_maps_a_changed_path_to_the_page_that_describes_it
run_test obligation_index_ignores_a_marker_below_the_preamble
run_test obligation_index_reports_a_moved_pin_with_no_register_row
run_test obligation_index_records_a_register_the_change_updated
run_test obligation_index_caps_the_tests_it_lists_for_one_type
run_test obligation_index_leaves_migrations_out
run_test publish_qualifies_every_nightly_tag_with_the_repository_it_resolves
run_test publish_qualifies_the_release_tags_and_ignores_a_blank_line
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
run_test workflow_scripts_use_flat_manual_layout

printf '%s passed, %s failed\n' "$passed_count" "$failed_count"

if ((failed_count > 0)); then
  exit 1
fi
