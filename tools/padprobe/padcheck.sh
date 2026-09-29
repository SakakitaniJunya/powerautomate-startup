#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
# usage: padcheck.sh <pad-template-file>  — clears flow, pastes, reports actions+errors+first error text
PROBE=$ROOT/tools/padprobe/bin/Release/net8.0-windows/PadProbe.exe
F="$1"

# clear existing actions
"$PROBE" clickxy designer 794 672 >/dev/null 2>&1
sleep 0.7
"$PROBE" keys designer CONTROL,A >/dev/null 2>&1
sleep 0.5
"$PROBE" keys designer DELETE >/dev/null 2>&1
sleep 1.5

powershell -NoProfile -Command "Set-Clipboard -Value ([IO.File]::ReadAllText('$F'))" || exit 1
sleep 0.5
"$PROBE" clickxy designer 794 672 >/dev/null 2>&1
sleep 0.9
"$PROBE" keys designer CONTROL,V >/dev/null 2>&1
sleep 4

d=$("$PROBE" dump designer 16 2>/dev/null)
n=$(echo "$d" | grep -c "ProgramItemTemplateSummaryTextBlock")
e=$(echo "$d" | grep "ErrorCountTextBlock" | grep -oE "[0-9]+" | head -1)
echo "actions_visible=$n errors=${e:-0}"
# first few error texts + line numbers
"$PROBE" dump designer 18 2>/dev/null | grep -E "DataItem" -A6 | grep -E "'.*('|見つかりません|存在しません|不正|引数)" | grep -oE "'[^']{4,200}'" | head -12
