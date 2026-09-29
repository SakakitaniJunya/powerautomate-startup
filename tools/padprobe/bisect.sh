#!/bin/bash
# paste each test file, report action count, undo
cd /c/Users/sakaj/projects/company-person/apps/fincalc/out
for f in t_comment t_str t_dos t_if t_json t_foreach t_file t_msg; do
  printf "%-12s " "$f"
  bash /c/Users/sakaj/projects/company-person/tools/padprobe/padpaste.sh "C:\\Users\\sakaj\\projects\\company-person\\apps\\fincalc\\out\\${f}.txt"
done
