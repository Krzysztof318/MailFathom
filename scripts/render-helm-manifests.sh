#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Lints the Helm chart, renders it against every values document the repository verifies it with, and holds what it
### rendered against the manifests committed beside them.
#
# Usage:
#   scripts/render-helm-manifests.sh             lints, renders, and reports what moved against the committed manifests
#   scripts/render-helm-manifests.sh --update    writes the rendering over them, which is how an intended change lands
#
# The deployment contract is one of the four public surfaces ADR 0004 names, and the live deployment installs it with
# Helm. Until this existed the chart was linted and rendered for the first time during a release — after the image was
# already published — so a chart that stopped rendering was found at the worst moment there is. `CI` runs this on every
# pull request that touches the chart; `Publish Helm chart` still runs its own lint and render at release time, because
# a release gate that trusts an earlier run is not a gate.
#
# Rendering is only half of it. A template edit that keeps rendering but produces a different object is invisible in a
# diff of the templates alone, so what each values document renders is committed under `ci/golden/` and this compares
# against it. What appears in the pull request is then the manifests themselves — a changed image reference, a dropped
# volume, a security context that stopped being applied — rather than the absence of a failure.
#
# Nothing here reaches a cluster or a network. `helm template` renders; it does not admit, and the values documents name
# no real image, database, or host.

readonly chart_directory='deploy/helm/mailfathom'
readonly golden_directory="$chart_directory/ci/golden"
readonly release_name='mailfathom'

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'render-helm-manifests.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

update_golden_files='false'

case "${1:-}" in
  '')
    ;;
  --update)
    update_golden_files='true'
    ;;
  *)
    printf 'Usage: scripts/render-helm-manifests.sh [--update]\n' >&2
    exit 1
    ;;
esac

# Helm is a developer-machine install rather than something this repository vendors, and the GitHub-hosted runner
# carries it already — see THIRD_PARTY_LICENSES.md. Its absence is reported rather than worked around, because a run
# that skipped the chart silently is what this script exists to replace.
if ! command -v helm > /dev/null 2>&1; then
  printf 'Helm is not on PATH. Install it from https://helm.sh/docs/intro/install/ to lint and render the chart.\n' >&2
  exit 1
fi

# What `helm template` prints between documents is Helm's own framing rather than the chart's: Helm 4 emits the blank
# line a template left at its end and Helm 3 does not, so two renderings of one chart differ by whitespace alone
# depending on which version ran. The record is about the objects the chart produces, so the framing is settled here and
# a golden file rendered on either version compares equal — Helm 3.19.0 and Helm 4.2.3 produce identical normalized
# renderings of this chart, which is what makes pinning a Helm version for the comparison unnecessary and keeps the
# runner's preinstalled copy the one this repository uses everywhere. Nothing that carries meaning is touched: a
# document's own lines survive, in order, and only whitespace at the end of a line and blank lines at the end of a
# document go.
normalize_rendering() {
  awk '
    { sub(/[[:space:]]+$/, "") }

    # Blank lines are held rather than printed, so the ones that turn out to be at the end of a document — which is any
    # run of them followed by the next document separator or by the end of the rendering — are dropped instead.
    /^$/ { pending_blank_lines++; next }

    {
      if ($0 != "---") {
        for (; pending_blank_lines > 0; pending_blank_lines--) {
          print ""
        }
      }

      pending_blank_lines = 0
      print
    }
  '
}

# The same three lines every file in this repository that the C# analyzer cannot reach carries by hand, plus what a
# reader of a generated file needs: what produced it, and the one command that takes a change into it.
write_golden_header() {
  local values_document="$1"

  sed -n 's/^file_header_template = //p' .editorconfig |
    sed 's/\\n/\n/g' |
    sed 's/^/# /'

  printf '#\n'
  printf '# Rendered from %s with %s by scripts/render-helm-manifests.sh.\n' "$chart_directory" "$values_document"
  printf '# Committed so a change in what the chart produces is visible in the diff rather than only a failure to\n'
  printf '# produce anything. Regenerate with: scripts/render-helm-manifests.sh --update\n'
}

# `ci/*-values.yaml` is the set the release run renders too, and it is deliberately read as a glob rather than listed:
# a values document added to the directory is verified by the run that adds it, without a second place to remember.
mapfile -t values_documents < <(find "$chart_directory/ci" -maxdepth 1 -name '*-values.yaml' -type f | sort)

if [[ "${#values_documents[@]}" -eq 0 ]]; then
  printf 'No values document under %s/ci matches *-values.yaml, so the chart is rendered against nothing.\n' \
    "$chart_directory" >&2
  exit 1
fi

mkdir -p "$golden_directory"

rendering_directory="$(mktemp --directory)"
trap 'rm --recursive --force "$rendering_directory"' EXIT

differing_charts=0

for values_document in "${values_documents[@]}"; do
  channel="$(basename "$values_document" '-values.yaml')"
  golden_file="$golden_directory/$channel.yaml"
  rendering="$rendering_directory/$channel.yaml"

  printf '\n--- %s ---\n' "$values_document"

  # `--strict`, so a lint warning fails as well: the chart is small enough that a warning here is a defect rather than
  # noise, and this is the same invocation the release runs. Schema validation happens inside both of the calls below,
  # over the values document coalesced with `values.yaml`, which is what validates the two files against the schema
  # together — including a `values.schema.json` that is not a JSON document at all, which Helm reports rather than
  # skipping. `values.yaml` alone is deliberately not valid against it: `image.repository` is empty there, and the
  # schema's `minLength` is what makes supplying an image reference an operator's obligation rather than a default.
  # `ci/defaults-values.yaml` is what keeps the defaults themselves inside the check, since the other two documents
  # override most of them.
  #
  # The render is not what the lint already did. Helm 4's lint reports a template that calls `fail` as an informational
  # line and still passes the chart, so a chart that refuses to render reaches a release green on the lint alone.
  helm lint "$chart_directory" --strict --values "$values_document"

  {
    write_golden_header "$values_document"
    helm template "$release_name" "$chart_directory" --values "$values_document" | normalize_rendering
  } > "$rendering"

  if [[ "$update_golden_files" == 'true' ]]; then
    cp "$rendering" "$golden_file"
    printf 'Wrote %s\n' "$golden_file"

    continue
  fi

  if [[ ! -f "$golden_file" ]]; then
    printf '::error::%s is missing. Regenerate it with: scripts/render-helm-manifests.sh --update\n' "$golden_file" >&2
    differing_charts=$(( differing_charts + 1 ))

    continue
  fi

  if diff --unified "$golden_file" "$rendering"; then
    printf 'The chart renders %s exactly as committed.\n' "$golden_file"

    continue
  fi

  printf '::error::The chart no longer renders %s as committed.\n' "$golden_file" >&2
  printf 'This is a change to the deployment contract. Name it in the pull request against that surface and with the\n' >&2
  printf 'operator action it implies, because the release pull request composes CHANGELOG.md from that reading alone.\n' >&2
  printf 'Take the change into the record with: scripts/render-helm-manifests.sh --update\n' >&2
  differing_charts=$(( differing_charts + 1 ))
done

# A values document that was renamed or removed leaves its rendering behind, and a committed manifest nothing renders
# is a record of a chart that no longer exists. It is reported rather than deleted here, because which of the two files
# is the mistake is the author's to say.
while IFS= read -r golden_file; do
  channel="$(basename "$golden_file" '.yaml')"

  if [[ -f "$chart_directory/ci/$channel-values.yaml" ]]; then
    continue
  fi

  printf '::error::%s renders from no values document under %s/ci. Remove it, or restore the values document.\n' \
    "$golden_file" "$chart_directory" >&2
  differing_charts=$(( differing_charts + 1 ))
done < <(find "$golden_directory" -maxdepth 1 -name '*.yaml' -type f | sort)

if [[ "$differing_charts" -ne 0 ]]; then
  exit 1
fi

if [[ "$update_golden_files" == 'true' ]]; then
  printf '\nThe chart lints and renders against every values document the repository verifies it with.\n'
  exit 0
fi

printf '\nThe chart lints, renders, and produces the manifests committed under %s.\n' "$golden_directory"
