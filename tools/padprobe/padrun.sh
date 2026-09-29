#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
# usage: padrun.sh <file> — clear+paste, if errors==0 save & run, wait until started & finished
PROBE=$ROOT/tools/padprobe/bin/Release/net8.0-windows/PadProbe.exe
res=$(bash padtest.sh "$1" 2>/dev/null | tail -1)
echo "$res"
echo "$res" | grep -q "errors=0" || { echo "SKIP-RUN (errors present)"; exit 1; }
"$PROBE" click designer SaveDraftFlowButton >/dev/null 2>&1
# 保存完了を待つ (ready に戻るまで)
for i in $(seq 1 30); do
  st=$("$PROBE" dump designer 6 2>/dev/null | grep -oE "Flow_status_[a-z]+" | head -1)
  [ "$st" = "Flow_status_ready" ] && break
  sleep 1
done
echo "post-save status: $st"
rect=$("$PROBE" dump designer 8 2>/dev/null | grep "StartFlowButton" | grep -oE "X=[0-9]+,Y=[0-9]+,Width=[0-9]+,Height=[0-9]+" | head -1)
x=$(echo "$rect" | grep -oE "X=[0-9]+" | cut -d= -f2); y=$(echo "$rect" | grep -oE "Y=[0-9]+" | cut -d= -f2)
w=$(echo "$rect" | grep -oE "Width=[0-9]+" | cut -d= -f2); h=$(echo "$rect" | grep -oE "Height=[0-9]+" | cut -d= -f2)
"$PROBE" clickxy designer $((x + w/2)) $((y + h/2)) >/dev/null 2>&1
started=0
for i in $(seq 1 90); do
  st=$("$PROBE" dump designer 6 2>/dev/null | grep -oE "Flow_status_[a-z]+" | head -1)
  case "$st" in Flow_status_running|Flow_status_parsing) started=1;; esac
  [ "$started" = "1" ] && [ "$st" = "Flow_status_ready" ] && { echo "STATUS=ran+ready after ~$((i*2))s"; exit 0; }
  sleep 2
done
echo "STATUS=timeout started=$started last=$st"; exit 2
