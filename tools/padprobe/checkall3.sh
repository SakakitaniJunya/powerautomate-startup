#!/bin/bash
for f in "$@"; do
  n=$(basename "$f")
  out=$(bash padtest.sh "$f" 2>/dev/null | tail -1)
  echo "$n: $out"
done
