#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom
#
### Writes the artifacts an AI agent reads into a built version of the documentation site.
#
#   scripts/write-docs-agent-artifacts.sh <version-directory>
#
# The argument is one version of the site — what `scripts/build-docs-site.sh` produced, which calls this last. Three
# things are added beside the rendered pages, and the split between them is what a context window costs:
#
#   llms.txt              the map: every published page, its title, and one line saying what it answers
#   <page>.md             the Markdown source beside each rendered page, so a link from the map fetches text
#   llms-*.txt            one bundle per reader's path through the user guide, each page of it in one file
#
# `docs/` is roughly 1.8 MB of Markdown, so no artifact here carries all of it: the map covers everything and stays
# small enough to load in full, and a bundle covers one reader's path and nothing else. An agent loads the map, reads
# the line beside each page, and fetches the one page that owns the contract it was asked about — which is the answer
# a search over fragments of every page does not give.
#
# **The map is the navigation.** A page's title and its place in the map are the `name:` and the position it already
# has in a `toc.yml`, and the line saying what it answers is a `description:` beside them. That is one file to write
# when a page is added rather than two, and it is why this cannot rot: the section a page belongs to is decided once,
# in the file that decides where the page appears on the site.
#
# **A published page the map would miss fails the build**, here rather than only in `scripts/test-agent-workflow.sh`.
# The map is an artifact somebody's agent answers from, so publishing one that silently omits a page is worse than
# publishing no map at all — the omission reads as *MailFathom does not document that* rather than as a defect.
#
# docs/operations/documentation-site.md states what this emits and what it deliberately leaves out.

set -euo pipefail

readonly documentation_directory='docs'
readonly changelog_page='CHANGELOG.md'
readonly map_file='llms.txt'
readonly link_rebase_script='scripts/rebase-markdown-links.sh'

# The site's landing page, which is the one published page the map does not list. It exists for the site alone and
# says where to start in a browser; an agent that has the map has already arrived, and listing the front door beside
# the rooms would spend a line of the map on a page carrying no contract of its own. It is the same page the
# table-of-contents contracts exempt, for the same reason.
readonly unmapped_page='index.md'

# The user guide serves two readers, and each bundle is the whole of what one of them needs. Neither carries the
# configuration reference the guide links at its end: at 113 KB it is larger than either path and it is a lookup
# rather than a reading, so an agent fetches the page when a question reaches an option.
readonly operator_bundle_file='llms-operator.txt'
readonly operator_bundle_name='The operator path'
readonly operator_bundle_summary='Choosing an installation, getting started, configuring a mailbox at your provider, and administering the deployment.'
readonly operator_bundle_pages=(
  'users/installation.md'
  'users/getting-started.md'
  'users/mailbox-providers.md'
  'users/administering.md'
)

readonly mailbox_user_bundle_file='llms-mailbox-user.txt'
readonly mailbox_user_bundle_name='The mailbox user path'
readonly mailbox_user_bundle_summary='Connecting the chat client you already use, and what each tool returns and bounds.'
readonly mailbox_user_bundle_pages=(
  'users/mcp-clients.md'
  'users/usage.md'
)

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'write-docs-agent-artifacts.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"
repository_root="$(pwd -P)"

version_directory="${1-}"

if [[ -z "$version_directory" ]]; then
  printf 'write-docs-agent-artifacts.sh needs the built version of the site to write into.\n' >&2
  exit 1
fi

if [[ ! -d "$version_directory" ]]; then
  printf 'write-docs-agent-artifacts.sh found no directory at %s.\n' "$version_directory" >&2
  exit 1
fi

version_directory="$(cd "$version_directory" && pwd -P)"

# The set docfx publishes, stated the same way `scripts/test-agent-workflow.sh` states it: everything under `docs/`
# except the architectural decision records, the index GitHub shows for the directory, and the instructions written
# for whoever changes the repository. The repository-root changelog is published beside them, from outside `docs/`.
published_pages() {
  git -C "$repository_root" ls-files -- ':(glob)docs/**/*.md' |
    grep --invert-match '^docs/README\.md$' |
    grep --invert-match '^docs/decisions/' |
    grep --invert-match '/AGENTS\.md$' |
    grep --invert-match '/CLAUDE\.md$' || true

  printf '%s\n' "$changelog_page"
}

# Where a source file is served from within a version. Everything under `docs/` keeps its path with the directory
# stripped, and the changelog — the one published page written outside `docs/` — is served from the version's root,
# which is where docfx puts a content file reached from above the documentation directory.
site_path_of() {
  local repository_path="$1"

  if [[ "$repository_path" == "$documentation_directory/"* ]]; then
    printf '%s' "${repository_path#"$documentation_directory/"}"
  else
    printf '%s' "$(basename "$repository_path")"
  fi
}

# One record per entry of a table of contents, as `name<TAB>href<TAB>description`, in the order the file writes them.
# These files are written by hand in one shape — a `- name:` opening each entry and its fields under it — so reading
# them takes this rather than a YAML parser the repository would otherwise have no use for. An entry with no `href` is
# a group heading, which is the one entry carrying no page, and its own entries follow it exactly as they do on the
# site's sidebar.
table_of_contents_records() {
  awk '
    function flush() {
      if (name != "") {
        printf "%s\t%s\t%s\n", name, href, description
      }

      name = ""
      href = ""
      description = ""
    }

    /^[[:space:]]*-[[:space:]]+name:[[:space:]]*/ {
      flush()
      name = $0
      sub(/^[[:space:]]*-[[:space:]]+name:[[:space:]]*/, "", name)
      next
    }

    /^[[:space:]]*href:[[:space:]]*/ {
      href = $0
      sub(/^[[:space:]]*href:[[:space:]]*/, "", href)
      next
    }

    /^[[:space:]]*description:[[:space:]]*/ {
      description = $0
      sub(/^[[:space:]]*description:[[:space:]]*/, "", description)
      next
    }

    END { flush() }
  ' "$1"
}

# An `href` is resolved against the directory of the table of contents that carries it, which is what lets the user
# guide order the configuration reference without owning it. The result is where the page is served from within the
# version, so the map links the same address the site serves — with `.md` for the source rather than `.html` for the
# rendered page.
resolve_href() {
  local table_of_contents="$1" href="$2" repository_path

  repository_path="$(realpath --canonicalize-missing --relative-to="$repository_root" \
    "$repository_root/$(dirname "$table_of_contents")/$href")"

  site_path_of "$repository_path"
}

mapped_pages_file="$(mktemp)"
trap 'rm --force "$mapped_pages_file"' EXIT

# One blank line between blocks and none inside a list, which takes knowing what the previous block was: a heading and
# a paragraph each open with a blank line, and a list opens with one only where a list is not already running.
map_previous_block=''

# A section is delimited by an `##` heading. A group inside one — the questions `docs/operations/toc.yml` groups its
# pages by — is written as an `###` heading between the list items rather than as a section of its own: it carries no
# page, so anything reading the file by its `##` sections finds every link exactly once either way, and an agent
# reading it keeps the grouping the navigation was written with.
write_map_heading() {
  local heading_prefix="$1" name="$2" description="$3"

  printf '\n%s %s\n' "$heading_prefix" "$name" >> "$version_directory/$map_file"
  map_previous_block='heading'

  if [[ -n "$description" ]]; then
    printf '\n%s\n' "$description" >> "$version_directory/$map_file"
    map_previous_block='paragraph'
  fi
}

# `[name](url): notes` is the whole of an llms.txt file list entry. The line after the colon is what decides whether
# the map is worth loading, so a page reaching it without one stops the build rather than arriving with its title
# repeated at a reader.
write_map_link() {
  local name="$1" target="$2" description="$3" table_of_contents="$4"

  if [[ -z "$description" ]]; then
    printf '%s lists %s with no description, so the map would carry a page and no line saying what it answers.\n' \
      "$table_of_contents" "$name" >&2
    exit 1
  fi

  if [[ "$map_previous_block" != 'list' ]]; then
    printf '\n' >> "$version_directory/$map_file"
  fi

  printf -- '- [%s](%s): %s\n' "$name" "$target" "$description" >> "$version_directory/$map_file"
  map_previous_block='list'

  # What the map claims this version carries, checked against what it does once every entry is written. The bundles
  # are listed the same way and checked the same way: a map naming a bundle this build did not write is the same
  # broken fetch as one naming a page.
  printf '%s\n' "$target" >> "$mapped_pages_file"
}

write_map() {
  local name href description section_table_of_contents

  cat > "$version_directory/$map_file" <<'MAP'
# MailFathom

> MailFathom is a self-hosted service that synchronizes mail from your IMAP accounts into a local PostgreSQL copy,
> indexes it for search, and serves it to AI agents as tools over the Model Context Protocol. Reading is local, no
> tool writes to a mailbox, and synchronization never marks anything read on the mail server. What an agent can write
> is MailFathom's own contact book.

This is the whole of the published documentation for one version of MailFathom, one line per page. Every link below
is a page's Markdown source, served beside the rendered page at the same address with a `.md` extension, so fetching
one returns text rather than a template. The site holds one directory per documented version and this file describes
the one it was fetched from.

The pages do not repeat each other. Each states one contract and the line beside its link says which, so the way to
an answer is to fetch the page that owns it rather than to read the set — which together is roughly 1.8 MB. Where the
question is a whole path rather than a page of it, a bundle below carries that path in one fetch.
MAP

  map_previous_block='paragraph'

  write_map_heading '##' 'Bundles' ''
  write_map_link "$operator_bundle_name" "$operator_bundle_file" "$operator_bundle_summary" \
    'write-docs-agent-artifacts.sh'
  write_map_link "$mailbox_user_bundle_name" "$mailbox_user_bundle_file" "$mailbox_user_bundle_summary" \
    'write-docs-agent-artifacts.sh'

  while IFS=$'\t' read -r name href description; do
    # A header entry naming a directory is a section, and the sidebar beside that directory's pages fills it. One
    # naming a page — the generated API reference, the changelog — is a section holding that page alone, which invents
    # no heading the navigation does not already carry.
    if [[ "$href" != */ ]]; then
      write_map_heading '##' "$name" ''
      write_map_link "$name" "$(resolve_href "$documentation_directory/toc.yml" "$href")" \
        "$description" "$documentation_directory/toc.yml"
      continue
    fi

    if [[ -z "$description" ]]; then
      printf '%s opens the %s section with no description, so the map would head a section and say nothing.\n' \
        "$documentation_directory/toc.yml" "$name" >&2
      exit 1
    fi

    section_table_of_contents="$documentation_directory/${href}toc.yml"
    write_map_heading '##' "$name" "$description"

    while IFS=$'\t' read -r name href description; do
      if [[ -z "$href" ]]; then
        write_map_heading '###' "$name" "$description"
        continue
      fi

      write_map_link "$name" "$(resolve_href "$section_table_of_contents" "$href")" \
        "$description" "$section_table_of_contents"
    done < <(table_of_contents_records "$section_table_of_contents")
  done < <(table_of_contents_records "$documentation_directory/toc.yml")

  write_licensing_footer "$map_file"
}

# The licensing header every file this repository writes carries, in the form these readers parse: prose at the end of
# the document rather than a comment at the top of it, because an llms.txt opens with its heading and whatever reads
# one of these files reads text. It is the same three lines `.editorconfig` applies to the C# sources and a script
# carries under its shebang, and the repository URL is what makes the second line resolvable to somebody holding the
# file alone. The mirrored page sources carry none, for the reason the pages themselves carry none: a copy states what
# the original states.
write_licensing_footer() {
  {
    printf '\n---\n\n'
    printf 'Copyright © 2026 Krzysztof Kasprowicz\n'
    printf 'Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.\n'
    printf 'Project repository: https://github.com/Krzysztof318/MailFathom\n'
  } >> "$version_directory/$1"
}

write_page_sources() {
  local page

  while IFS= read -r page; do
    install -D --mode=644 "$page" "$version_directory/$(site_path_of "$page")"
  done < <(published_pages)
}

# A bundle is read from the version's root rather than from the directory its pages live in, so every relative link in
# it is resolved for that move. A link between two pages of the same bundle therefore names the page's own source
# file rather than a position inside the bundle: the reader already holds both, and a link that resolves for somebody
# who fetched only this file is worth more than one that resolves only inside it.
write_bundle() {
  local bundle_file="$1" bundle_name="$2" bundle_summary="$3"
  shift 3

  local page

  {
    printf '# %s\n\n' "$bundle_name"
    printf '%s\n\n' "$bundle_summary"
    printf 'This is one reading path through the MailFathom user guide, every page of it in one file and in the\n'
    printf 'order the guide walks them. `llms.txt` beside this file maps the whole of the documentation, and every\n'
    printf 'link below is relative to the directory that map sits in.\n'
  } > "$version_directory/$bundle_file"

  for page in "$@"; do
    if [[ ! -f "$documentation_directory/$page" ]]; then
      printf 'The %s bundle names %s, which does not exist.\n' "$bundle_name" "$page" >&2
      exit 1
    fi

    {
      printf '\n<!-- %s -->\n\n' "$page"
      bash "$link_rebase_script" "$(dirname "$page")" < "$documentation_directory/$page"
    } >> "$version_directory/$bundle_file"
  done

  write_licensing_footer "$bundle_file"
}

# Both halves of the same defect, and a reader meets each as an absence rather than as an error: a page the map never
# names is documentation an agent reports as missing, and a map entry naming nothing is a fetch that 404s in the
# middle of an answer.
assert_the_map_and_the_pages_agree() {
  local page target failures=0

  while IFS= read -r page; do
    target="$(site_path_of "$page")"

    [[ "$target" == "$unmapped_page" ]] && continue

    if ! grep --quiet --line-regexp --fixed-strings "$target" "$mapped_pages_file"; then
      printf '%s is published and %s lists no entry for it, so an agent reading the map would not find it\n' \
        "$page" "$map_file" >&2
      failures=$(( failures + 1 ))
    fi
  done < <(published_pages)

  while IFS= read -r target; do
    if [[ ! -f "$version_directory/$target" ]]; then
      printf '%s lists %s, and this version carries no such page\n' "$map_file" "$target" >&2
      failures=$(( failures + 1 ))
    fi
  done < "$mapped_pages_file"

  if (( failures > 0 )); then
    printf '\nThe map is written from the tables of contents under %s/: a page joins the map by joining the\n' \
      "$documentation_directory" >&2
    printf 'navigation of its section, with the line saying what it answers beside it.\n' >&2
    exit 1
  fi
}

write_page_sources
write_map
write_bundle "$operator_bundle_file" "$operator_bundle_name" "$operator_bundle_summary" "${operator_bundle_pages[@]}"
write_bundle "$mailbox_user_bundle_file" "$mailbox_user_bundle_name" "$mailbox_user_bundle_summary" \
  "${mailbox_user_bundle_pages[@]}"
assert_the_map_and_the_pages_agree

printf 'Wrote %s, %s Markdown source(s), and 2 bundle(s) into %s.\n' \
  "$map_file" "$(published_pages | wc --lines)" "$version_directory"
