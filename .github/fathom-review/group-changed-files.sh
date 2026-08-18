#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Split a collected change into the groups the reader jobs each take.
#
# Reading the change used to be one session's problem, and the prompt asked that session to split
# `files.json` into groups and launch a subagent per group. Nothing enforced any part of that. A
# session that complied returned in minutes; one that read sequentially spent 28 of the job's 30 on
# an 88-file change; one that decided it had seen enough returned after eight of 42 files with a
# verdict shaped exactly like a complete one. Those are the same defect read three ways: the fan-out
# was a request, so coverage and duration were both properties of what the model chose rather than
# of how the run was built.
#
# This is where that choice stops being the model's. Every changed file is assigned to exactly one
# group here, before any model runs, and the workflow starts one reader job per group. What a reader
# covers is then decided by this file, and a group nobody read is a group missing from the
# candidates, which the coverage step states in the published review.
#
# The grouping is by path, and deliberately so. `AGENTS.md` asks a directory to be one thing, so
# files that sit together are the files that are judged together — one project, one feature, one
# boundary — and a reader given a directory can tell whether the change to it is coherent. A group
# assembled by size alone would put a migration beside a Helm template and ask one reader to hold
# both.
#
# Balance wins where the two disagree, because the run is exactly as long as its slowest reader: a
# split that keeps every directory whole but hands one reader twice the files of another has spent
# the concurrency and kept the duration. So the cuts are made at even intervals and then *pulled* to
# the nearest directory boundary within a quarter of a group, which keeps a directory whole wherever
# doing so costs nothing much and splits one that is simply too large to place.
#
# The split also decides which groups a later pass actually re-reads. `changed-since-last-review.txt`
# names the paths that moved since this App's previous review, and a group holding none of them holds
# nothing this pass may conclude anything about: #844 already bounds a later pass's verdict to what
# moved, so re-reading the rest bought a second reading of files the verdict could not rest on. That
# is the largest cost this workflow carries — a change is reviewed 2.94 times on average, and the
# readers were paying full price for every one of them.
#
# The bound applies to the readers alone. The judge still reads `obligations.json` over the whole
# change, still reads the pull request body twice, and still has `files.json` and `head/` in front of
# it, so nothing that a later pass is *for* moves out of reach.
#
# Usage: group-changed-files.sh <files.json> <groups.json> <max-groups> <target-group-size> [changed-since-last-review.txt]

set -euo pipefail

files_json="${1:?the collected files.json is required}"
output_file="${2:?the output path is required}"
max_groups="${3:?the maximum number of groups is required}"
target_group_size="${4:?the target group size is required}"
# Absent on a first pass, on a review somebody asked for, and where the comparison could not be made
# — all three of which put every group in scope, for the same reason the prompt reads its absence
# that way: a bound nobody could establish must not silence a reader.
changed_since_last_review="${5:-}"

for bound in "$max_groups" "$target_group_size"; do
  if [[ ! "$bound" =~ ^[1-9][0-9]*$ ]]; then
    printf 'The bounds must be positive integers; got max-groups=%s and target-group-size=%s.\n' \
      "$max_groups" "$target_group_size" >&2
    exit 1
  fi
done

file_count="$(jq 'length' "$files_json")"

# A change with no collected file is not an error here. The collection ceiling, an API that returned
# nothing, and a pull request whose files all fell outside the limit each arrive as an empty list,
# and the run has to reach the judge either way so that the review says what happened rather than
# dying with a red job nobody can read a verdict out of.
if (( file_count == 0 )); then
  printf '[]\n' > "$output_file"
  echo 'Grouped 0 changed files into 0 groups.'
  exit 0
fi

# The count is the first of the two bounds to bind, and which one binds says something different in
# each direction. Below the ceiling the groups are the target size and the run is as wide as the
# change needs; at the ceiling the groups grow instead, because the concurrency this workflow may
# spend on one review is a decision about the owner's subscription rather than about this change.
group_count=$(( (file_count + target_group_size - 1) / target_group_size ))

if (( group_count > max_groups )); then
  group_count="$max_groups"
fi

# Sorted by path, which is what puts one directory's files next to each other before anything is
# cut. The collected order is the order the files endpoint happens to return, which means nothing
# about the change.
jq -r 'map(.filename) | sort | .[]' "$files_json" \
  | jq -R -s -c \
      --argjson group_count "$group_count" '
    split("\n")
    | map(select(. != ""))
    | . as $paths
    | ($paths | length) as $count
    # The directory each file sits in. A file at the repository root has none, so it stands as its
    # own boundary rather than joining whatever sorted next to it.
    | ($paths | map(if test("/") then sub("/[^/]*$"; "") else "/" + . end)) as $directories
    # Every position where the file starts a directory the one before it did not belong to. These
    # are the only places a cut may be pulled to.
    | [range(1; $count) | select($directories[.] != $directories[. - 1])] as $boundaries
    # Each cut is measured from where the previous one actually landed rather than from an even
    # interval fixed in advance. A pull that gave one reader three files fewer than the target would
    # otherwise take those three from nobody: the next interval is already spoken for, so the
    # shortfall lands entirely on the following group and the split alternates short, long, short.
    # Re-dividing what is left over the groups that remain is what absorbs it instead.
    | reduce range(1; $group_count) as $group ({start: 0, cuts: []};
        .start as $start
        | (($count - $start) / ($group_count - $group + 1) | ceil) as $size
        # How far a cut may travel to reach a directory boundary. A quarter of a group is the
        # largest pull that still leaves the two readers either side of it within minutes of each
        # other; beyond that the directory is simply too large to place whole, and splitting it
        # costs less than the imbalance would.
        | (($size + 3) / 4 | floor) as $window
        | (.start + $size) as $ideal
        | ([$boundaries[]
            | select(. > $start and . < $count and ((. - $ideal) | fabs) <= $window)]
           | sort_by([((. - $ideal) | fabs), .])
           | .[0] // $ideal) as $cut
        | .cuts += [$cut] | .start = $cut)
    | (.cuts + [$count]) | unique | map(select(. > 0 and . <= $count)) as $cuts
    | reduce $cuts[] as $cut ({start: 0, groups: []};
        .groups += [$paths[.start:$cut]] | .start = $cut)
    | .groups
    | map(select(length > 0))
    | to_entries
    | map({index: (.key + 1), files: .value})
  ' > "$output_file"

# Which groups this pass re-reads. Every group when nothing bounds the pass, and otherwise the ones
# holding at least one path that moved since the last review.
if [[ -n "$changed_since_last_review" && -f "$changed_since_last_review" ]]; then
  moved_paths="$(jq -R -s -c 'split("\n") | map(select(. != ""))' < "$changed_since_last_review")"
else
  moved_paths='null'
fi

grouped_file="$(mktemp)"
jq --argjson moved "$moved_paths" '
  map(. + {read_this_pass: (
    if $moved == null then true
    else ((.files - ($moved | map(select(. != "")))) | length) < (.files | length)
    end)})
' "$output_file" > "$grouped_file"
mv "$grouped_file" "$output_file"

grouped="$(jq '[.[].files | length] | add // 0' "$output_file")"

# Every collected file reaches exactly one group, or the split is wrong and the run must not spend a
# model on it: a file silently dropped here is a file no reader is given and no coverage line can
# report, which is the failure this whole mechanism replaces.
if (( grouped != file_count )); then
  printf 'The split assigned %s of the %s collected files, so the grouping is not exhaustive.\n' \
    "$grouped" "$file_count" >&2
  exit 1
fi

reading="$(jq '[.[] | select(.read_this_pass)] | length' "$output_file")"
skipped_files="$(jq '[.[] | select(.read_this_pass | not) | .files | length] | add // 0' "$output_file")"

printf 'Grouped %s changed files into %s groups of at most %s; %s of them are read this pass.\n' \
  "$file_count" "$(jq 'length' "$output_file")" "$(jq '[.[].files | length] | max' "$output_file")" \
  "$reading"

if (( skipped_files > 0 )); then
  printf '%s changed files have not moved since the last review and are not re-read.\n' "$skipped_files"
fi
