#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Turns a composed documentation site into the whole tree GitHub Pages serves.
#
#   scripts/compose-pages-site.sh <site-directory>
#
# The argument already holds `docs/`, composed by `scripts/compose-docs-site.sh`. This adds what sits beside it:
#
#   design/             the repository's `design/` as it stands, so an artboard opens with its data, logic, and styles
#   design/index.html   a plain list of the artboard pages, each linking to the page itself
#   index.html          the site root, which sends a reader to `docs/`
#   .nojekyll           what stops GitHub Pages from running the whole tree through Jekyll first
#
# The design is copied whole rather than selected, because an artboard is a page that loads its runtime, its data, and
# its logic by relative path, and copying the directory is what keeps every one of those paths true on the site.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"

site_directory="${1-}"

if [[ -z "$site_directory" ]]; then
  printf 'compose-pages-site.sh needs the directory holding the composed site.\n' >&2
  exit 1
fi

if [[ ! -f "$site_directory/docs/index.html" ]]; then
  printf '%s carries no docs/index.html, so compose-docs-site.sh has not run on %s/docs.\n' \
    "$site_directory" "$site_directory" >&2
  exit 1
fi

design_directory="$site_directory/design"

rm -rf "$design_directory"
cp -R "$repository_root/design" "$design_directory"

mapfile -t design_pages < <(find "$design_directory" -maxdepth 1 -name '*.html' -type f -printf '%f\n' | sort)

if [[ "${#design_pages[@]}" -eq 0 ]]; then
  printf '%s holds no artboard page, so the design index would list nothing.\n' "$repository_root/design" >&2
  exit 1
fi

{
  cat <<'HTML'
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>MailFathom design</title>
  </head>
  <body>
    <h1>MailFathom design</h1>
    <ul>
HTML

  for page in "${design_pages[@]}"; do
    printf '      <li><a href="%s">%s</a></li>\n' "$page" "$page"
  done

  cat <<'HTML'
    </ul>
  </body>
</html>
HTML
} > "$design_directory/index.html"

cat > "$site_directory/index.html" <<'HTML'
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>MailFathom</title>
    <link rel="canonical" href="docs/">
    <meta http-equiv="refresh" content="0; url=docs/">
  </head>
  <body>
    <p>The MailFathom documentation is at <a href="docs/">docs</a>.</p>
  </body>
</html>
HTML

# Jekyll is what GitHub Pages runs by default, and it drops every path beginning with an underscore. Nothing here needs
# building, so the whole pass is skipped rather than configured around.
touch "$site_directory/.nojekyll"

printf 'Composed the Pages site in %s: docs/, and design/ listing %d page(s):\n' \
  "$site_directory" "${#design_pages[@]}"
printf '  %s\n' "${design_pages[@]}"
