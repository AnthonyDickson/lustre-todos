RUNTIME := env_var_or_default("RUNTIME", "linux-x64")
PUBLISH_DIR := env_var_or_default("PUBLISH_DIR", "server/src/LustreTodos/bin/Release/publish")
VERSION := env_var_or_default("VERSION", "0.0.0-local")
GIT_SHA := env_var_or_default("GIT_SHA", "")

# Restore the server's packages. Run it after the E2E stack, whose server container rewrites the restore
# output under server/ with the container's package paths, so host builds fail until a restore
server-restore:
	cd server && dotnet restore

# Build the server
server-build:
	dotnet build server/src/LustreTodos/LustreTodos.fsproj

# Run the server (auto-applies DB migrations)
server-watch:
	ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5000}" dotnet watch run --project server/src/LustreTodos --no-hot-reload

# Expecto tests (through the Expecto test adapter, same as IDE test explorers)
server-test:
	dotnet test server/LustreTodos.slnx

# npm install
client-install-deps:
	cd client && npm install

# gleeunit tests
client-test:
	cd client && gleam test

# Start the client dev server (Vite + Gleam watch)
client-watch:
	cd client && npx vite

# Production client bundle
client-build:
	cd client && npx vite build

# Copy client dist into server wwwroot/
copy-client-dist: client-build
	mkdir -p server/src/LustreTodos/wwwroot
	cp -r client/dist/* server/src/LustreTodos/wwwroot/

# Single-file publish (builds client, copies assets, publishes server)
publish: copy-client-dist
	dotnet publish server/src/LustreTodos/LustreTodos.fsproj \
		-c Release -r {{RUNTIME}} -o {{PUBLISH_DIR}} \
		-p:PublishTrimmed=true -p:TrimMode=partial \
		-p:Version={{VERSION}} -p:SourceRevisionId={{GIT_SHA}}

# Playwright E2E tests in Docker
e2e-test:
	docker compose -f docker-compose.e2e.yml up --abort-on-container-exit --exit-code-from e2e --remove-orphans
	docker compose -f docker-compose.e2e.yml down

# Format with fantomas + gleam format
format:
	dprint fmt
	gleam format
	cd server && dotnet fantomas .

# Lint with fsharplint
lint: check-strings
	cd server && dotnet fsharplint lint LustreTodos.slnx

# Reject a `\` line continuation inside a triple-quoted F# string, which keeps the backslash and the next line's
# indentation rather than joining the lines
check-strings:
	#!/usr/bin/env bash
	set -euo pipefail
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

audit:
	cd client && npm audit
	cd server && dotnet list package --vulnerable

outdated:
	cd client && gleam deps outdated
	# `-` ignores npm's exit code, which is non-zero when packages are outdated, so the next command still runs
	-cd client && npm outdated
	cd server && dotnet list package --outdated

update-deps:
	cd client && gleam deps update
	cd client && npm update
	cd server && dotnet package update
	cd server && dotnet tool update --all

# ── Database ──────────────────────────────────────────────────────────────

# Scaffold a new migration file
db-migration name:
	#!/usr/bin/env bash
	set -euo pipefail
	dir="server/src/LustreTodos/migrations"
	count=$(ls "$dir"/*.sql 2>/dev/null | wc -l)
	num=$(printf "%03d" $((count + 1)))
	file="$dir/${num}_{{name}}.sql"
	echo "-- {{name}}" > "$file"
	echo "Created migration: migrations/$(basename "$file")"

# Apply pending migrations (standalone script)
db-migrate:
	cd server && dotnet fsi scripts/migrate.fsx

# Regenerate Db.fs types from live DB (SqlHydra)
db-generate:
	cd server && dotnet sqlhydra sqlite --project src/LustreTodos/LustreTodos.fsproj

# db-migrate + db-generate (full schema update)
db-update: db-migrate db-generate

# Delete DB, re-apply all migrations, regenerate
db-reset:
	rm -f server/src/LustreTodos/app.sqlite3
	just db-update
