#!/bin/bash
# usage: padpaste.sh <filename-in-out-dir> [--keep]
OUTDIR='C:\Users\sakaj\projects\company-person\apps\fincalc\out'
PROBE=/c/Users/sakaj/projects/company-person/tools/padprobe/bin/Release/net8.0-windows/PadProbe.exe
powershell -NoProfile -Command "Set-Clipboard -Value ([IO.File]::ReadAllText('$OUTDIR\\$1'))" || exit 1
sleep 0.3
count=0
for i in 1 2 3; do
  "$PROBE" clickname designer "編集" >/dev/null || break
  sleep 1.0
  "$PROBE" clickname designer "貼り付け" >/dev/null || break
  sleep 2.5
  count=$("$PROBE" dump designer 12 2>/dev/null | grep -c "ProgramItemTemplateSummaryTextBlock")
  [ "$count" -gt 0 ] && break
  sleep 0.5
done
echo "actions=$count (tries=$i)"
if [ "$2" != "--keep" ]; then
  "$PROBE" clickname designer "編集" >/dev/null
  sleep 0.7
  "$PROBE" clickname designer "元に戻す" >/dev/null
  sleep 1
fi
