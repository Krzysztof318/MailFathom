#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom


# Which remote is MailFathom itself. Sourced by the workspace and verification scripts; it defines
# functions and runs nothing on its own.
#
# In the owner's checkout that remote is `origin`, and every gate has always assumed so. In a fork
# `origin` is the fork, whose `main` is whatever the contributor last synced, and the convention
# every Git host documents is a second remote named `upstream`. Assuming `origin` there is the worse
# of the two failures available: the branch either fails the base check with a message naming no fix
# a fork owner can apply, or passes against a base that is not the one it will merge into — a green
# run that proves nothing.
#
# The remote is therefore identified by the repository it points at rather than by its name, so a
# contributor who named it something else is served too, and a checkout whose remotes name no
# MailFathom at all is told what to add instead of being verified against the wrong base.

# Not `readonly`: a second `source` of this file would then abort the caller under `set -e` rather
# than being the no-op a library re-inclusion should be.
CANONICAL_REPOSITORY='Krzysztof318/MailFathom'

# Whether a remote URL names the canonical repository. Handles every form Git accepts — an HTTPS
# URL, an SCP-style `git@host:owner/name`, and a local path — by comparing only the trailing
# `owner/name`, with an optional `.git` suffix and trailing slash removed. GitHub treats an owner and
# a repository name case-insensitively, so this does too; a fork under another owner does not match,
# which is the whole point.
#
# The two segments are split out and compared whole rather than matched as a string suffix, because a
# suffix has no left-hand boundary: `*krzysztof318/mailfathom` also accepts an owner named
# `notkrzysztof318` or `some-krzysztof318`, and the caller would then fetch and compare against that
# remote's `main` — the silent wrong base this whole file exists to prevent. The SCP form separates
# host from path with `:` rather than `/`, so that is normalized first and the split is then one rule
# for every form.
#
# Folded with `tr` rather than with `${x,,}`: that expansion is bash-only, and under any other shell
# it fails the whole function, which would leave the caller reading "no remote names MailFathom" and
# refusing a checkout that was correct. Every caller runs bash today; a gate whose failure mode is
# refusing valid work should not depend on that staying true.
names_canonical_repository() {
  local remote_url="${1%/}"
  local repository_name
  local owner_path
  local owner_name

  remote_url="$(printf '%s' "${remote_url%.git}" | tr '[:upper:]' '[:lower:]' | tr ':' '/')"

  repository_name="${remote_url##*/}"
  owner_path="${remote_url%/*}"
  owner_name="${owner_path##*/}"

  [[ "$owner_name/$repository_name" == "$(printf '%s' "$CANONICAL_REPOSITORY" | tr '[:upper:]' '[:lower:]')" ]]
}

# The remote to resolve the base branch against, or nothing when no remote names MailFathom.
# `upstream` and `origin` are preferred in that order so the answer is stable in a fork that
# configured both; any other name still resolves, because what decides is where the remote points.
resolve_base_remote() {
  local remote_name
  local preferred_name

  for preferred_name in 'upstream' 'origin'; do
    if git remote get-url "$preferred_name" > /dev/null 2>&1 &&
      names_canonical_repository "$(git remote get-url "$preferred_name")"; then
      printf '%s\n' "$preferred_name"
      return 0
    fi
  done

  while read -r remote_name; do
    if names_canonical_repository "$(git remote get-url "$remote_name")"; then
      printf '%s\n' "$remote_name"
      return 0
    fi
  done < <(git remote)

  return 1
}

# What to do about it, written for the person who will read it: a fork owner who has never added the
# second remote. Naming the command is the difference between a gate that blocks and a gate that
# teaches.
base_remote_resolution_hint() {
  printf 'No remote points at %s, so there is no base branch to verify against.\n' "$CANONICAL_REPOSITORY"
  printf 'In a fork, add the upstream repository as a second remote and fetch it:\n'
  printf '  git remote add upstream https://github.com/%s.git\n' "$CANONICAL_REPOSITORY"
  printf '  git fetch upstream main\n'
}

# Prints the base remote once the branch has been proved to contain its current `main`, and returns
# non-zero with the reason on stderr otherwise. Both gates ask this, and both ask it before anything
# else costs anything: a run against a base the branch no longer contains proves nothing about the
# tree a merge would produce, and a rebase is cheapest at the moment the drift appears rather than
# after the work is pushed.
#
# The explicit destination refspec is what makes the ancestor test below meaningful. A bare
# `git fetch <remote> main` only writes FETCH_HEAD, so a repository whose `remote.<remote>.fetch` is
# missing or remapped would keep a stale `refs/remotes/<remote>/main` and pass against it.
#
# Nothing in the pipeline asks this. A pull request whose branch fell behind `main` is not a pull
# request that failed, and failing one for it would turn every merge into a red check on every branch
# open beside it; the `main` ruleset already requires a branch to be current before it merges, which
# is the one place the question belongs there.
require_base_is_contained() {
  local gate_name="$1"
  local base_remote

  if ! base_remote="$(resolve_base_remote)"; then
    base_remote_resolution_hint >&2
    return 1
  fi

  if ! git fetch --quiet "$base_remote" "+refs/heads/main:refs/remotes/$base_remote/main"; then
    printf '%s cannot fetch %s main. Restore access to the remote instead of verifying against a stale base.\n' \
      "$gate_name" "$base_remote" >&2
    return 1
  fi

  if ! git merge-base --is-ancestor "$base_remote/main" HEAD; then
    printf 'HEAD does not contain the current %s/main. Rebase the branch onto the fetched base before verifying.\n' \
      "$base_remote" >&2
    return 1
  fi

  printf '%s\n' "$base_remote"
}
