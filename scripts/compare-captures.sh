#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# The third step of the design-parity loop, and the one that decides what a session reads. `scripts/capture-design.sh`
# and `scripts/capture-client.sh` write the two halves of each pair; this compares them mechanically and reports the
# result as text.
#
# Text first is the whole point. A screenshot is the most expensive thing a session loads and the cost follows the
# image's pixel dimensions rather than its bytes, so compressing a capture saves nothing and reading eight frames to
# find two differences spends a context window on the six that matched. What this prints instead is a count per pair
# and a bounding box per differing region, with the crop that opens each one — so the images that get read are the
# regions the numbers already flagged.
#
# Nothing is tolerated by default. `--fuzz` states a tolerance when one turns out to be needed, and it applies to the
# colour distance of a single pixel rather than to a count of them, so a region that moved is still reported at full
# weight.

usage() {
  cat <<'USAGE'
Usage: bash scripts/compare-captures.sh <directory> [option...]

  <directory>              Where `scripts/capture-design.sh` and `scripts/capture-client.sh` wrote their
                           captures. Pairs are matched by name: `<screen>-<composition>.design.png` against
                           `<screen>-<composition>.client.png`.
  --smallest <pixels>      Ignore a differing region smaller than this many pixels. Default 1, which reports
                           every region there is; raise it only with a reason, and say the reason.
  --fuzz <percent>         How far two pixels may differ in colour before they count as differing. Default 0.
                           A tolerance applies to a named class of difference and is stated with it, never
                           left on as a way of making a report quieter.
  --largest <count>        How many of a pair's regions are listed, biggest first. Default 12. The count of
                           regions is always reported in full; this bounds the listing, not the measurement.

It writes a report and no images. Open a region with the crop the report names for it; the whole frame is
almost never what has to be read.
USAGE
}

if ! command -v magick >/dev/null 2>&1; then
  printf 'ImageMagick is required to compare captures.\n' >&2
  exit 1
fi

directory=''
smallest=1
fuzz=0
largest=12

while (($# > 0)); do
  case "$1" in
    --smallest)
      smallest="${2:-}"
      shift 2
      ;;
    --largest)
      largest="${2:-}"
      shift 2
      ;;
    --fuzz)
      fuzz="${2:-}"
      shift 2
      ;;
    -h | --help | help)
      usage
      exit 0
      ;;
    *)
      if [[ -n "$directory" ]]; then
        printf 'Name one directory of captures, not several.\n\n' >&2
        usage >&2
        exit 1
      fi

      directory="$1"
      shift
      ;;
  esac
done

if [[ -z "$directory" || ! -d "$directory" ]]; then
  printf 'Name the directory the captures were written to.\n\n' >&2
  usage >&2
  exit 1
fi

shopt -s nullglob
design_captures=("$directory"/*.design.png)
shopt -u nullglob

if ((${#design_captures[@]} == 0)); then
  printf 'No captures in %s. Run scripts/capture-design.sh and scripts/capture-client.sh first.\n' "$directory" >&2
  exit 1
fi

# A pair that differs in size is not a pair, and comparing one would report the resize as a difference in the design.
# It is reported and skipped rather than scaled to fit.
mismatched=0
differing=0

printf 'Captures in %s\n\n' "$directory"
printf '%-24s %-10s %12s %12s %8s %8s\n' 'SCREEN' 'COMPOSITION' 'DIFFERING' 'OF' 'SHARE' 'REGIONS'

for design in "${design_captures[@]}"; do
  pair="$(basename "$design" .design.png)"
  client="$directory/$pair.client.png"
  screen="${pair%-*}"
  composition="${pair##*-}"

  if [[ ! -f "$client" ]]; then
    printf '%-24s %-10s %s\n' "$screen" "$composition" 'no client capture of this pair'
    mismatched=$((mismatched + 1))
    continue
  fi

  design_size="$(magick identify -format '%wx%h' "$design")"
  client_size="$(magick identify -format '%wx%h' "$client")"

  if [[ "$design_size" != "$client_size" ]]; then
    printf '%-24s %-10s design is %s and client is %s — not a pair\n' \
      "$screen" "$composition" "$design_size" "$client_size"
    mismatched=$((mismatched + 1))
    continue
  fi

  total=$((${design_size%x*} * ${design_size#*x}))

  # One difference image, read twice: once for how many pixels differ, and once for where they are. The threshold is
  # what turns a colour distance into a yes or a no, and `--fuzz` is the only thing that moves it — at zero, any
  # difference at all counts.
  mask="$directory/$pair.differs.png"
  magick "$design" "$client" -compose difference -composite \
    -colorspace Gray -threshold "$fuzz%" "$mask"

  count="$(magick "$mask" -format '%[fx:int(mean*w*h+0.5)]' info:)"

  if ((count == 0)); then
    printf '%-24s %-10s %12s %12s %7s%% %s\n' "$screen" "$composition" '0' "$total" '0.00' '—'
    rm -f "$mask"
    continue
  fi

  differing=$((differing + 1))

  # Where, as regions rather than as one number for the frame. Connected components is ImageMagick's own, so the
  # grouping is a measurement rather than a heuristic written here: each row is one blob of differing pixels with the
  # bounding box that holds it.
  #
  # The mask is binary, so a component is either the difference or the ground between differences, and the filter is
  # written as "not the ground" rather than as an equality against what white is called. What white is called is not
  # this script's to predict: ImageMagick 7.1.2 prints `gray(255)` for a grayscale mask whatever its quantum depth or
  # the file's own bit depth — measured at Q16 against masks written at 1, 8 and 16 bits — while a build or a version
  # that scaled it to the quantum range would print `gray(65535)` instead, and either would match here.
  regions="$(
    magick "$mask" \
      -define connected-components:verbose=true \
      -define connected-components:area-threshold="$smallest" \
      -define connected-components:mean-color=true \
      -connected-components 8 null: 2>/dev/null |
      awk -v smallest="$smallest" '
        NR > 1 && $NF != "gray(0)" && $4 + 0 >= smallest { print $2, $4 }
      ' | sort -k2 -nr
  )"

  found="$(printf '%s\n' "$regions" | grep -c . || true)"

  printf '%-24s %-10s %12s %12s %7.2f%% %8s\n' \
    "$screen" "$composition" "$count" "$total" \
    "$(awk -v c="$count" -v t="$total" 'BEGIN { printf "%.2f", c * 100 / t }')" \
    "$found"

  # The listing is bounded and the count above is not, which is the same reasoning as the bound on how many pairs one
  # capture produces: a frame whose text is drawn one pixel across differs in a region per glyph run, and printing
  # four hundred of them would spend on the report what the report exists to save.
  printf '%s\n' "$regions" | head -n "$largest" | while read -r geometry pixels; do
    [[ -z "$geometry" ]] && continue
    printf '    %-20s %10s px\n' "$geometry" "$pixels"
  done

  if ((found > largest)); then
    printf '    ... and %s more region(s), every one smaller than the last listed\n' "$((found - largest))"
  fi

  printf '    open one with: magick %s -crop <geometry> +repage <somewhere>.png\n' "$pair.design.png"
  printf '    the same crop on %s opens the client side; %s is every differing pixel as a mask\n' \
    "$pair.client.png" "$pair.differs.png"
done

printf '\n'

if ((mismatched > 0)); then
  printf '%s pair(s) could not be compared. A missing or differently sized capture is a capture run to repeat,\n' "$mismatched"
  printf 'never a difference to read.\n'
fi

if ((differing == 0 && mismatched == 0)); then
  printf 'Every pair matches pixel for pixel.\n'
  exit 0
fi

printf 'Read the regions above before any image, and open a region with the crop beside it rather than the frame.\n'
