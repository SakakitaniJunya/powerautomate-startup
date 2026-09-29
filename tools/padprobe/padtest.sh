#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
# usage: padtest.sh <file> — reliable clear (repeat until 0), paste, report actions+errors
OUTDIR="${ROOTW}\apps\fincalc\out"
PROBE=$ROOT/tools/padprobe/bin/Release/net8.0-windows/PadProbe.exe
for i in 1 2 3 4 5; do
  n=$("$PROBE" dump designer 12 2>/dev/null | grep -A9 "ProgramDetailsStatusBarItem" | grep -oE "'[0-9]+ " | head -2 | tail -1 | tr -dc 0-9)
  [ "$n" = "0" ] && break
  "$PROBE" clickxy designer 750 500 >/dev/null 2>&1
  sleep 0.6
  "$PROBE" keys designer CONTROL,A >/dev/null 2>&1
  sleep 0.5
  "$PROBE" keys designer DELETE >/dev/null 2>&1
  sleep 1.2
done
case "$1" in
  /*|?:*) F="$1" ;;
  *)    F="$OUTDIR\\$1" ;;
esac
powershell -NoProfile -Command "Set-Clipboard -Value ([IO.File]::ReadAllText('$F'))" || exit 1
sleep 0.5
"$PROBE" clickxy designer 750 500 >/dev/null 2>&1
sleep 0.9
"$PROBE" keys designer CONTROL,V >/dev/null 2>&1
sleep 3.5
d=$("$PROBE" dump designer 14 2>/dev/null)
n=$(echo "$d" | grep -A9 "ProgramDetailsStatusBarItem" | grep -oE "'[0-9]+ " | head -2 | tail -1 | tr -dc 0-9)
e=$(echo "$d" | grep "ErrorCountTextBlock" | grep -oE "[0-9]+" | head -1)
echo "$1: actions=$n errors=${e:-0}"
