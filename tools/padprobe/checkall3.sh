#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
for f in "$@"; do
  n=$(basename "$f")
  out=$(bash padtest.sh "$f" 2>/dev/null | tail -1)
  echo "$n: $out"
done
