#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
# paste each test file, report action count, undo
cd $ROOT/apps/fincalc/out
for f in t_comment t_str t_dos t_if t_json t_foreach t_file t_msg; do
  printf "%-12s " "$f"
  bash $ROOT/tools/padprobe/padpaste.sh "${f}.txt"
done
