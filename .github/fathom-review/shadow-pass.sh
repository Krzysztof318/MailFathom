#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# The deterministic half of the shadow pass: what is handed to the second model, and what is kept
# of its answer.
#
# The shadow pass asks a cheaper model two questions beside the review and feeds neither answer
# back into it. One checks every changed file against a fixed list of narrow rules; the other
# decides, for every finding the judge published and every reader candidate it dropped, whether the
# change shows the defect. Both answers are kept as numbers, paths, rule identifiers, and booleans
# — never as model prose — so a later comparison against the review can be made from the artifact
# alone, and nothing written by a model reading an untrusted diff reaches a summary anybody renders.
#
# Usage:
#   shadow-pass.sh patches <review-directory>
#   shadow-pass.sh claims <review-directory> <findings.json> <claims.json> <claims-key.json>
#   shadow-pass.sh rules <review-directory> <answer.json> <output-directory>
#   shadow-pass.sh verdicts <claims-key.json> <answer.json> <output-directory>
#
# `rules` and `verdicts` write `rules.json` or `verdicts.json` into the output directory and print a
# Markdown summary on standard output.

set -euo pipefail

command="${1:?a command is required: claims, rules, or verdicts}"
shift

case "$command" in
  patches)
    review_directory="$1"

    # Each patch as a file of its own, one diff line per line. `files.json` holds a patch as one
    # JSON string, which is a single line however long the diff is, and the session's `Read` tool
    # cuts a line short — measured on #2334, every patch past two thousand characters reached the
    # model truncated and the rules pass answered over the first hunk alone.
    jq -r '.[] | select(.patch != null) | .filename' "$review_directory/files.json" |
      while IFS= read -r path; do
        if [[ "$path" == /* || "/$path/" == */../* ]]; then
          continue
        fi
        mkdir -p "$review_directory/patches/$(dirname "$path")"
        jq -r --arg path "$path" '.[] | select(.filename == $path) | .patch' "$review_directory/files.json" \
          > "$review_directory/patches/$path.diff"
      done
    ;;

  claims)
    review_directory="$1"
    findings_file="$2"
    claims_file="$3"
    key_file="$4"

    findings='[]'
    if [[ -s "$findings_file" ]] && jq -e '.findings | type == "array"' "$findings_file" > /dev/null 2>&1; then
      findings="$(jq -c '.findings' "$findings_file")"
    fi

    candidates='[]'
    if compgen -G "$review_directory/candidates/group-*.json" > /dev/null; then
      candidates="$(jq -s -c '[.[] | (.candidates // [])[]]' "$review_directory"/candidates/group-*.json)"
    fi

    # A candidate the judge kept is one a published finding sits on: the same path, and a line
    # inside the finding's range. Every other candidate was dropped, and those are the half of the
    # comparison that says whether the cheaper model would have dropped them too. The source stays
    # in the key alone, so the model judges a claim without knowing which side of the judge it fell.
    #
    # A path the change does not contain is recorded as none, so the one free-text field the summary
    # renders is always a path the collection itself wrote.
    jq -n \
      --argjson findings "$findings" \
      --argjson candidates "$candidates" \
      --slurpfile files "$review_directory/files.json" '
      def kept($candidate):
        any($findings[];
          .path == $candidate.path
          and (.path == null
               or ($candidate.line != null and .line != null
                   and $candidate.line >= (.start_line // .line) and $candidate.line <= .line)));
      ($files[0] | map(.filename)) as $paths
      | def known($path): if $path != null and ($paths | index($path)) != null then $path else null end;
      [ ($findings | to_entries[]
          | {id: "f\(.key + 1)", source: "published", severity: .value.severity,
             path: known(.value.path), line: .value.line, title: .value.title,
             impact: .value.impact, rule: .value.rule}),
        ($candidates | to_entries[] | .value as $candidate | select(kept($candidate) | not)
          | {id: "d\(.key + 1)", source: "dropped", severity: null,
             path: known($candidate.path), line: $candidate.line, title: $candidate.concern,
             impact: $candidate.concern, rule: $candidate.rule}) ]' > "$key_file"

    jq '[.[] | {id, path, line, title, impact, rule}]' "$key_file" > "$claims_file"
    ;;

  rules)
    review_directory="$1"
    answer_file="$2"
    output_directory="$3"
    mkdir -p "$output_directory"

    # A hit on a path the change does not contain is dropped rather than kept, which is also what
    # makes the `path` the one free-text field of the answer safe to render: everything else in it
    # is an enumerated rule, an integer, or nothing.
    jq --slurpfile files "$review_directory/files.json" '
      ($files[0] | map(.filename)) as $paths
      | {files_checked: ([(.files_checked // [])[] | select(. as $path | $paths | index($path))] | unique | length),
         files_changed: ($paths | length),
         hits: [(.hits // [])[] | select(.path as $path | $paths | index($path))]
               | unique_by([.path, .rule, .line])}' \
      "$answer_file" > "$output_directory/rules.json"

    jq -r '
      "### Shadow pass: rules",
      "",
      "Checked \(.files_checked) of \(.files_changed) changed files and found \(.hits | length) hits.",
      (if (.hits | length) > 0 then
         "", "| Rule | Path | Line |", "|---|---|---|",
         (.hits | sort_by(.rule, .path)[] | "| `\(.rule)` | `\(.path)` | \(.line // "—") |")
       else empty end)' \
      "$output_directory/rules.json"
    ;;

  verdicts)
    key_file="$1"
    answer_file="$2"
    output_directory="$3"
    mkdir -p "$output_directory"

    # A verdict naming no claim is dropped, and a claim with no verdict is kept with `holds: null`,
    # so a model that skipped a claim reads as unanswered rather than as agreeing.
    jq --slurpfile answer "$answer_file" '
      ($answer[0].verdicts // [] | map({key: .id, value: .}) | from_entries) as $verdicts
      | [.[] | {id, source, severity, path, line,
                holds: (if $verdicts[.id] then $verdicts[.id].holds else null end),
                confidence: (if $verdicts[.id] then $verdicts[.id].confidence else null end)}]' \
      "$key_file" > "$output_directory/verdicts.json"

    jq -r '
      def tally($source):
        [.[] | select(.source == $source)] as $claims
        | "| \($source) | \($claims | length) | \([$claims[] | select(.holds == true)] | length) | \([$claims[] | select(.holds == false)] | length) | \([$claims[] | select(.holds == null)] | length) |";
      "### Shadow pass: verdicts",
      "",
      "| Claims | Count | Holds | Does not hold | Unanswered |",
      "|---|---|---|---|---|",
      tally("published"),
      tally("dropped"),
      (if length > 0 then
         "", "| Claim | Source | Severity | Path | Line | Holds | Confidence |", "|---|---|---|---|---|---|---|",
         (.[] | "| \(.id) | \(.source) | \(.severity // "—") | `\(.path // "—")` | \(.line // "—") | \(.holds // "—") | \(.confidence // "—") |")
       else empty end)' \
      "$output_directory/verdicts.json"
    ;;

  *)
    printf 'Unknown command: %s\n' "$command" >&2
    exit 2
    ;;
esac
