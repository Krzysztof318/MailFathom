#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# The design project is read through an MCP server that only an agent session can call, so the two
# halves of a mirror refresh sit in different hands: the session makes the calls, and this script
# does the comparing, the recording and the checking that a session should not be doing by eye.
#
# It never reaches the design server and never writes into the mirror. What it reads is one
# full-depth listing the session captured, and what it decides from it is which paths the session
# still has to read.

usage() {
  cat <<'USAGE'
Usage: bash scripts/design-mirror.sh <command> [<argument>...]

  plan <listing.json>          Compare a full-depth listing against the recorded manifest and
                               print which screen sources have to be read, plus every file that
                               appeared or disappeared anywhere in the project. `-` reads the
                               listing from stdin.
  extract <path> <saved>...    Take the wrapper off one or more saved `read_file` results and
                               write the decoded body to a mirrored path, appending the windows
                               in the order they are named.
  decode <file>                Undo `read_file`'s HTML-entity escaping in place, for a result
                               small enough that it came back inline and was copied by hand.
  record <listing.json>        Check every mirrored file against the size the listing records,
                               then write the manifest and print the stamp the inventory names.
  stamp                        Print the stamp of the recorded manifest without changing anything.

The mirror is `artifacts/design/files/`, the manifest is `artifacts/design/manifest.json`, and
both are ignored by `.gitignore`: a design is not repository content.
USAGE
}

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'Not inside a Git repository.\n' >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  printf 'jq is required to read a listing.\n' >&2
  exit 1
fi

design_directory="$repository_root/artifacts/design"
manifest="$design_directory/manifest.json"
mirror="$(realpath -m -- "$design_directory/files")"

# A screen source is an HTML file, and everything else the project holds is an asset, a generated
# runtime or a thumbnail. Mirroring is therefore one rule rather than a list nobody updates: the
# manifest still covers every file, so an asset appearing or disappearing stays visible without
# several megabytes of it being carried into every worktree.
is_mirrored='(.path | endswith(".html"))'

# A path in the listing is a value a remote server chose, and every command below expands one into a
# filesystem path. `..` in it walks out of the mirror, an absolute one ignores it entirely, and either
# would have this script create or overwrite a file somewhere else on the machine. So the listing is
# confined once, here, rather than at each of the places that go on to use it.
confine() {
  local candidate="$1" resolved
  if [[ -z "$candidate" ]]; then
    printf 'Name a mirrored path under %s.\n' "$mirror" >&2
    exit 1
  fi
  resolved="$(realpath -m -- "$candidate")"
  if [[ "$resolved" != "$mirror" && "$resolved" != "$mirror"/* ]]; then
    printf 'Refusing %s: a mirrored path stays under %s.\n' "$candidate" "$mirror" >&2
    exit 1
  fi
  printf '%s\n' "$resolved"
}

read_listing() {
  local source="$1" listing

  if [[ "$source" == "-" ]]; then
    listing="$(cat)"
  elif [[ -f "$source" ]]; then
    listing="$(cat "$source")"
  else
    printf 'No listing at %s.\n' "$source" >&2
    exit 1
  fi

  if ! jq -e 'type == "array" and (length > 0) and all(has("path") and has("size") and has("etag"))' \
    >/dev/null 2>&1 <<<"$listing"; then
    printf 'That is not a full-depth listing: every entry needs a path, a size and an etag.\n' >&2
    exit 1
  fi

  if jq -e 'any(.[]; .path | (startswith("/")) or (split("/") | any(. == ".." or . == "")))' \
    >/dev/null 2>&1 <<<"$listing"; then
    printf 'That listing carries a path that leaves the mirror; nothing was read or written.\n' >&2
    jq -r '.[] | select(.path | (startswith("/")) or (split("/") | any(. == ".." or . == ""))) | "  \(.path)"' \
      >&2 <<<"$listing"
    exit 1
  fi

  jq -S 'map({path, size, etag}) | sort_by(.path)' <<<"$listing"
}

stamp_of() {
  jq -r "map(select($is_mirrored)) | sort_by(.path) | .[] | \"\(.etag) \(.path)\"" \
    | sha256sum \
    | cut -c1-16
}

command="${1:-}"

case "$command" in
  plan)
    listing="$(read_listing "${2:-}")"

    if [[ ! -f "$manifest" ]]; then
      printf 'No manifest yet — every screen source has to be read:\n'
      jq -r "map(select($is_mirrored)) | .[] | \"  read  \(.path)\"" <<<"$listing"
      exit 0
    fi

    moved="$(jq -n --argjson was "$(cat "$manifest")" --argjson now "$listing" '
      ($was | INDEX(.path)) as $before
      | ($now | INDEX(.path)) as $after
      | ($now | map(select(($before[.path] | not)) | "  new       \(.path)"))
      + ($now | map(select($before[.path] and ($before[.path].etag != .etag)) | "  changed   \(.path)"))
      + ($was | map(select(($after[.path] | not)) | "  gone      \(.path)"))
      | .[]' -r)"

    if [[ -z "$moved" ]]; then
      printf 'Nothing moved: the mirror is the current design (stamp %s).\n' "$(stamp_of <<<"$listing")"
      exit 0
    fi

    printf 'The design moved:\n%s\n\n' "$moved"
    printf 'Read these and rewrite them under artifacts/design/files/:\n'
    jq -n --argjson was "$(cat "$manifest")" --argjson now "$listing" "
      (\$was | INDEX(.path)) as \$before
      | \$now
      | map(select($is_mirrored))
      | map(select((\$before[.path] | not) or (\$before[.path].etag != .etag)))
      | if length == 0 then \"  none — what moved is not a screen source\" else .[] | \"  \(.path)\" end" -r
    ;;

  record)
    listing="$(read_listing "${2:-}")"
    mkdir -p "$mirror"

    # The mirror is checked before the manifest is written, never after. A manifest recorded over a
    # mirror that does not match it is worse than the failure it records: the next session's `plan`
    # reads that manifest, reports that nothing moved, and builds a screen from a file it has no
    # reason to doubt. One loud refusal now, and the previous manifest left standing, is the only
    # shape of this that stays honest.
    failures=0
    while IFS=$'\t' read -r path size; do
      if [[ ! -f "$mirror/$path" ]]; then
        printf 'Missing from the mirror: %s\n' "$path" >&2
        failures=$(( failures + 1 ))
        continue
      fi

      # The listing's own size is the only check available that costs nothing: the server states no
      # hash, and computing one here would mean reading the file the etag exists to avoid reading.
      # A byte count still catches the failure this step actually has, which is a windowed
      # transcription that dropped or doubled a window.
      mirrored_size="$(wc -c <"$mirror/$path" | tr -d ' ')"
      if [[ "$mirrored_size" != "$size" ]]; then
        printf 'Mirrored %s is %s bytes where the project states %s.\n' "$path" "$mirrored_size" "$size" >&2
        failures=$(( failures + 1 ))
      fi
    done < <(jq -r "map(select($is_mirrored)) | .[] | \"\(.path)\t\(.size)\"" <<<"$listing")

    if (( failures > 0 )); then
      printf '\nThe mirror does not match the listing, so no manifest was written.\n' >&2
      exit 1
    fi

    printf '%s\n' "$listing" >"$manifest"
    printf 'Mirror recorded, %s screen sources, stamp %s.\n' \
      "$(jq -r "map(select($is_mirrored)) | length" <<<"$listing")" \
      "$(stamp_of <<<"$listing")"
    ;;

  extract)
    # A `read_file` result too large for the session's own limit is written to a file by the harness
    # instead of being handed back inline. That file is the cheapest mirror there is — the bytes
    # never pass through the model at all — so this takes the wrapper off it and decodes what is
    # left, which is the whole of a screen source's journey onto disk.
    #
    # The wrapper is one opening tag on the first line, a blank line the wrapper adds, the closing
    # tag, and a note after it. Everything between the first line and the closing tag is the file,
    # less that one blank line.
    target="${2:-}"
    shift 2 || true
    if [[ -z "$target" || $# -eq 0 ]]; then
      printf 'Name the mirrored path and at least one saved result.\n' >&2
      exit 1
    fi
    target="$(confine "$target")"

    # Every window is checked before the first byte is written, because the target is usually the
    # mirrored file that is already there: a mistyped second path would otherwise truncate a good
    # copy and leave half of one behind.
    for saved in "$@"; do
      if [[ ! -f "$saved" ]]; then
        printf 'No saved result at %s.\n' "$saved" >&2
        exit 1
      fi
    done

    mkdir -p "$(dirname "$target")"
    : >"$target"
    for saved in "$@"; do
      # A file past the 256 KiB per-call cap comes back as several windows, and they are appended
      # in the order they are named — which is the order a session read them in.
      awk 'NR == 1 { next } /^<\/untrusted-project-content>$/ { exit } { print }' "$saved" \
        | sed -e '${/^$/d}' \
        | sed -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&amp;/\&/g' \
        >>"$target"
    done
    printf 'Extracted %s, %s bytes, %s lines.\n' \
      "$target" "$(wc -c <"$target" | tr -d ' ')" "$(wc -l <"$target" | tr -d ' ')"
    ;;

  decode)
    # `read_file` hands a session HTML-entity-escaped text so the body cannot close the wrapper tag,
    # and the mirror is supposed to hold what the project holds. A session therefore writes the
    # escaped bytes exactly as they arrived — copying rather than re-typing is what keeps a
    # transcription honest — and this undoes the escaping afterwards.
    #
    # The order is the whole of it: `&lt;` and `&gt;` first, `&amp;` last. A body that itself
    # contains `&amp;lt;` arrives as `&amp;amp;lt;`, and one pass in this order returns it to
    # `&amp;lt;` instead of collapsing it to `<`.
    target="$(confine "${2:-}")"
    if [[ ! -f "$target" ]]; then
      printf 'No file at %s.\n' "$target" >&2
      exit 1
    fi
    sed -i -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&amp;/\&/g' "$target"
    printf 'Decoded %s, now %s bytes.\n' "$target" "$(wc -c <"$target" | tr -d ' ')"
    ;;

  stamp)
    if [[ ! -f "$manifest" ]]; then
      printf 'No manifest yet.\n' >&2
      exit 1
    fi
    printf '%s\n' "$(stamp_of <"$manifest")"
    ;;

  ''|-h|--help|help)
    usage
    ;;

  *)
    printf 'Unknown command: %s\n\n' "$command" >&2
    usage >&2
    exit 1
    ;;
esac
