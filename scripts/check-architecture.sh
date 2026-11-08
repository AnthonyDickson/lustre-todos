#!/usr/bin/env bash
# Enforce the architecture rules that no compiler or formatter can: slice and
# shared module boundaries. See docs/architecture.md#dependency-rules.
set -euo pipefail
cd "$(dirname "$0")/.."
src=server/src/LustreTodos
fail=0

# Feature files may only open their own slice (or the shared modules)
while IFS= read -r file; do
	slice="$(basename "$(dirname "$file")")"
	opens="$(grep -oE 'open LustreTodos\.Feature\.[A-Za-z]+' "$file" | sed 's/open LustreTodos\.Feature\.//' | sort -u || true)"
	while read -r dep; do
		if [[ -n "$dep" && "$dep" != "$slice" ]]; then
			echo "Cross-slice dependency: $file opens LustreTodos.Feature.$dep"
			fail=1
		fi
	done <<< "$opens"
done < <(find "$src/Feature" -name '*.fs' -type f)

# Shared modules must not depend on any feature
shared=()
for dir in Shared Domain Data; do
	[[ -d "$src/$dir" ]] && shared+=("$src/$dir")
done
while IFS= read -r file; do
	if grep -qE 'open LustreTodos\.Feature\.' "$file"; then
		echo "Shared module depends on a feature: $file"
		fail=1
	fi
done < <(find "${shared[@]}" -name '*.fs' -type f)

if [[ $fail -ne 0 ]]; then
	echo "Architecture violations found (see above)"
	exit 1
fi
