#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${1:-$(pwd)}"
RUN_XCTRACE="${RUN_XCTRACE:-0}"
OUT_DIR="$ROOT_DIR/artifacts/quality"
mkdir -p "$OUT_DIR"

echo "== Refactor Gate =="
echo "root=$ROOT_DIR"
echo "run_xctrace=$RUN_XCTRACE"

echo
echo "[1/3] LOC audit"
"$ROOT_DIR/scripts/quality/loc_audit.sh" "$ROOT_DIR" "$OUT_DIR" > "$OUT_DIR/loc_audit_last.txt"

echo
echo "[2/3] Build gate"
dotnet build "$ROOT_DIR/KnobForge.sln" --nologo 2>&1 | tee "$OUT_DIR/build_last.txt"

if [[ "$RUN_XCTRACE" != "1" ]]; then
  echo
  echo "[3/3] Memory/resource gate skipped (set RUN_XCTRACE=1 to enable)."
  exit 0
fi

if ! command -v xcrun >/dev/null 2>&1; then
  echo
  echo "[3/3] xcrun not found; skipping xctrace gates."
  exit 0
fi

echo
echo "[3/3] Memory/resource gate (xctrace)"
xcrun xctrace record \
  --template "Allocations" \
  --time-limit 8s \
  --output /tmp/knobforge_refactor_alloc.trace \
  --launch -- \
  /usr/local/share/dotnet/dotnet run --no-build --project "$ROOT_DIR/KnobForge.App/KnobForge.App.csproj" \
  >/dev/null 2>&1 || true

xcrun xctrace export \
  --input /tmp/knobforge_refactor_alloc.trace \
  --xpath "/trace-toc/run[@number=\"1\"]/tracks/track[@name=\"Allocations\"]/details/detail[@name=\"Statistics\"]" \
  > "$OUT_DIR/xctrace_alloc_stats_last.xml" || true

xcrun xctrace record \
  --template "Leaks" \
  --time-limit 8s \
  --output /tmp/knobforge_refactor_leaks.trace \
  --launch -- \
  /usr/local/share/dotnet/dotnet run --no-build --project "$ROOT_DIR/KnobForge.App/KnobForge.App.csproj" \
  >/dev/null 2>&1 || true

xcrun xctrace export \
  --input /tmp/knobforge_refactor_leaks.trace \
  --xpath "/trace-toc/run[@number=\"1\"]/tracks/track[@name=\"Leaks\"]/details/detail[@name=\"Leaks\"]" \
  > "$OUT_DIR/xctrace_leaks_last.xml" || true

echo "xctrace artifacts:"
echo "  $OUT_DIR/xctrace_alloc_stats_last.xml"
echo "  $OUT_DIR/xctrace_leaks_last.xml"
