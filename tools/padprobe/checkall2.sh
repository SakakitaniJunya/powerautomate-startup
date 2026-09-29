#!/bin/bash
# このリポジトリのルートを自動解決 (Git Bash 前提)
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
ROOTW=$(cygpath -w "$ROOT" 2>/dev/null)
ROOTWIN=${ROOTW//\\/\\\\}
PAD="${ROOTW}\apps\fincalc\pad"
for n in 02-excel-asset-batch 03-invoice-tax 04-withholding-batch 05-error-handling-retry 06-csv-folder-batch 07-excel-dep-batch; do
  bash $ROOT/tools/padprobe/padtest.sh "${PAD}\\${n}.txt"
done
