#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
# for each icon: clear flow, paste dialog line, report actions+errors
OUTDIR="$ROOT/apps/fincalc/out"
PROBE=$ROOT/tools/padprobe/bin/Release/net8.0-windows/PadProbe.exe
for icon in "$@"; do
  f="$OUTDIR/icon_$icon.txt"
  printf "Display.ShowMessageDialog.ShowMessageWithTimeout Title: \$'''t''' Message: \$'''m''' Icon: Display.Icon.%s Buttons: Display.Buttons.OK DefaultButton: Display.DefaultButton.Button1 IsTopMost: False Timeout: 10\n" "$icon" > "$f"
  # clear
  "$PROBE" clickxy designer 700 400 >/dev/null 2>&1
  sleep 0.6
  "$PROBE" keys designer CONTROL,A >/dev/null 2>&1
  sleep 0.5
  "$PROBE" keys designer DELETE >/dev/null 2>&1
  sleep 1
  powershell -NoProfile -Command "Set-Clipboard -Value ([IO.File]::ReadAllText('${ROOTWIN}\\apps\\fincalc\\out\\icon_$icon.txt'))" >/dev/null
  sleep 0.4
  "$PROBE" clickname designer "編集" >/dev/null
  sleep 1.0
  "$PROBE" clickname designer "貼り付け" >/dev/null
  sleep 2.5
  acts=$("$PROBE" dump designer 12 2>/dev/null | grep -c "ProgramItemTemplateSummaryTextBlock")
  errs=$("$PROBE" dump designer 12 2>/dev/null | grep "ErrorCountTextBlock" | grep -oE "[0-9]+")
  echo "$icon: actions=$acts errors=${errs:-0}"
done
