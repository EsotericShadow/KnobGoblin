#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${1:-$(pwd)}"
OUT_DIR="${2:-$ROOT_DIR/artifacts/quality}"
mkdir -p "$OUT_DIR"

LOC_TSV="$OUT_DIR/csharp_loc.tsv"
SUMMARY_TXT="$OUT_DIR/csharp_loc_summary.txt"

cd "$ROOT_DIR"

rg --files \
  -g '*.cs' \
  -g '!**/bin/**' \
  -g '!**/obj/**' \
  -g '!**/artifacts/**' \
  -g '!**/.git/**' \
  -g '!**/.vs/**' \
  -g '!**/node_modules/**' \
  | while IFS= read -r file; do
      lines="$(wc -l < "$file" | tr -d ' ')"
      printf '%s\t%s\n' "$lines" "$file"
    done \
  | sort -nr > "$LOC_TSV"

total_files="$(wc -l < "$LOC_TSV" | tr -d ' ')"
gt1000="$(awk -F'\t' '$1>1000{n++} END{print n+0}' "$LOC_TSV")"
gt840="$(awk -F'\t' '$1>840{n++} END{print n+0}' "$LOC_TSV")"
gt400="$(awk -F'\t' '$1>400{n++} END{print n+0}' "$LOC_TSV")"

{
  echo "root=$ROOT_DIR"
  echo "total_files=$total_files"
  echo "gt1000=$gt1000"
  echo "gt840=$gt840"
  echo "gt400=$gt400"
  echo
  echo "Top 25:"
  head -n 25 "$LOC_TSV"
} > "$SUMMARY_TXT"

cat "$SUMMARY_TXT"
