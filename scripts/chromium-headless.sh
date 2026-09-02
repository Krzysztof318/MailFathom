#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# Snap's /usr/bin/chromium is a wrapper that calls snap-confine, which cannot change AppArmor
# profile from unconfined. The chrome binary on the snap mount does not go through that wrapper.

find_chrome() {
  local candidate

  if [[ -x /snap/chromium/current/usr/lib/chromium-browser/chrome ]]; then
    printf '%s\n' /snap/chromium/current/usr/lib/chromium-browser/chrome
    return 0
  fi

  for candidate in google-chrome-stable google-chrome chromium-browser chromium; do
    if command -v "$candidate" >/dev/null 2>&1; then
      candidate="$(command -v "$candidate")"
      if is_snap_wrapper "$candidate"; then
        continue
      fi

      printf '%s\n' "$candidate"
      return 0
    fi
  done

  echo "chromium-headless: no Chromium binary found that snap-confine is not required to launch." >&2
  echo "Install Chromium outside Snap, or keep the snap package so /snap/chromium/current/usr/lib/chromium-browser/chrome exists." >&2
  return 1
}

is_snap_wrapper() {
  local resolved="$1"

  if resolved="$(readlink -f "$1" 2>/dev/null)"; then
    [[ "$resolved" == /snap/* ]] && return 0
  fi

  head -c 256 "$1" 2>/dev/null | grep -q snap-confine
}

chrome="$(find_chrome)"
user_data="$(mktemp -d "${TMPDIR:-/tmp}/chromium-headless-XXXXXX")"
trap 'rm -rf "$user_data"' EXIT
mkdir -p "$user_data/crashes"

# HOME is pointed at the same directory so Chrome does not write crashpad state into the
# operator's real home. --headless (not --headless=new): dump-dom against the snap binary
# returned no bytes under the new mode. --no-sandbox is required when the process is not confined.
# Not exec: the trap has to remove the user-data directory after Chrome exits.
export HOME="$user_data"
"$chrome" \
  --headless \
  --disable-gpu \
  --no-sandbox \
  --disable-dev-shm-usage \
  --disable-crash-reporter \
  --disable-breakpad \
  --crash-dumps-dir="$user_data/crashes" \
  --user-data-dir="$user_data" \
  "$@"
