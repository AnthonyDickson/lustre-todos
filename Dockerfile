# =====Build===== #
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

RUN apt-get update \
    && apt-get install -y ca-certificates curl gnupg just \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_24.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install Gleam
ARG GLEAM_VERSION=1.18.0

RUN curl -fsSL https://github.com/gleam-lang/gleam/releases/download/v${GLEAM_VERSION}/gleam-v${GLEAM_VERSION}-x86_64-unknown-linux-musl.tar.gz \
    | tar -xz -C /usr/local/bin

WORKDIR /src

# Restore dotnet tools and packages
COPY server/dotnet-tools.json server/
RUN cd server && dotnet tool restore

COPY server/Directory.Build.props server/Directory.Packages.props server/
COPY server/LustreTodos.slnx server/
COPY server/src/LustreTodos/LustreTodos.fsproj server/src/LustreTodos/
# The test fsproj file needs to be included due to the solution file referencing
# it despite it not being needed for the built binary.
COPY server/tests/LustreTodos.Tests/LustreTodos.Tests.fsproj server/tests/LustreTodos.Tests/
RUN cd server && dotnet restore

# Install client dependencies
COPY client/package*.json client/
RUN npm ci --prefix client

# Copy everything and publish
COPY ./justfile ./
COPY ./server ./server
COPY ./client ./client
ARG RUNTIME=linux-musl-x64
ARG APP_VERSION=0.0.0-local
ARG GIT_SHA=""
RUN RUNTIME=${RUNTIME} PUBLISH_DIR=/publish VERSION=${APP_VERSION} GIT_SHA=${GIT_SHA} just publish

# =====Runtime===== #

# Microsoft's Alpine image with only the native dependencies of a
# self-contained .NET app, which is why the build targets musl
# (linux-musl-x64). It provides the non-root `app` user (UID 1654), and
# BusyBox's wget serves the container healthcheck.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS runtime

# Mount points for the database and logs. A fresh named volume copies the
# ownership of the image directory it mounts over, so these must exist and be
# owned by app, or SQLite cannot create the database.
RUN mkdir -p /data /logs && chown app:app /data /logs

EXPOSE 5000

COPY --from=build /publish/LustreTodos /app/
COPY --from=build /publish/wwwroot/ /app/wwwroot/
COPY --from=build /publish/LustreTodos.staticwebassets.endpoints.json /app/

WORKDIR /app
USER app
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENTRYPOINT ["./LustreTodos"]
