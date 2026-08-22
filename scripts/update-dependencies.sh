#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Reads every version this repository pins against the upstream that publishes it, and against the terms
### THIRD_PARTY_LICENSES.md recorded when that version was reviewed.
#
# Usage:
#   scripts/update-dependencies.sh                      survey every pin and report; writes nothing
#   scripts/update-dependencies.sh --apply              and rewrite the pins that can be written mechanically
#   scripts/update-dependencies.sh --apply --verify     and then run scripts/verify-full.sh over the result
#   scripts/update-dependencies.sh --only <family>      restrict to one of: nuget, tools, sdk, actions, images
#
# A version reaches a run from one of six files, in five syntaxes, and nothing until now read them together. The
# question this answers is the one no tooling here answers otherwise: which pins are behind, and did any of them change
# licence since somebody reviewed it. Dependabot version updates were removed deliberately in #304/#305 and are not
# coming back — the pull request they produced carried a version number while the work that costs something, reading
# what the upstream now declares and deciding whether the register row is still true, stayed manual either way. This is
# that half. Dependabot *alerts* remain on and answer a different question: they fire on a published advisory, never on
# a component merely being behind.
#
# It reports and never gates, the same contract `scripts/review-obligations.sh` carries. Nothing it prints is a finding
# until it is confirmed at the source it names: a licence is read from the upstream's own metadata for the version that
# is current *now*, and the register side is read out of prose by pattern, so a disagreement is a place to look rather
# than a conclusion. Exiting non-zero on a row would turn "read this" into "this is wrong".
#
# Two pin families are surveyed and never rewritten, and the report says so where they appear:
#
#   * `global.json`'s `sdk.version` is a floor rather than a version. `rollForward: latestFeature` means the toolchain a
#     run executes is chosen on the machine, so moving the floor changes what the gates *require* rather than what they
#     use, and that is a decision about who can build this repository.
#   * A container image pin is written in up to four assets in four syntaxes — a Compose default, a Helm value split
#     across `repository` and `tag`, a Quadlet unit source, and an AppHost call — and two of them are digests. Moving
#     one also obliges the golden manifests under `deploy/helm/mailfathom/ci/golden/`, which only
#     `scripts/render-helm-manifests.sh --update` may write. The report names every file carrying each reference so the
#     blast radius is visible; the edit stays a human one.
#
# It never edits `THIRD_PARTY_LICENSES.md` either. A row there is a completed review written as prose — what a component
# is used for, what its terms oblige, which of them a distribution has to discharge — and a machine cannot restate one.
# What `--apply` does instead is print, for every pin it moved, the register lines that name the version it moved from,
# by line number, so the prose edit is guided rather than searched for.
#
# Network access is required, to nuget.org, to the .NET release index, to GitHub through `gh`, and to the three
# registries the images live in. Anything that does not resolve is reported as `unresolved` beside the pin rather than
# failing the run, because a survey that stops at the first unreachable host is worth less than a partial one.

readonly usage='Usage: scripts/update-dependencies.sh [--apply] [--verify] [--only nuget|tools|sdk|actions|images]'

# Ordered longest first, because a shorter alternative that is a prefix of a longer one would otherwise win: without it
# `Apache-2.0` in a register row reads as no match at all and `BSD-3-Clause` reads as `BSD`. `PostgreSQL License` sits
# ahead of the bare `PostgreSQL` for the same reason — the register writes the licence's full name where a package's own
# metadata declares the SPDX identifier, and both have to resolve to the same thing.
readonly licence_pattern='Apache-2\.0|BSD-3-Clause|BSD-2-Clause|PostgreSQL License|LGPL-[0-9.]+(-or-later|-only)?|NOASSERTION|PostgreSQL|MPL-2\.0|CC-BY-[0-9.]+|Unicode|MS-PL|MIT|ISC|CC0'

readonly register_file='THIRD_PARTY_LICENSES.md'
readonly global_json='global.json'
readonly tool_manifest='.config/dotnet-tools.json'
readonly workflow_directory='.github/workflows'
readonly backend_pins='backend/Directory.Packages.props'
readonly frontend_pins='frontend/Directory.Packages.props'
readonly backend_solution='backend/MailFathom.slnx'
readonly frontend_solution='frontend/MailFathom.Client.slnx'

apply_pins='false'
run_verification='false'
selected_family='all'

while [[ $# -gt 0 ]]; do
  case "$1" in
    --apply) apply_pins='true' ;;
    --verify) run_verification='true' ;;
    --only)
      shift
      case "${1:-}" in
        nuget | tools | sdk | actions | images) selected_family="$1" ;;
        *)
          printf 'Unknown family %s.\n%s\n' "${1:-<missing>}" "$usage" >&2
          exit 1
          ;;
      esac
      ;;
    -h | --help)
      printf '%s\n' "$usage"
      exit 0
      ;;
    *)
      printf 'Unknown argument %s.\n%s\n' "$1" "$usage" >&2
      exit 1
      ;;
  esac
  shift
done

if [[ "$run_verification" == 'true' && "$apply_pins" != 'true' ]]; then
  printf '%s\n' '--verify proves a tree this run changed, so it needs --apply beside it.' >&2
  exit 1
fi

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'update-dependencies.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

for required_command in curl jq gh; do
  if ! command -v "$required_command" > /dev/null 2>&1; then
    printf 'update-dependencies.sh needs %s and it is not on the path.\n' "$required_command" >&2
    exit 1
  fi
done

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

readonly records="$work_directory/records"
readonly moved="$work_directory/moved"
: > "$records"
: > "$moved"

# One record per pin, in the order the survey met it. `state` is derived rather than recorded, so a family only has to
# say what it found and what the upstream now publishes.
record() {
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$1" "$2" "$3" "$4" "$5" "$6" "$7" "$8" >> "$records"
}

selected() {
  [[ "$selected_family" == 'all' || "$selected_family" == "$1" ]]
}

# `sort -V` compares the way versions compare rather than the way text does, so 10.0.11 lands above 10.0.9. It is asked
# only whether the two differ and which is higher; equality is settled by string comparison first, because a version
# and itself never need sorting.
version_is_newer() {
  [[ "$1" != "$2" ]] && [[ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -1)" == "$1" ]]
}

# A survey of a hundred pins makes several hundred requests in a minute, and one of them coming back throttled or reset
# is ordinary rather than exceptional. Without a retry that blip reads as `unresolved` beside a pin whose upstream is
# perfectly reachable, which is the one failure mode that would teach a reader to disbelieve the column. Two extra
# attempts cover it; `--retry` already treats 408, 429, and every 5xx as worth repeating, and every request here is a
# read.
fetch() {
  curl --fail --silent --show-error --max-time 30 --retry 2 --retry-delay 1 --retry-connrefused "$@" 2> /dev/null
}

### Upstream readers. Each returns a value on standard output and never fails the run: an unreachable host is reported
### beside the pin it belongs to, because a survey of nineteen actions is worth having when one of them times out.

nuget_latest_version() {
  local package_id="${1,,}" allow_prerelease="$2" versions

  versions="$(fetch "https://api.nuget.org/v3-flatcontainer/$package_id/index.json" | jq -r '.versions[]?')" || true
  [[ -n "$versions" ]] || { printf 'unresolved'; return; }

  if [[ "$allow_prerelease" != 'true' ]]; then
    versions="$(printf '%s\n' "$versions" | grep -v -- '-' || true)"
  fi

  [[ -n "$versions" ]] || { printf 'unresolved'; return; }

  # The flat container publishes the list ascending, and sorting it again would reorder prerelease identifiers by rules
  # NuGet does not use. Take what the service already ordered.
  printf '%s' "$(printf '%s\n' "$versions" | tail -1)"
}

nuget_latest_stable_version() {
  nuget_latest_version "$1" 'false'
}

# The licence is read for the version that is current now rather than for the pinned one, because the question is
# whether the terms moved since the register recorded them.
nuget_licence() {
  local package_id="${1,,}" version="${2,,}" nuspec expression licence_url

  [[ "$version" != 'unresolved' ]] || { printf 'unresolved'; return; }

  nuspec="$(fetch "https://api.nuget.org/v3-flatcontainer/$package_id/$version/$package_id.nuspec")" || true
  [[ -n "$nuspec" ]] || { printf 'unresolved'; return; }

  expression="$(grep -oE '<license type="expression">[^<]+' <<< "$nuspec" | head -1 | sed 's|.*>||')" || true
  [[ -z "$expression" ]] || { printf '%s' "$expression"; return; }

  # A package may carry the text instead of an expression — `dotnet-stryker` is the worked example, and its register row
  # says so. There is nothing to compare in that case, and saying which shape it is beats printing nothing.
  if grep -q '<license type="file">' <<< "$nuspec"; then
    printf 'licence-file-in-package'
    return
  fi

  licence_url="$(grep -oE '<licenseUrl>[^<]+' <<< "$nuspec" | head -1 | sed 's|.*>||')" || true
  [[ -z "$licence_url" ]] && printf 'undeclared' || printf 'deprecated-url:%s' "$licence_url"
}

github_licence() {
  local spdx
  spdx="$(gh api "repos/$1/license" --jq '.license.spdx_id' 2> /dev/null)" || true
  [[ -z "$spdx" ]] && printf 'unresolved' || printf '%s' "$spdx"
}

github_version_tags() {
  gh api "repos/$1/git/matching-refs/tags/v" --jq '.[].ref' 2> /dev/null | sed 's|^refs/tags/||' || true
}

### The register side. Both readers are pattern reads over prose and are reported as such.

# Escapes a component name for use inside an extended regular expression, so a dot in a package identifier matches a dot
# rather than any character.
as_pattern() {
  sed -E 's#[][\\.*^$/+?(){}|]#\\&#g' <<< "$1"
}

# The register names a component inside prose, so a mention is matched as a whole identifier rather than as a substring:
# every character an identifier can carry is refused on both sides of it. Without that boundary,
# `Npgsql.EntityFrameworkCore.PostgreSQL` reads the row for `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` as its own and
# comes back recording that row's licence beside its own — a widened set that could absorb the very change the licence
# column exists to show. `Microsoft.Extensions.AI` against `Microsoft.Extensions.AI.Abstractions` is the same collision
# one level up.
#
# What the boundary does not fix is prose naming a component without a version, which is why this stays a pattern read
# reported as one rather than a lookup: the row is where a disagreement is confirmed.
register_mentions() {
  grep -nE "(^|[^A-Za-z0-9._/-])$(as_pattern "$1")([^A-Za-z0-9._-]|$)" "$register_file" 2> /dev/null || true
}

register_recorded_licences() {
  local recorded
  recorded="$(register_mentions "$1" | grep -oE "$licence_pattern" | sort -u | paste -sd',' -)" || true
  [[ -z "$recorded" ]] && printf 'not-named' || printf '%s' "$recorded"
}

# A register row names a package as `MailKit 4.17.0` and an image as `pgvector/pgvector:0.8.6-pg18`, so the separator is
# left out of it: a mention of the component, on a line that also carries the version it moved from. What comes back is a
# line number to open rather than an assertion about the row.
register_lines_naming() {
  local lines
  lines="$(register_mentions "$1" | grep -F -- "$2" | cut -d: -f1 | sort -un | paste -sd',' -)" || true
  [[ -z "$lines" ]] && printf 'none' || printf '%s' "$lines"
}

### Families.

survey_nuget_pins() {
  local pin_file package_id pinned latest allow_prerelease licence recorded note stable

  for pin_file in "$backend_pins" "$frontend_pins"; do
    [[ -f "$pin_file" ]] || continue

    while IFS=$'\t' read -r package_id pinned; do
      [[ -n "$package_id" ]] || continue

      allow_prerelease='false'
      [[ "$pinned" == *-* ]] && allow_prerelease='true'

      latest="$(nuget_latest_version "$package_id" "$allow_prerelease")"
      licence="$(nuget_licence "$package_id" "$latest")"
      recorded="$(register_recorded_licences "$package_id")"
      note="$pin_file"

      # A prerelease pin is the one case where the interesting answer is not the newest build of its own line. The
      # register carries a live obligation to leave one as soon as a stable release exists, so say whether one does.
      if [[ "$allow_prerelease" == 'true' ]]; then
        stable="$(nuget_latest_stable_version "$package_id")"
        if [[ "$stable" != 'unresolved' ]]; then
          note="$note; stable release available: $stable"
        else
          note="$note; still no stable release"
        fi
      fi

      record 'nuget' "$package_id" "$pinned" "$latest" "$licence" "$recorded" "$note" "$pin_file"
    done < <(grep -oE '<PackageVersion Include="[^"]+" Version="[^"]+"' "$pin_file" \
      | sed -E 's|<PackageVersion Include="([^"]+)" Version="([^"]+)"|\1\t\2|')
  done
}

survey_tool_manifest() {
  local tool_id pinned latest licence recorded

  [[ -f "$tool_manifest" ]] || return 0

  while IFS=$'\t' read -r tool_id pinned; do
    [[ -n "$tool_id" ]] || continue

    latest="$(nuget_latest_version "$tool_id" 'false')"
    licence="$(nuget_licence "$tool_id" "$latest")"
    recorded="$(register_recorded_licences "$tool_id")"

    record 'tools' "$tool_id" "$pinned" "$latest" "$licence" "$recorded" "$tool_manifest" "$tool_manifest"
  done < <(jq -r '.tools | to_entries[] | "\(.key)\t\(.value.version)"' "$tool_manifest")
}

survey_sdk_pins() {
  local sdk_pin channel latest_sdk sdk_id pinned latest licence recorded

  [[ -f "$global_json" ]] || return 0

  # The MSBuild SDKs are ordinary NuGet packages and are rewritten like any other pin. Uno.Sdk is the one that matters:
  # it decides the version of every Uno package the client restores, so the lock diff is where a move of it shows.
  while IFS=$'\t' read -r sdk_id pinned; do
    [[ -n "$sdk_id" ]] || continue

    latest="$(nuget_latest_version "$sdk_id" 'false')"
    licence="$(nuget_licence "$sdk_id" "$latest")"
    recorded="$(register_recorded_licences "$sdk_id")"

    record 'sdk' "$sdk_id" "$pinned" "$latest" "$licence" "$recorded" "$global_json (msbuild-sdks)" "$global_json"
  done < <(jq -r '(."msbuild-sdks" // {}) | to_entries[] | "\(.key)\t\(.value)"' "$global_json")

  sdk_pin="$(jq -r '.sdk.version // empty' "$global_json")"
  [[ -n "$sdk_pin" ]] || return 0

  channel="$(printf '%s' "$sdk_pin" | cut -d. -f1-2)"
  latest_sdk="$(fetch 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json' \
    | jq -r --arg channel "$channel" '."releases-index"[]? | select(."channel-version" == $channel) | ."latest-sdk"')" || true
  [[ -n "$latest_sdk" ]] || latest_sdk='unresolved'

  record 'sdk-floor' '.NET SDK' "$sdk_pin" "$latest_sdk" 'MIT' "$(register_recorded_licences '.NET 10 runtime')" \
    "$global_json (sdk.version) — a floor under rollForward, reported and never rewritten" "$global_json"
}

survey_action_references() {
  local reference action_repository pinned latest licence recorded note tags exact_tags

  [[ -d "$workflow_directory" ]] || return 0

  while read -r reference; do
    [[ -n "$reference" ]] || continue

    action_repository="$(printf '%s' "${reference%@*}" | cut -d/ -f1-2)"
    pinned="${reference#*@}"

    tags="$(github_version_tags "$action_repository")"
    licence="$(github_licence "$action_repository")"
    recorded="$(register_recorded_licences "$action_repository")"

    if [[ -z "$tags" ]]; then
      record 'actions' "$reference" "$pinned" 'unresolved' "$licence" "$recorded" 'no tag list came back' "$workflow_directory"
      continue
    fi

    if [[ "$pinned" =~ ^v[0-9]+$ ]]; then
      # "The line" means the major that is pinned, so the exact tag quoted beside it comes from that major rather than
      # from the newest one the repository has — otherwise a pin one major behind would be described by a line it is
      # not on.
      exact_tags="$(printf '%s\n' "$tags" | grep -E "^${pinned}\.[0-9]+\.[0-9]+$" | sort -V | tail -1)" || true
    else
      exact_tags="$(printf '%s\n' "$tags" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -1)" || true
    fi

    if [[ "$pinned" =~ ^v[0-9]+$ ]]; then
      # A moving major is its own update mechanism: a patch and a minor arrive without a commit here, and only a major
      # needs one. So the comparison is major against major, and the exact tag is carried as context rather than as a
      # proposal — an exact reference written over a moving one can pin *lower* than what a run executes today, which
      # is what #290 did.
      latest="$(printf '%s\n' "$tags" | grep -E '^v[0-9]+$' | sort -V | tail -1)" || true
      [[ -n "$latest" ]] || latest="$pinned"
      note="moving major; the line currently releases ${exact_tags:-unknown}"
    else
      latest="${exact_tags:-unresolved}"
      note='exact reference; this one is pinned rather than moving on purpose — read its register row before moving it'
    fi

    record 'actions' "$reference" "$pinned" "$latest" "$licence" "$recorded" "$note" "$workflow_directory"
  done < <(grep -rhoE 'uses: [A-Za-z0-9][^ ]*@[^ ]+' "$workflow_directory" | sed 's|^uses: ||' | sort -u)
}

# Every image reference this repository pins, read from the places that write one whole: the Dockerfile's `FROM` lines,
# the Compose defaults, and the AppHost's container calls. The Helm values and the Quadlet unit sources carry the same
# references split across keys; the count of files naming each one is what says how far a move reaches.
collect_image_references() {
  # The Dockerfile names its two bases in build arguments and its `FROM` lines then expand them, which is what lets a
  # build override a base without editing the file. The argument default is the pin; the `FROM` line is not.
  if [[ -f 'deploy/docker/Dockerfile' ]]; then
    grep -oE '^ARG +[A-Z_]+_IMAGE=[^ ]+' 'deploy/docker/Dockerfile' | sed -E 's|^ARG +[A-Z_]+_IMAGE=||'
  fi

  # Compose writes the same shape: an environment variable with the pin as its default.
  if [[ -f 'deploy/compose/compose.yaml' ]]; then
    grep -oE 'image: \$\{[A-Z_]+:-[^}]+\}' 'deploy/compose/compose.yaml' | sed -E 's|^.*:-([^}]+)\}$|\1|'
  fi

  # The AppHost states an image and its tag as consecutive quoted strings, whether that is `AddContainer(name, image,
  # tag)` on one line, the same call wrapped over three, or `WithImage(...)` followed by `WithImageTag(...)`. Keeping
  # the last image-shaped string seen and attaching the next tag-shaped or digest string to it reads all three.
  if [[ -f 'backend/src/AppHost/Program.cs' ]]; then
    grep -oE '"[A-Za-z0-9][A-Za-z0-9./_-]*"' 'backend/src/AppHost/Program.cs' \
      | tr -d '"' \
      | awk '
          /^([a-z0-9-]+(\.[a-z0-9-]+)+\/)?[a-z0-9._-]+\/[a-z0-9._-]+$/ { image = $0; next }
          image != "" && length($0) == 64 && /^[a-f0-9]+$/ { print image "@sha256:" $0; image = ""; next }
          image != "" && /^[0-9][A-Za-z0-9._-]*$/ { print image ":" $0; image = ""; next }
        '
  fi
}

# Which registry a reference lives in is decided once, because a tag list and a digest are read from the same host and
# routing them separately is how one of them ends up asking Docker Hub for an image it does not serve. Anything without a
# recognised host is Docker Hub, whether it was written with `docker.io/` in front of it or without.
registry_of() {
  case "$1" in
    mcr.microsoft.com/*) printf 'mcr' ;;
    ghcr.io/*) printf 'ghcr' ;;
    *) printf 'dockerhub' ;;
  esac
}

ghcr_pull_token() {
  fetch "https://ghcr.io/token?scope=repository:${1#ghcr.io/}:pull&service=ghcr.io" | jq -r '.token // empty'
}

registry_tags() {
  local repository="$1" token

  case "$(registry_of "$repository")" in
    mcr)
      fetch "https://mcr.microsoft.com/v2/${repository#mcr.microsoft.com/}/tags/list" | jq -r '.tags[]?'
      ;;
    ghcr)
      token="$(ghcr_pull_token "$repository")" || true
      [[ -n "${token:-}" ]] || return 0
      fetch -H "Authorization: Bearer $token" "https://ghcr.io/v2/${repository#ghcr.io/}/tags/list" | jq -r '.tags[]?'
      ;;
    *)
      fetch "https://hub.docker.com/v2/repositories/${repository#docker.io/}/tags/?page_size=100" | jq -r '.results[]?.name'
      ;;
  esac
}

# What the repository's `latest` tag resolves to now, which is the only comparison a digest pin admits. The two registry
# APIs answer it in a `Docker-Content-Digest` response header; Docker Hub's own API carries it as a field, and taking it
# there avoids a second token exchange.
registry_latest_digest() {
  local repository="$1" token
  local accept='application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json'

  case "$(registry_of "$repository")" in
    mcr)
      fetch -I -H "Accept: $accept" "https://mcr.microsoft.com/v2/${repository#mcr.microsoft.com/}/manifests/latest" \
        | tr -d '\r' | sed -n 's/^docker-content-digest: //Ip'
      ;;
    ghcr)
      token="$(ghcr_pull_token "$repository")" || true
      [[ -n "${token:-}" ]] || return 0
      fetch -I -H "Authorization: Bearer $token" -H "Accept: $accept" \
        "https://ghcr.io/v2/${repository#ghcr.io/}/manifests/latest" \
        | tr -d '\r' | sed -n 's/^docker-content-digest: //Ip'
      ;;
    *)
      fetch "https://hub.docker.com/v2/repositories/${repository#docker.io/}/tags/latest" | jq -r '.digest // empty'
      ;;
  esac
}

resolve_image_latest() {
  local repository="$1" tag="$2" tag_pattern tags

  tags="$(registry_tags "$repository")" || true

  [[ -n "${tags:-}" ]] || { printf 'unresolved'; return; }

  # The comparable tags are the ones shaped like the pinned one: its leading version becomes a wildcard and everything
  # after the first dash stays literal, so `0.8.6-pg18` is compared against other `pg18` builds rather than against a
  # newer release for a different PostgreSQL major.
  tag_pattern="^[0-9][0-9.]*$(printf '%s' "$tag" | sed -E 's#^[0-9][0-9.]*##' | sed -E 's#[][\\.*^$/+?(){}|]#\\&#g')\$"

  printf '%s' "$(printf '%s\n' "$tags" \
    | grep -E "$tag_pattern" \
    | sed -E 's|^([0-9][0-9.]*)|\1 &|' \
    | sort -V -k1,1 \
    | tail -1 \
    | cut -d' ' -f2-)"
}

resolve_image_digest() {
  local digest

  digest="$(registry_latest_digest "$1")" || true
  [[ -z "$digest" ]] && printf 'unresolved' || printf '%s' "$digest"
}

survey_image_pins() {
  local reference repository tag latest recorded carriers note

  while read -r reference; do
    [[ -n "$reference" ]] || continue

    # MailFathom's own image is built here rather than pinned from anywhere.
    case "$reference" in
      *krzysztof318* | mailfathom:*) continue ;;
    esac

    if [[ "$reference" == *@sha256:* ]]; then
      repository="${reference%@*}"
      tag="${reference#*@}"
      latest="$(resolve_image_digest "$repository")"
      note='digest pin; the upstream publishes no version tag, so this compares against what its latest tag resolves to now'
    else
      repository="${reference%:*}"
      tag="${reference##*:}"
      latest="$(resolve_image_latest "$repository" "$tag")"
      note='reported and never rewritten; the same reference is written in several assets in several syntaxes'
    fi

    [[ -n "$latest" ]] || latest='unresolved'

    recorded="$(register_recorded_licences "$repository")"
    carriers="$(git grep -l -F -- "$repository" -- deploy backend/src/AppHost 2> /dev/null | paste -sd',' -)" || true

    record 'images' "$repository" "$tag" "$latest" 'see register' "$recorded" "$note; carried by ${carriers:-none}" 'deploy'
  done < <(collect_image_references | sort -u)
}

### Reporting.

# An upstream may declare a compound expression — `MIT OR Apache-2.0` is the shape `crate-ci/typos` publishes — so the
# comparison is per identifier rather than over the whole string, and the question asked is whether the register
# recorded *any* of what the upstream now declares. Flagging unless it recorded all of them would report a dual licence
# as a change every time, which is the failure mode that trains a reader to stop looking.
register_records_any_of() {
  local declared="$1" recorded="$2" identifier found='false'

  # A here-string rather than a pipe into `grep -q`: `-q` exits on the first match, and under `pipefail` the SIGPIPE it
  # sends back reaches the pipeline's status as 141, which reads exactly like "no match".
  while read -r identifier; do
    [[ -n "$identifier" ]] || continue
    found='true'

    if grep -qF -- "$identifier" <<< "$recorded"; then
      return 0
    fi
  done < <(grep -oE "$licence_pattern" <<< "$declared" || true)

  # An identifier the list above does not know is a licence nobody here has met, not a licence that changed. Falling back
  # to comparing the declared expression whole keeps the weaker answer instead of reporting a difference the reader would
  # find is none — an unrecognised identifier is worth widening the list for, and worth reading the row for, but it is
  # not evidence.
  [[ "$found" == 'true' ]] && return 1

  grep -qF -- "$declared" <<< "$recorded"
}

state_of() {
  local pinned="$1" latest="$2"

  if [[ "$latest" == 'unresolved' ]]; then
    printf 'unknown'
  elif [[ "$pinned" == "$latest" ]]; then
    printf 'current'
  elif [[ "$pinned" == sha256:* ]]; then
    # Two digests are equal or they are not. Which of them is newer is not a question a digest can answer, and version
    # ordering over hexadecimal would invent an answer.
    printf 'moved'
  elif version_is_newer "$latest" "$pinned"; then
    printf 'behind'
  else
    # The pin is ahead of what the upstream's newest comparable reference resolves to. That is not an error and it is
    # not nothing: it is how a reference pinned lower than a moving tag looks, and how a prerelease pin looks beside its
    # own stable line.
    printf 'ahead'
  fi
}

report() {
  local family component pinned latest licence recorded note source state
  local heading_printed_for='' heading

  while IFS=$'\t' read -r family component pinned latest licence recorded note source; do
    [[ -n "$family" ]] || continue

    case "$family" in
      nuget) heading='Package pins' ;;
      tools) heading='Tool manifest' ;;
      sdk) heading='MSBuild SDK pins' ;;
      sdk-floor) heading='.NET SDK floor — surveyed, never rewritten' ;;
      actions) heading='GitHub Action references' ;;
      images) heading='Container images — surveyed, never rewritten' ;;
      *) heading="$family" ;;
    esac

    if [[ "$heading" != "$heading_printed_for" ]]; then
      printf '\n== %s ==\n' "$heading"
      heading_printed_for="$heading"
    fi

    state="$(state_of "$pinned" "$latest")"

    printf '  %-8s %-52s %s\n' "$state" "$component" \
      "$(if [[ "$state" == 'current' ]]; then printf '%s' "$pinned"; else printf '%s -> %s' "$pinned" "$latest"; fi)"
    printf '           licence now: %-24s register records: %s\n' "$licence" "$recorded"
    printf '           %s\n' "$note"

    # Only an SPDX expression is comparable. A package declaring its terms as a bundled file, a deprecated URL, or
    # nothing at all has no identifier to hold against the register, and saying so is the answer rather than a
    # disagreement — `dotnet-stryker` is the worked example, and its register row already records why.
    case "$licence" in
      unresolved | 'see register')
        ;;
      licence-file-in-package | undeclared | deprecated-url:*)
        printf '           The package declares no licence expression, so nothing here is comparable. Read the row and the package.\n'
        ;;
      *)
        if [[ "$recorded" != 'not-named' ]] && ! register_records_any_of "$licence" "$recorded"; then
          printf '           LICENCE DIFFERS from every identifier the register records for this component. Read the row.\n'
        fi
        ;;
    esac

    if [[ "$recorded" == 'not-named' ]]; then
      printf '           NOT IN THE REGISTER under this name. Either the row names it differently or the review is missing.\n'
    fi

    if [[ "$state" == 'behind' || "$state" == 'moved' ]]; then
      printf '%s\t%s\t%s\t%s\t%s\n' "$family" "$component" "$pinned" "$latest" "$source" >> "$moved"
    fi
  done < "$records"
}

### Applying.

# Writes one pin and answers whether it moved, which is not the same question as whether the write succeeded. `sed -i`
# exits 0 whether or not it substituted anything, so its status says the file was opened rather than that the version
# changed — and a `uses:` line carrying an inline comment after the reference is exactly the case where the two answers
# differ, because the survey's extraction accepts trailing content and an end-anchored pattern does not. Comparing the
# touched files before and after settles it for every family at once, so `--apply`'s report is what happened rather than
# what was attempted.
rewrite_pin() {
  local family="$1" component="$2" pinned="$3" latest="$4" source="$5"
  local before after temporary reference="${component%@*}"
  local -a touched=()

  case "$family" in
    nuget | tools | sdk) touched=("$source") ;;
    actions) mapfile -t touched < <(grep -rlF "uses: $component" "$workflow_directory" 2> /dev/null || true) ;;
    *) return 1 ;;
  esac

  ((${#touched[@]} > 0)) || return 1

  before="$(cat "${touched[@]}" | cksum)"

  case "$family" in
    nuget)
      sed -i -E "s#(<PackageVersion Include=\"$(as_pattern "$component")\" Version=\")$(as_pattern "$pinned")(\")#\1$latest\2#" "$source"
      ;;
    tools)
      temporary="$work_directory/tools.json"
      jq --indent 2 --arg tool "$component" --arg version "$latest" '.tools[$tool].version = $version' "$source" > "$temporary"
      mv "$temporary" "$source"
      ;;
    sdk)
      temporary="$work_directory/global.json"
      jq --indent 2 --arg sdk "$component" --arg version "$latest" '."msbuild-sdks"[$sdk] = $version' "$source" > "$temporary"
      mv "$temporary" "$source"
      ;;
    actions)
      # Two expressions rather than one anchored pattern: a reference at the end of its line, and a reference followed by
      # anything an action reference cannot itself contain, which is where an inline comment sits.
      sed -i -E \
        -e "s#uses: $(as_pattern "$component")\$#uses: $reference@$latest#" \
        -e "s#uses: $(as_pattern "$component")([^A-Za-z0-9._-])#uses: $reference@$latest\1#" \
        "${touched[@]}"
      ;;
  esac

  after="$(cat "${touched[@]}" | cksum)"

  [[ "$before" != "$after" ]]
}

apply_moved_pins() {
  local family component pinned latest source applied=0 skipped=0 packages_moved='false'

  if [[ ! -s "$moved" ]]; then
    printf '\nNothing is behind. No pin was rewritten.\n'
    return 0
  fi

  printf '\n== Rewriting the pins that can be written mechanically ==\n'

  while IFS=$'\t' read -r family component pinned latest source; do
    case "$family" in
      sdk-floor | images)
        printf '  left alone  %-48s %s -> %s\n' "$component" "$pinned" "$latest"
        skipped=$((skipped + 1))
        continue
        ;;
    esac

    if rewrite_pin "$family" "$component" "$pinned" "$latest" "$source"; then
      printf '  rewritten   %-48s %s -> %s\n' "$component" "$pinned" "$latest"
      applied=$((applied + 1))
      # Only a package version changes what a restore resolves. A tool manifest is restored on its own and an action
      # reference reaches no project, so neither obliges a lock file and neither is worth a four-minute restore.
      case "$family" in
        nuget | sdk) packages_moved='true' ;;
      esac
    else
      printf '  failed      %-48s %s -> %s\n' "$component" "$pinned" "$latest"
      skipped=$((skipped + 1))
    fi
  done < "$moved"

  printf '\n%d rewritten, %d left for a person.\n' "$applied" "$skipped"

  if [[ "$packages_moved" == 'true' ]]; then
    regenerate_lock_files
  fi
}

# Central pinning fixes the direct versions and the committed lock files fix the transitive closure those versions
# resolve to, so the two are one decision recorded in two places and both move in the same change. Restore runs in
# locked mode everywhere it is gated, which is what makes a stale lock file fail with NU1004 rather than resolve
# something nobody reviewed.
regenerate_lock_files() {
  local solution

  printf '\n== Regenerating the lock files ==\n'

  # Only this path needs the SDK, which is why it is not among the commands the run refuses to start without: a survey
  # answers on a machine that has never restored this repository.
  if ! command -v dotnet > /dev/null 2>&1; then
    printf '\nThe pins are written and the lock files are not: dotnet is not on the path.\n' >&2
    exit 1
  fi

  for solution in "$backend_solution" "$frontend_solution"; do
    [[ -f "$solution" ]] || continue

    printf '  %s\n' "$solution"

    if ! dotnet restore "$solution" --force-evaluate; then
      printf '\nThe restore of %s failed. The pins are written; the lock files are not.\n' "$solution" >&2
      exit 1
    fi
  done
}

report_register_obligations() {
  local family component pinned latest source lines

  [[ -s "$moved" ]] || return 0

  printf '\n== What THIRD_PARTY_LICENSES.md now says about a version that moved ==\n'
  printf '%s\n' 'Each line below names a register line that still carries the version it moved from. A row is a completed'
  printf '%s\n' 'review written as prose, so it is rewritten by hand rather than by this script.'
  printf '\n'

  while IFS=$'\t' read -r family component pinned latest source; do
    # An action is recorded above as the whole `owner/repo@ref` a workflow writes, and the register names the action
    # without its reference. Nothing else in a component name carries an `@`.
    lines="$(register_lines_naming "${component%@*}" "$pinned")"
    printf '  %-52s %s:%s\n' "${component%@*} $pinned" "$register_file" "$lines"
  done < "$moved"
}

### The run.

printf 'Reading every pin in %s against its upstream.\n' "$repository_root"

selected 'nuget' && survey_nuget_pins
selected 'tools' && survey_tool_manifest
selected 'sdk' && survey_sdk_pins
selected 'actions' && survey_action_references
selected 'images' && survey_image_pins

report

if [[ "$apply_pins" == 'true' ]]; then
  apply_moved_pins
fi

report_register_obligations

printf '\nThis reports and never gates. Confirm every line at the source it names before acting on it.\n'

if [[ "$run_verification" == 'true' ]]; then
  printf '\n== Proving the tree ==\n'
  "$repository_root/scripts/verify-full.sh"
fi
