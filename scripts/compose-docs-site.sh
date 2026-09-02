#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Turns a directory of built documentation versions into the site that gets published.
#
#   scripts/compose-docs-site.sh <site-directory>
#
# The argument holds one subdirectory per version, each the output of `scripts/build-docs-site.sh` — `latest` from the
# default branch, and one named after each release tag the site carries. This adds the three files that make them one
# site rather than several:
#
#   versions.json   what the selector in the header offers, and which version the site opens on
#   index.html      the landing page, which sends a reader to that version
#   <page>.html     one redirect per page of that version, mirrored at the site root, so that a page has an address
#                   naming no version — which is what the repository-root README links to
#   llms.txt        that version's map of the documentation, at the address an agent looks for it, with every link in
#                   it resolved into the version directory it came from
#   .nojekyll       what stops GitHub Pages from running the whole site through Jekyll first
#
# **The site opens on the newest release, not on `latest`.** Somebody arriving at the documentation is running a
# release or about to install one, and `latest` describes the default branch — where a page can document a setting no
# published version accepts. `latest` stays in the selector, one click away, and every page outside the default version
# says which version it is and links to the current one.
#
# Nothing here reads the documentation sources. The composition is a function of the directories present, so the same
# site is produced whether it was assembled from one build or from a matrix of them; the one repository file it uses is
# the link rebaser beside it, which is a tool rather than an input.

readonly default_branch_version='latest'
readonly release_version_pattern='^v[0-9]+\.[0-9]+\.[0-9]+$'
readonly map_file='llms.txt'

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"

site_directory="${1-}"

if [[ -z "$site_directory" ]]; then
  printf 'compose-docs-site.sh needs the directory holding the built versions.\n' >&2
  exit 1
fi

if [[ ! -d "$site_directory" ]]; then
  printf 'compose-docs-site.sh found no directory at %s.\n' "$site_directory" >&2
  exit 1
fi

mapfile -t release_versions < <(
  find "$site_directory" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' |
    grep --extended-regexp "$release_version_pattern" |
    sort --version-sort --reverse
)

has_default_branch_version='no'
if [[ -d "$site_directory/$default_branch_version" ]]; then
  has_default_branch_version='yes'
fi

if [[ "${#release_versions[@]}" -eq 0 && "$has_default_branch_version" == 'no' ]]; then
  printf '%s holds no built version, so there is no site to compose.\n' "$site_directory" >&2
  exit 1
fi

# Newest release first, so the selector reads downwards in time, and the default-branch build last: it is the one
# version a reader chooses deliberately rather than lands on.
declare -a ordered_versions=("${release_versions[@]}")
if [[ "$has_default_branch_version" == 'yes' ]]; then
  ordered_versions+=("$default_branch_version")
fi

if [[ "${#release_versions[@]}" -gt 0 ]]; then
  default_version="${release_versions[0]}"
else
  # Before the first release is documented there is nothing else to open on. The selector still renders, with one entry.
  default_version="$default_branch_version"
fi

version_label() {
  if [[ "$1" == "$default_branch_version" ]]; then
    printf 'latest (main)'
  else
    printf '%s' "${1#v}"
  fi
}

for version in "${ordered_versions[@]}"; do
  if [[ ! -f "$site_directory/$version/index.html" ]]; then
    printf '%s carries no index.html, so the selector would offer a version that opens on nothing.\n' "$version" >&2
    exit 1
  fi
done

{
  printf '{\n'
  printf '  "default": "%s",\n' "$default_version"
  printf '  "versions": [\n'

  for index in "${!ordered_versions[@]}"; do
    version="${ordered_versions[index]}"
    separator=','
    if [[ "$index" -eq $((${#ordered_versions[@]} - 1)) ]]; then
      separator=''
    fi

    printf '    { "path": "%s", "label": "%s" }%s\n' "$version" "$(version_label "$version")" "$separator"
  done

  printf '  ]\n'
  printf '}\n'
} > "$site_directory/versions.json"

# The addresses that outlive a release. Every page of the default version is mirrored at the site root as a redirect,
# so `…/MailFathom/operations/mcp-endpoint.html` names a page without naming a version and lands on whichever version
# the site currently opens on. The root `README.md` links that way: a link carrying a version would be wrong the day
# the next one ships, and one carrying `latest` would quietly opt a reader out of the release the site opens on.
#
# The API reference is left out. It is a thousand generated pages whose names are type names, nothing links into it by
# hand, and mirroring it would treble the file count of the site to no end.
write_redirect() {
  local page_path="$1"
  local target="$2"
  local label="$3"

  mkdir --parents "$(dirname "$site_directory/$page_path")"

  # The script carries the fragment and the query across; the refresh below is what happens without one. A meta refresh
  # navigates to exactly the URL it names, and this is a page rather than an HTTP redirect, so nothing appends the
  # fragment the reader arrived with — a link to `…/container-image.html#what-a-nightly-build-risks` would land at the
  # top of the page instead of at the section the README named. `replace` rather than an assignment, so that going back
  # returns to wherever the reader came from rather than to this page, which would send them forward again.
  cat > "$site_directory/$page_path" <<HTML
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>MailFathom documentation</title>
    <link rel="canonical" href="$target">
    <script>
      window.location.replace('$target' + window.location.search + window.location.hash)
    </script>
    <meta http-equiv="refresh" content="0; url=$target">
  </head>
  <body>
    <p>The MailFathom documentation is at <a href="$target">$label</a>.</p>
  </body>
</html>
HTML
}

stub_count=0

while IFS= read -r page_path; do
  # `index.html` at the root is written below and points at the version's front page rather than at its `index.html`,
  # which is the same destination stated as the address a reader would type.
  [[ "$page_path" == 'index.html' ]] && continue

  # One `../` per directory the page sits in, because a static site has no root-relative form that survives being
  # served from a project subpath — `/operations/…` would resolve above `…/MailFathom/` on GitHub Pages.
  ascent=''
  for ((depth = $(tr --delete --complement '/' <<< "$page_path" | wc --chars); depth > 0; depth--)); do
    ascent+='../'
  done

  write_redirect "$page_path" "$ascent$default_version/$page_path" "$default_version/$page_path"
  stub_count=$(( stub_count + 1 ))
done < <(
  cd "$site_directory/$default_version" &&
    find . -name '*.html' -type f -not -path './api/*' -not -name 'toc.html' -printf '%P\n' | sort
)

# The landing page, written last so that it names the version's front page rather than its `index.html` — the same
# destination, stated as the address a reader would type.
write_redirect 'index.html' "$default_version/" "$default_version"

# The map an agent looks for, which it looks for at the site root. Each version carries its own — the default version's
# is the one a reader arriving without a version gets, exactly as the redirects above hand them that version's pages.
#
# It is copied rather than redirected to, because a redirect stub is an HTML page and what this address has to return
# is the text of the map. Copying it moves it one directory up, so every link in it is resolved for that move and the
# root map reaches the default version's own pages. That names a version inside the file, which the stable addresses
# above exist to avoid — and it costs nothing here for the reason it costs everything there: this file is rewritten by
# every publish, while a link written into a README once is read for as long as that README stands.
#
# A release built before the artifacts existed carries no map, and the site opens on the newest release rather than on
# `latest`. So the root map arrives with the first release that carries one, and until then this says so rather than
# failing a publish over a version whose commit could not have written it.
map_at_root='no'

if [[ -f "$site_directory/$default_version/$map_file" ]]; then
  bash "$scripts_directory/rebase-markdown-links.sh" "$default_version" \
    < "$site_directory/$default_version/$map_file" > "$site_directory/$map_file"
  map_at_root='yes'
fi

# Jekyll is what GitHub Pages runs by default, and it drops every path beginning with an underscore. Nothing docfx
# generates needs building, so the whole pass is skipped rather than configured around.
touch "$site_directory/.nojekyll"

printf 'Composed %d version(s) in %s, opening on %s, with %d version-agnostic address(es):\n' \
  "${#ordered_versions[@]}" "$site_directory" "$default_version" "$stub_count"
printf '  %s\n' "${ordered_versions[@]}"

if [[ "$map_at_root" == 'yes' ]]; then
  printf '%s at the site root reads %s.\n' "$map_file" "$default_version"
else
  printf '%s carries no %s, so the site root has none: that release predates the artifact.\n' \
    "$default_version" "$map_file"
fi
