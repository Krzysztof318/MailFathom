#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# The client half of a parity pair. It serves the client from the fixture corpus and captures the named screens at the
# compositions `frontend/design-parity/screens.json` states — the same file `scripts/capture-design.sh` reads, which is
# what makes the two sides comparable by construction rather than by remembering four numbers.
#
# The corpus reaches a screen through the development server rather than through a built bundle, and that is a decision
# taken elsewhere: `src/Client.App/src/deployment/transportForThisRun.ts` gates it on `import.meta.env.DEV`, so example
# mail cannot travel in a bundle a deployment publishes and a built one has no corpus to answer with. What is captured
# here is therefore the same modules the build compiles, served by Vite instead of from `dist/`.

usage() {
  cat <<'USAGE'
Usage: bash scripts/capture-client.sh --out <directory> [option...]

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

Captures are named `<screen>-<composition>.client.png`, which is what `scripts/compare-captures.sh` pairs
them by. A capture of a client answering a real deployment would be personal data; this one answers the
example corpus under `frontend/tests/fixtures/` and nothing else.
USAGE
}

source "$(dirname "${BASH_SOURCE[0]}")/capture-plan.sh"

# A port of this workspace's own rather than a conventional one, derived the way `frontend/playwright.config.ts`
# derives the browser suite's: several sessions run on this machine at once, each in a worktree of its own, and a run
# that attached to another worktree's development server would report on that worktree's client.
development_port=$((20000 + 16#$(printf '%s' "$repository_root" | sha256sum | cut -c1-4) % 12768))
development_origin="http://127.0.0.1:$development_port"
development_log="$out/development-server.log"

mkdir -p "$out"

pnpm --dir "$repository_root/frontend" --filter @mailfathom/client-app exec \
  vite --mode fixtures --host 127.0.0.1 --port "$development_port" --strictPort >"$development_log" 2>&1 &
development_pid=$!

stop_development_server() {
  kill "$development_pid" 2>/dev/null || true
  wait "$development_pid" 2>/dev/null || true
}

trap stop_development_server EXIT

for _ in $(seq 1 60); do
  if curl --silent --fail --output /dev/null "$development_origin/"; then
    break
  fi

  sleep 1
done

if ! curl --silent --fail --output /dev/null "$development_origin/"; then
  printf 'The development server never answered on %s. Its log:\n' "$development_origin" >&2
  cat "$development_log" >&2
  exit 1
fi

plan="$(
  jq -n \
    --arg out "$out" \
    --arg theme "$theme" \
    --arg origin "$development_origin" \
    --argjson manifest "$(cat "$manifest")" \
    --argjson screens "$(printf '%s\n' "${screens[@]}" | jq -R . | jq -s .)" \
    --argjson compositions "$(printf '%s\n' "${compositions[@]}" | jq -R . | jq -s .)" '
      ($manifest.compositions | INDEX(.name)) as $sizes
      | {
          side: "client",
          outputDirectory: $out,
          theme: $theme,
          clientOrigin: $origin,
          pairs: [
            $manifest.screens[]
            | select(.id | IN($screens[]))
            | . as $screen
            | $screen.compositions[]
            | select(IN($compositions[]))
            | {
                screen: $screen.id,
                composition: $sizes[.],
                route: $screen.route,
                signIn: $screen.signIn,
                steps: ($screen.steps // [])
              }
          ]
        }'
)"

run_capture "$plan"
