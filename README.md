# godot-csharp-starter

When starting a new project, find-and-replace all `starter-template` occurrences
(assembly name, Docker binary, DTLS cert CN, README title).

Godot 4.4 + C# template with Git LFS, csharpier, and a dedicated authoritative
server (ENet + DTLS) for local Docker Compose. A capsule pawn demonstrates
client-side prediction and remote interpolation; replace it with your game.

## Prerequisites

- [Godot .NET / C#](https://godotengine.org/download) — see [`.godot-version`](.godot-version) for the required version.
- [.NET SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for local dedicated server)
- Export templates for Godot (install from the editor if prompted)

## Local multiplayer

The game server runs as a separate headless process. Run from repo root.

Requires Docker Compose 2.22+ (included in current Docker Desktop) for watch mode.

```bash
./run build    # build and start (detached)
./run logs     # follow server output
./run watch    # rebuild on C# / scene / asset changes
./run stop     # graceful SIGTERM stop
./run down     # tear down compose stack
```

On Windows, use Git Bash or WSL to run `./run`, or use the
`docker compose` commands below.

### Start the dedicated server (Docker)

```bash
./run build
# or: docker compose up --build -d game-server
```

This builds and runs the `game-server` container on **UDP port 7000** with
`--dev-mode` (stub session validation). `docker-compose.yml` passes
`GODOT_EXPORT=debug` so the server matches an **editor** client (Debug C# RPC
layout). For a production-style image:

```bash
docker compose build --build-arg GODOT_EXPORT=release game-server
docker compose up -d game-server
```

The image copies the whole Linux server export folder (binary, `.pck`, and
`data_*` .NET runtime) — Godot’s default layout when Embed PCK is off.

After a successful start you should see `[Bootstrap] dedicated_server=True ...`
and `Server started on port 7000 (DTLS enabled)`.

**Editor client + Docker server:** use the default compose build (`GODOT_EXPORT=debug`).
A **release** server against an editor client will fail RPC checksum negotiation.
After any networking/C# change, rebuild (`./run build`) or leave
`./run watch` running.

Do not pass `--log-file` on Godot 4.4.1 here — it can crash the headless binary
at startup.

Stop:

```bash
./run down
```

### Connect a local client

1. Open the project in Godot (see [`.godot-version`](.godot-version)).
2. Rebuild the server image if you changed C# since the last build (or use
   `./run watch`).
3. Use the Lobby with address `127.0.0.1:7000`, **or** skip the Lobby:

   ```
   --connect=127.0.0.1:7000
   ```

   (Project → Debug → Customize Run Arguments in the editor.)

4. The editor enables `--dev-mode` handshake automatically; exported clients
   should pass `--dev-mode` or set `GAME_SESSION_VALIDATE_DISABLED=1`.

### Escape hatches

```bash
docker build -f docker/server/Dockerfile -t starter-template-server .
docker run --rm -p 7000:7000/udp starter-template-server
```

Native headless (exports a **Linux** binary; prefer Docker on Windows):

```bash
mkdir -p build
godot --headless --path . --export-release "Server Linux" build/starter-template-server
./build/starter-template-server --headless --server --dev-mode
```

## Build and format

```bash
dotnet build starter-template.csproj
dotnet tool restore
dotnet csharpier .
dotnet csharpier --check .
```

Conventions for contributors and AI assistants: [`AGENTS.md`](AGENTS.md).
