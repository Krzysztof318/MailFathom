#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# The design half of a parity pair. It serves the design out of `design/` and captures the named screens at the
# compositions `frontend/design-parity/screens.json` states, which is the same file `scripts/capture-client.sh` reads —
# so the two sides cannot differ in size, pointer or device pixel ratio, whatever either invocation asked for.
#
# The pairing itself is split in two on purpose. The client half is `frontend/design-parity/screens.json`, which
# describes the client and says nothing about the design. The design half — which artboard a screen is drawn on, which
# properties it takes, what is pressed to reach it — describes the design, so it lives beside the artboards in
# `design/parity.json` and moves with them when an artboard is renamed or a state is reached differently.

usage() {
  cat <<'USAGE'
Usage: bash scripts/capture-design.sh --out <directory> [option...]

  --out <directory>        Where the captures are written. The session's own scratch directory, or somewhere
                           under this repository's `artifacts/`. Anything else is refused.
  --screen <id>            A screen from frontend/design-parity/screens.json. Repeatable; every screen when
                           none is named.
  --composition <name>     telefon, fold, tablet or desktop. Repeatable; every composition the screen states
                           when none is named.
  --theme light|dark       Which `prefers-color-scheme` both sides are captured under. Default light.

One invocation produces at most four pairs and refuses rather than truncating, because a screenshot costs a
session context by its pixel dimensions: a parity pass proceeds one screen at a time and reads the report
before it opens an image.

Captures are named `<screen>-<composition>.design.png`, which is what `scripts/compare-captures.sh` pairs
them by.
USAGE
}

source "$(dirname "${BASH_SOURCE[0]}")/capture-plan.sh"

design_root="$repository_root/design"
pairing="$design_root/parity.json"

if [[ ! -f "$design_root/runtime/support.js" ]]; then
  printf 'The design carries no runtime at %s, so an artboard would never boot.\n' "$design_root/runtime/support.js" >&2
  exit 1
fi

if [[ ! -f "$pairing" ]]; then
  printf 'No design pairing at %s.\n' "$pairing" >&2
  printf 'It names, for each screen in %s, the artboard it is drawn on, the component properties it takes and\n' "$manifest" >&2
  printf 'what is pressed to reach it, and it sits beside the artboards under design/.\n' >&2
  exit 1
fi

for screen in "${screens[@]}"; do
  if ! jq -e --arg screen "$screen" '.screens[$screen]' >/dev/null "$pairing"; then
    printf 'The design pairing names no screen %s.\n' "$screen" >&2
    exit 1
  fi
done

plan="$(
  jq -n \
    --arg out "$out" \
    --arg theme "$theme" \
    --arg root "$design_root" \
    --argjson manifest "$(cat "$manifest")" \
    --argjson pairing "$(cat "$pairing")" \
    --argjson screens "$(printf '%s\n' "${screens[@]}" | jq -R . | jq -s .)" \
    --argjson compositions "$(printf '%s\n' "${compositions[@]}" | jq -R . | jq -s .)" '
      ($manifest.compositions | INDEX(.name)) as $sizes
      | {
          side: "design",
          outputDirectory: $out,
          theme: $theme,
          designRoot: $root,
          pairs: [
            $manifest.screens[]
            | select(.id | IN($screens[]))
            | . as $screen
            | $screen.compositions[]
            | select(IN($compositions[]))
            | {
                screen: $screen.id,
                composition: $sizes[.],
                file: $pairing.screens[$screen.id].file,
                properties: ($pairing.screens[$screen.id].properties // {}),
                steps: ($pairing.screens[$screen.id].steps // [])
              }
          ]
        }'
)"

run_capture "$plan"
