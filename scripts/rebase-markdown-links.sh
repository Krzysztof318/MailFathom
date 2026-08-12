#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom
#
### Rewrites the relative links of a Markdown document that has moved up out of its own directory.
#
#   scripts/rebase-markdown-links.sh <source-directory> < page.md > rebased.md
#
# The document on standard input lived in `<source-directory>`; what comes out is the same document read from the
# directory that path is relative to. A link is therefore resolved rather than merely prefixed — `../operations/x.md`
# in a page of `users/` becomes `operations/x.md`, and `installation.md` beside it becomes `users/installation.md`.
#
# Two callers move a document that way, and they are why this is a script of its own rather than the same twenty lines
# of awk written twice. `scripts/write-docs-agent-artifacts.sh` concatenates pages of `users/` into a bundle that sits
# at a version's root, and `scripts/compose-docs-site.sh` copies the default version's map to the site root, where
# every link has to reach back into that version's directory. The second case looks like prefixing and is the same
# operation: a document moving out of `v0.5.0/` is a document whose links resolve against `v0.5.0/`.
#
# Only a relative link is touched. An absolute URL, a scheme of any kind, a root-relative path, and a bare fragment
# are left exactly as written, which is what keeps the rule the site already applies to a page's links — an absolute
# `https://github.com/Krzysztof318/MailFathom` URL for anything the site does not carry — true of the artifacts too.
# A fragment on a relative link travels with it, so a link into a section of another page still lands on that section.

set -euo pipefail

source_directory="${1-}"

if [[ -z "$source_directory" ]]; then
  printf 'rebase-markdown-links.sh needs the directory the document on standard input came from.\n' >&2
  exit 1
fi

# The whole rewrite is one pass over each line. A Markdown inline link is `](target)`, and a target carrying a closing
# parenthesis would end it early — no page here writes one, and a link that did would be left visibly wrong rather
# than silently redirected, which is the failure worth having of the two.
awk -v source_directory="$source_directory" '
function normalize(path,   segments, count, stack, top, position, result) {
  count = split(path, segments, "/")
  top = 0

  for (position = 1; position <= count; position++) {
    if (segments[position] == "" || segments[position] == ".") {
      continue
    }

    if (segments[position] == "..") {
      if (top > 0) {
        top--
      }

      continue
    }

    stack[++top] = segments[position]
  }

  result = ""

  for (position = 1; position <= top; position++) {
    result = result (position > 1 ? "/" : "") stack[position]
  }

  return result
}

function rebase(target,   fragment, position) {
  # A scheme, a protocol-relative address, a root-relative path, and a bare fragment all resolve without the directory
  # the document sat in, so each is already what it will be after the move.
  if (target ~ /^([A-Za-z][A-Za-z0-9+.-]*:|\/|#)/) {
    return target
  }

  fragment = ""
  position = index(target, "#")

  if (position > 0) {
    fragment = substr(target, position)
    target = substr(target, 1, position - 1)
  }

  if (target == "") {
    return fragment
  }

  return normalize(source_directory "/" target) fragment
}

{
  remainder = $0
  rebased = ""

  while (match(remainder, /\]\([^)]*\)/)) {
    rebased = rebased substr(remainder, 1, RSTART - 1) "](" rebase(substr(remainder, RSTART + 2, RLENGTH - 3)) ")"
    remainder = substr(remainder, RSTART + RLENGTH)
  }

  print rebased remainder
}
'
