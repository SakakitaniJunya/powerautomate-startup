#!/bin/bash
PAD='C:\Users\sakaj\projects\company-person\apps\fincalc\pad'
for n in 02-excel-asset-batch 03-invoice-tax 04-withholding-batch 05-error-handling-retry 06-csv-folder-batch 07-excel-dep-batch; do
  bash /c/Users/sakaj/projects/company-person/tools/padprobe/padtest.sh "${PAD}\\${n}.txt"
done
