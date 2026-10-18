#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# Turn the model's answer to `Duplication` into a warning per duplicate and a job summary, and
# never into a failure.
#
# A pair is reported only when both of its sides are definitions the prefilter listed, copied
# exactly: a pair naming anything else is a definition the model invented or misremembered, and it
# is counted and dropped rather than shown. An empty or unreadable answer is the model step having
# failed, and reads as *no verdict*, because this check fails open.
#
# The model's sentence is text derived from a pull request, so it is reduced to one line, its `|` is
# escaped for the summary table, and `%` is encoded for the workflow command before either sees it.
#
# Usage: report-verdict.sh <landed.tsv> <changed.tsv> <verdict.json> <summary-file>

set -euo pipefail

landed_definitions="${1:?the definitions that landed are required}"
changed_definitions="${2:?the definitions this pull request adds are required}"
verdict_file="${3:?the verdict file is required}"
summary_file="${4:?the summary file is required}"

if ! jq -e '.verdict and (.duplicates | type == "array")' "$verdict_file" > /dev/null 2>&1; then
  echo 'No verdict: the model step failed or returned nothing readable, so nothing is reported.' >> "$summary_file"
  exit 0
fi

pairs="$(
  jq -r --rawfile landed "$landed_definitions" --rawfile changed "$changed_definitions" '
    def listed($tsv; $path; $name): ($tsv | split("\n") | index($path + "\t" + $name)) != null;
    if .verdict == "duplicate" then .duplicates[] else empty end
    | [ (listed($landed; .landed_path; .landed_name) and listed($changed; .changed_path; .changed_name)),
        .changed_path, .changed_name, .landed_path, .landed_name,
        (.what_both_do | gsub("[[:cntrl:]]+"; " ")) ]
    | @tsv
  ' "$verdict_file"
)"

reported=0
dropped=0
rows=''

while IFS=$'\t' read -r is_listed changed_path changed_name landed_path landed_name what_both_do; do
  [[ -n "$is_listed" ]] || continue

  if [[ "$is_listed" != 'true' ]]; then
    dropped=$((dropped + 1))
    continue
  fi

  reported=$((reported + 1))
  message="${changed_name} in ${changed_path}, which this pull request adds, does what ${landed_name} in ${landed_path} already does: ${what_both_do}"
  echo "::warning title=Duplicate definition::${message//%/%25}"
  rows+="| \`${changed_path}\` \`${changed_name}\` | \`${landed_path}\` \`${landed_name}\` | ${what_both_do//|/\\|} |"$'\n'
done <<< "$pairs"

if (( reported == 0 )); then
  echo 'No duplicate: nothing this pull request adds does the job of a definition that landed under it.' >> "$summary_file"
else
  {
    echo '### Definitions this pull request adds that duplicate one that landed under it'
    echo
    echo '| Added by this pull request | Landed under it | What both do |'
    echo '|---|---|---|'
    printf '%s' "$rows"
  } >> "$summary_file"
fi

if (( dropped > 0 )); then
  echo "${dropped} pair(s) the model named were not among the listed definitions and were dropped." >> "$summary_file"
fi
