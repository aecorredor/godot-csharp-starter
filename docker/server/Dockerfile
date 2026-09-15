# syntax=docker/dockerfile:1
# Build stage: export dedicated Server Linux binary with godot-ci (must match .godot-version).
# Export preset is x86_64; keep build + runtime on amd64 (Windows CI and Apple Silicon via QEMU/Rosetta).
FROM --platform=linux/amd64 barichello/godot-ci:mono-4.4.1 AS build

WORKDIR /project
COPY . .

RUN --mount=type=cache,target=/project/.godot,id=starter-godot-import,sharing=locked \
    --mount=type=cache,target=/root/.nuget/packages,id=starter-nuget,sharing=locked \
    mkdir -p build .godot && \
    godot --headless --path . --import

ARG GODOT_EXPORT=release
RUN if [ "$GODOT_EXPORT" = "debug" ]; then \
        godot --headless --path . --export-debug "Server Linux" build/starter-template-server; \
    else \
        godot --headless --path . --export-release "Server Linux" build/starter-template-server; \
    fi

# Runtime: slim image with tini for signal forwarding (SIGTERM graceful shutdown).
FROM --platform=linux/amd64 debian:bookworm-slim

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ca-certificates \
        libfontconfig1 \
        libfreetype6 \
        libicu72 \
        tini && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
# Ship the full Godot export output (see docs.godotengine.org dedicated-server + .NET data directory).
COPY --from=build /project/build/ /app/

EXPOSE 7000/udp

ENV GAME_BIND_PORT=7000
ENV GAME_MAX_PLAYERS=8
ENV GAME_VERSION=0.0.1

ENTRYPOINT ["/usr/bin/tini", "--"]
# Godot only exposes flags after "--" via OS.GetCmdlineUserArgs(); also merge boot flags from engine args in BootConfig.
CMD ["/app/starter-template-server", "--headless", "--verbose", "--", "--server", "--dev-mode"]
