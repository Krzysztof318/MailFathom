#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom


# What a gate has already proved. Sourced by the verification scripts; it defines functions and runs
# nothing on its own.
#
# A gate's expensive steps read the working tree and nothing else about the moment they run, so two
# runs over identical content reach identical verdicts — and the second one costs a Release build,
# the whole test suite, a coverage collection, and a formatting pass to say what the first already
# said. Measured across 150 session transcripts, a quarter of all full-gate runs were exactly that.
# So a gate that passes records a digest of what it read, and a gate handed a digest it already
# recorded reports the earlier run instead of repeating it.
#
# The digest deliberately says nothing about the base branch. Whether the branch still contains the
# current `origin/main` is re-asked on every run of the full gate, before this file is consulted at
# all, so folding the base into the digest would only invalidate a record every time somebody else
# merged — while proving nothing the ancestry check does not already refuse.

# Under `artifacts/`, which `.gitignore` covers, so a record is never staged, never committed, and
# never enters the digest of the tree it describes.
verification_record_directory='artifacts/verify'

# The scripts that decide either verdict. Both gates fold in both lists rather than only their own,
# which is what lets one gate read the other's record: a digest that named the running script would
# describe the runner instead of the content, and the two would never agree about a tree they both
# passed over. Resolved from this file's own location, because the contract suite runs the committed
# scripts against a fixture checkout that carries none of them.
verification_record_scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
verification_input_scripts=(
  "$verification_record_scripts_directory/verify-fast.sh"
  "$verification_record_scripts_directory/verify-full.sh"
  "$verification_record_scripts_directory/resolve-base-remote.sh"
  "$verification_record_scripts_directory/list-branch-changes.sh"
  "$verification_record_scripts_directory/resolve-changed-stacks.sh"
  "$verification_record_scripts_directory/verification-record.sh"
)

# Everything a gate's verdict depends on inside this checkout: the commit, which paths are in which
# state, the content of every tracked change, the content of every file Git is not tracking yet, and
# the scripts above — a change to one of those can move a verdict without touching a single file
# under `backend/src/`.
#
# The content of the *index* is deliberately not in it, only the fact of a staged path, because
# every step a record can skip reads the working tree rather than the index: the build compiles what
# is on disk, and so do the tests and the formatter. The two checks that do read the index —
# `git diff --cached --check` and its unstaged twin — run on every invocation, outside anything a
# record answers for.
compute_verification_digest() {
  {
    git rev-parse HEAD
    git status --porcelain=v1 --untracked-files=all
    git diff HEAD --binary
    # Anything under `artifacts/` is output rather than input — the records below live there, so
    # reading them back in would make every digest describe the last digest. The exclusion is a Git
    # pathspec rather than a filter behind a pipe, because `set -o pipefail` turns a `grep` that
    # matched nothing into a failed digest on every checkout with no untracked file at all.
    git ls-files --others --exclude-standard -z -- ":(exclude)$verification_record_directory" \
      | xargs --null --no-run-if-empty sha256sum
    sha256sum "${verification_input_scripts[@]}"
  } | sha256sum | cut --delimiter=' ' --fields=1
}

# The digest, or nothing at all. A checkout the digest cannot describe — an untracked symlink
# pointing nowhere is the reachable case, since `sha256sum` refuses it — must leave the gate doing
# its work rather than failing on a question nobody asked it. Returning the partial hash the broken
# pipeline still produced would be the one unacceptable answer: it is stable across runs, so a file
# left out of it would stay left out and a record would be honoured that never covered it.
resolve_verification_digest() {
  local digest

  if digest="$(compute_verification_digest)"; then
    printf '%s\n' "$digest"
    return 0
  fi

  printf 'Could not digest this working tree, so this run neither reads a record nor writes one.\n' >&2
}

verification_record_path() {
  printf '%s/%s.digest\n' "$verification_record_directory" "$1"
}

# Whether this gate, or one that subsumes it, already passed over exactly this content.
verification_already_recorded() {
  local gate="$1"
  local digest="$2"
  local record_path
  record_path="$(verification_record_path "$gate")"

  [[ -n "$digest" ]] \
    && [[ -f "$record_path" ]] \
    && [[ "$(head --lines=1 "$record_path")" == "$digest" ]]
}

verification_recorded_at() {
  local gate="$1"
  local record_path
  record_path="$(verification_record_path "$gate")"

  [[ -f "$record_path" ]] && sed --quiet '2p' "$record_path"
}

# Recording is conditional on the tree having stayed still, which is what makes the record mean what
# it says. The fast loop rewrites files, so a run whose formatting pass repaired something verified a
# build and a test suite against content that no longer exists; the next run does the work again
# rather than inheriting a claim about a tree nobody proved. The same check is what would let either
# gate be started in the background beside an editing session.
record_verification() {
  local gate="$1"
  local digest_when_the_run_started="$2"
  local digest_now

  if [[ -z "$digest_when_the_run_started" ]]; then
    return 0
  fi

  digest_now="$(resolve_verification_digest)"

  if [[ "$digest_now" != "$digest_when_the_run_started" ]]; then
    printf 'The working tree changed while %s ran, so nothing is recorded: what passed is not what is here now.\n' \
      "$gate" >&2
    return 0
  fi

  mkdir --parents "$verification_record_directory"
  printf '%s\n%s\n' "$digest_now" "$(date --utc '+%Y-%m-%dT%H:%M:%SZ')" \
    > "$(verification_record_path "$gate")"
}

# What a gate prints instead of running. It names the earlier run rather than only announcing a skip,
# because a reader has to be able to tell evidence already held from a check quietly dropped.
report_verification_already_recorded() {
  local gate="$1"
  local proving_gate="$2"
  local recorded_at

  recorded_at="$(verification_recorded_at "$proving_gate")"

  if [[ "$gate" == "$proving_gate" ]]; then
    printf '%s passed over exactly this content at %s. Nothing has changed since, so its verdict stands and the run is skipped. Run it anyway with VERIFY_FORCE=1.\n' \
      "$gate" "$recorded_at"
  else
    printf '%s passed over exactly this content at %s, and it proves everything %s does. Nothing has changed since, so the run is skipped. Run it anyway with VERIFY_FORCE=1.\n' \
      "$proving_gate" "$recorded_at" "$gate"
  fi
}
