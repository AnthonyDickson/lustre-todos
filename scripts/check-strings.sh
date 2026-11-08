#!/usr/bin/env bash
# Reject a `\` line continuation inside a triple-quoted F# string, which keeps the
# backslash and the next line's indentation rather than joining the lines.
set -euo pipefail
cd "$(dirname "$0")/.."
find server/src server/tests server/scripts \( -name '*.fs' -o -name '*.fsx' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
	| xargs -0 awk '
		FNR == 1 { inside = 0 }
		{
			if (gsub(/"""/, "&") % 2 == 1) inside = !inside
			if (inside && $0 ~ /\\[[:space:]]*$/) {
				printf "%s:%d: \\ does not continue a line in a \"\"\" string\n", FILENAME, FNR
				bad = 1
			}
		}
		END { exit bad }
	'
