#!/bin/bash
# usage: padpaste2.sh <filename-in-out-dir> [--keep]
# clears flow (select all + cut), pastes, reports actions + error count
OUTDIR='C:\Users\sakaj\projects\company-person\apps\fincalc\out'
PROBE=/c/Users/sakaj/projects/company-person/tools/padprobe/bin/Release/net8.0-windows/PadProbe.exe

# clear
"$PROBE" clickname designer "編集" >/dev/null
sleep 1.0
"$PROBE" clickname designer "すべて選択" >/dev/null
sleep 0.8
"$PROBE" clickname designer "編集" >/dev/null
sleep 1.0
"$PROBE" clickname designer "切り取り" >/dev/null
sleep 1.5
cur=$("$PROBE" dump designer 12 2>/dev/null | grep -c "ProgramItemTemplateSummaryTextBlock")
echo "after-clear-visible=$cur"

powershell -NoProfile -Command "Set-Clipboard -Value ([IO.File]::ReadAllText('$OUTDIR\\$1'))" || exit 1
sleep 0.5
count=0
for i in 1 2 3; do
  "$PROBE" clickname designer "編集" >/dev/null || break
  sleep 1.2
  "$PROBE" clickname designer "貼り付け" >/dev/null || break
  sleep 3
  count=$("$PROBE" dump designer 12 2>/dev/null | grep -c "ProgramItemTemplateSummaryTextBlock")
  [ "$count" -gt 0 ] && break
  sleep 0.7
done
errs=$("$PROBE" dump designer 12 2>/dev/null | grep "ErrorCountTextBlock" | grep -oE "[0-9]+")
echo "actions=$count errors=${errs:-0} (tries=$i)"
