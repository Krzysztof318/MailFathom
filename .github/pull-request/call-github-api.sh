#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Run one `gh api` call, retrying a transient failure a bounded number of times.
#
# A workflow step runs under `set -euo pipefail`, so a single call that does not reach the API ends
# the job that contains it. `Fathom review` lost a whole run to exactly that: the reply to the first
# call of its collection step was `upstream connect error or disconnect/reset before headers` from
# the proxy in front of the API, which reached `gh` as `invalid character 'u' looking for beginning
# of value` 0.35 seconds in. Nothing about the run was wrong — one request did not arrive — and the
# recovery was a maintainer noticing a red check with no verdict behind it and re-running the job by
# hand. A review that silently does not happen is the failure that pipeline is least able to report
# on itself, so the recovery belongs inside the run.
#
# The bound is the point as much as the retry. Root `AGENTS.md` § *Reliability, security, and
# performance* allows retrying only what is transient and safe to repeat, and this splits those two
# questions between the caller and this script:
#
# - **Safe to repeat** is the caller's. A read is, and so is a mutation that writes a value it
#   already knows; a call that creates a record is not, and the two calls that submit a review are
#   named at their own call sites as the ones that must never come through here.
# - **Transient** is decided below, from what the API said rather than from the fact that something
#   failed. A reply carrying a client error is an answer — the endpoint does not exist, the token
#   cannot see it — and repeating it produces the same answer more slowly. Anything else is either a
#   reply that never arrived or one the server could not produce, and both are worth a second ask.
#
# The whole answer is buffered and printed only once the call has succeeded. That is what makes
# `--paginate` safe to retry: pages stream as they arrive, so a call that failed on the third page
# would otherwise have already written the first two, and the retry would append a second copy of
# them to a filter expecting one record per line.
#
# Usage: call-github-api.sh <arguments to gh api>
#
#   call-github-api.sh "repos/${REPOSITORY}/pulls/${NUMBER}" --jq '.head.sha'
#   call-github-api.sh graphql -f owner="$OWNER" -f query='...'
#
# The arguments are the ones that follow `gh api`, rather than a whole `gh` command line, so nothing
# but an API call can be routed through the bound this script applies.
#
# Environment:
#   API_ATTEMPT_LIMIT          how many attempts a transient failure is worth, including the first
#   API_RETRY_DELAY_SECONDS    the backoff base; each attempt waits double the last, plus jitter

set -euo pipefail

attempt_limit="${API_ATTEMPT_LIMIT:-4}"
retry_delay_seconds="${API_RETRY_DELAY_SECONDS:-2}"

if (( $# == 0 )); then
  echo 'call-github-api.sh needs the arguments to pass to gh api.' >&2
  exit 64
fi

# What the messages below name the call by. It is the endpoint or `graphql`, never the flags, so a
# `-f query=` holding a whole document does not arrive in a log line — and no argument here has ever
# carried a credential, because the token reaches `gh` through the environment.
endpoint="$1"

output_file="$(mktemp)"
error_file="$(mktemp)"

trap 'rm -f "$output_file" "$error_file"' EXIT

# Whether the failure is one a second ask could answer differently.
#
# `gh` reports the status of a reply it received as `(HTTP <code>)`, so the absence of one is the
# case this whole script exists for: the request did not arrive, or what came back was not a reply
# at all. A 4xx is the API answering, and the answer will not change — with the two exceptions that
# are explicitly about trying again later. A 5xx is the API failing to answer.
#
# A failure `gh` reports without a status is retried whatever produced it, which includes a GraphQL
# error and a `--jq` filter that does not compile — both defects that will fail either way, and both
# now four times slower. That is the deliberate trade: telling them apart would mean a second rule
# reading error text, and being slow to fail a broken query is cheaper than being wrong about a
# dropped connection.
is_transient_failure() {
  local http_status

  http_status="$(grep -oE '\(HTTP [0-9]{3}\)' "$error_file" | tail -n 1 | grep -oE '[0-9]{3}')" \
    || http_status=''

  [[ -n "$http_status" ]] || return 0

  case "$http_status" in
    408 | 429 | 5??) return 0 ;;
    *) return 1 ;;
  esac
}

attempt=1
status=0

while true; do
  status=0
  gh api "$@" > "$output_file" 2> "$error_file" || status=$?

  if (( status == 0 )); then
    cat "$output_file"
    cat "$error_file" >&2
    exit 0
  fi

  if (( attempt >= attempt_limit )) || ! is_transient_failure; then
    break
  fi

  # Doubling from the base, with up to one further base of jitter on top, so several calls failing
  # at once do not come back in step. The budget is small and the delays are seconds: this recovers
  # a request that was dropped, and it deliberately cannot wait out an outage.
  delay=$(( (retry_delay_seconds * 2 ** (attempt - 1)) + (RANDOM % (retry_delay_seconds + 1)) ))

  printf 'The GitHub API call to %s failed on attempt %s of %s; retrying in %ss.\n' \
    "$endpoint" "$attempt" "$attempt_limit" "$delay" >&2

  sleep "$delay"
  attempt=$(( attempt + 1 ))
done

cat "$error_file" >&2

# The attempts are reported because the caller's own failure says only that the call did not
# succeed, and whether it was asked once or four times is the difference between an answer from the
# API and an API that could not be reached.
printf 'The GitHub API call to %s failed. Attempts made: %s of %s.\n' \
  "$endpoint" "$attempt" "$attempt_limit" >&2

exit "$status"
