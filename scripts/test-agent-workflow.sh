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
fake_bin_directory="$test_directory/bin"
invocation_log="$test_directory/dotnet-invocations.log"
passed_count=0
failed_count=0

cleanup() {
  rm -rf "$test_directory"
}

trap cleanup EXIT

mkdir -p \
  "$fake_bin_directory" \
  "$repository_root/docs" \
  "$repository_root/src" \
  "$repository_root/tests"
ln -s "$scripts_directory/test-agent-workflow.sh" "$fake_bin_directory/dotnet"

git -C "$repository_root" init --initial-branch=main --quiet
git -C "$repository_root" config user.email agent-workflow@example.invalid
git -C "$repository_root" config user.name 'Agent Workflow Tests'
printf '<Solution />\n' > "$repository_root/MailMcp.slnx"
printf 'clean\n' > "$repository_root/tracked.txt"
git -C "$repository_root" add MailMcp.slnx tracked.txt
git -C "$repository_root" commit --quiet -m 'test fixture'
git -C "$repository_root" update-ref refs/remotes/origin/main HEAD

export FAKE_DOTNET_LOG="$invocation_log"
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

  if ! grep -Fq "$expected_text" "$actual_file"; then
    printf 'Expected %s to contain: %s\n' "$actual_file" "$expected_text" >&2
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

verify_fast_runs_restore_build_and_tests() {
  : > "$invocation_log"

  (
    cd "$repository_root/tests"
    "$scripts_directory/verify-fast.sh"
  )

  assert_file_content \
    $'restore MailMcp.slnx\nbuild MailMcp.slnx --configuration Release --no-restore\ntest --solution MailMcp.slnx --configuration Release --no-build' \
    "$invocation_log"
}

verify_full_runs_tests_once_through_coverage() {
  : > "$invocation_log"

  (
    cd "$repository_root/src"
    "$scripts_directory/verify-full.sh"
  )

  assert_file_content \
    $'tool restore\nrestore MailMcp.slnx\nbuild MailMcp.slnx --configuration Release --no-restore\nmsbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release\nformat MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic' \
    "$invocation_log"
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

verification_stops_after_first_failure() {
  : > "$invocation_log"

  if (
    export FAKE_DOTNET_FAIL_MATCH='build MailMcp.slnx'
    cd "$repository_root"
    "$scripts_directory/verify-fast.sh"
  ); then
    printf 'verify-fast.sh succeeded despite the configured build failure\n' >&2
    return 1
  fi

  assert_file_content \
    $'restore MailMcp.slnx\nbuild MailMcp.slnx --configuration Release --no-restore' \
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

workflow_scripts_use_flat_manual_layout() {
  [[ -x "$source_repository_root/scripts/inspect-workspace.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-fast.sh" ]]
  [[ -x "$source_repository_root/scripts/verify-full.sh" ]]
  [[ -x "$source_repository_root/scripts/test-agent-workflow.sh" ]]
  [[ ! -e "$source_repository_root/eng/agent-workflow" ]]
}

run_test verify_fast_runs_restore_build_and_tests
run_test verify_full_runs_tests_once_through_coverage
run_test verify_full_checks_committed_staged_and_unstaged_changes
run_test verification_stops_after_first_failure
run_test workspace_inspection_is_read_only_and_labeled
run_test workspace_inspection_reports_unavailable_sdk
run_test workflow_scripts_use_flat_manual_layout

printf '%s passed, %s failed\n' "$passed_count" "$failed_count"

if ((failed_count > 0)); then
  exit 1
fi
