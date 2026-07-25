#!/usr/bin/env bash
set -euo pipefail

tests_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
workflow_directory="$(cd "$tests_directory/.." && pwd -P)"
repository_root="$(cd "$workflow_directory/../.." && pwd -P)"
test_directory="$(mktemp -d)"
fake_bin_directory="$test_directory/bin"
invocation_log="$test_directory/dotnet-invocations.log"
passed_count=0
failed_count=0

cleanup() {
  rm -rf "$test_directory"
}

trap cleanup EXIT

mkdir -p "$fake_bin_directory"
cp "$tests_directory/fake-dotnet.sh" "$fake_bin_directory/dotnet"
chmod +x "$fake_bin_directory/dotnet"

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

  if "$test_name"; then
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
    "$workflow_directory/verify-fast.sh"
  )

  assert_file_content \
    $'restore MailMcp.slnx\nbuild MailMcp.slnx --configuration Release --no-restore\ntest MailMcp.slnx --configuration Release --no-build' \
    "$invocation_log"
}

verify_full_runs_tests_once_through_coverage() {
  : > "$invocation_log"

  (
    cd "$repository_root/src"
    "$workflow_directory/verify-full.sh"
  )

  assert_file_content \
    $'tool restore\nrestore MailMcp.slnx\nbuild MailMcp.slnx --configuration Release --no-restore\nmsbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release\nformat MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic' \
    "$invocation_log"
}

verification_stops_after_first_failure() {
  : > "$invocation_log"

  if (
    export FAKE_DOTNET_FAIL_MATCH='build MailMcp.slnx'
    cd "$repository_root"
    "$workflow_directory/verify-fast.sh"
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
  local before_status="$test_directory/status-before"
  local after_status="$test_directory/status-after"
  local output_file="$test_directory/workspace-output"

  before_head="$(git -C "$repository_root" rev-parse HEAD)"
  git -C "$repository_root" status --porcelain=v2 --branch > "$before_status"

  (
    cd "$repository_root/docs"
    "$workflow_directory/inspect-workspace.sh"
  ) > "$output_file"

  after_head="$(git -C "$repository_root" rev-parse HEAD)"
  git -C "$repository_root" status --porcelain=v2 --branch > "$after_status"

  [[ "$before_head" == "$after_head" ]]
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

run_test verify_fast_runs_restore_build_and_tests
run_test verify_full_runs_tests_once_through_coverage
run_test verification_stops_after_first_failure
run_test workspace_inspection_is_read_only_and_labeled

printf '%s passed, %s failed\n' "$passed_count" "$failed_count"

if ((failed_count > 0)); then
  exit 1
fi

