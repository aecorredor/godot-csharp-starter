using System;
using System.Collections.Generic;
using Godot;
using Utils;
using Environment = System.Environment;

namespace Game;

public sealed class BootConfig
{
    private static readonly HashSet<string> KnownCliKeys = new(
        StringComparer.Ordinal
    )
    {
        "server",
        "dev-mode",
        "connect",
        "port",
        "max-players",
        "game-version",
        "session-validate-disabled",
    };

    public bool IsServer { get; init; }
    public EndpointParser.Endpoint? ConnectEndpoint { get; init; }
    public int Port { get; init; } = 7000;
    public int MaxPlayers { get; init; } = 8;
    public string GameVersion { get; init; } = "0.0.1";
    public bool DevMode { get; init; }
    public bool SessionValidateDisabled { get; init; }

    public static BootConfig Parse(string[]? args = null)
    {
        var cli = args is not null
            ? ParseCliArgs(args)
            : ParseCliArgsFromProcess();
        var env = Environment.GetEnvironmentVariables();

        string? Get(string cliKey, string envKey) =>
            cli.GetValueOrDefault(cliKey) ?? env[envKey] as string;

        bool HasFlag(string cliKey) => cli.ContainsKey(cliKey);

        EndpointParser.Endpoint? connect = null;
        var connectRaw = Get("connect", "GAME_CONNECT");
        if (
            !string.IsNullOrWhiteSpace(connectRaw)
            && EndpointParser.TryParse(connectRaw, out var parsed)
        )
        {
            connect = parsed;
        }

        return new BootConfig
        {
            IsServer = HasFlag("server"),
            ConnectEndpoint = connect,
            Port = ParseInt(Get("port", "GAME_BIND_PORT"), 7000),
            MaxPlayers = ParseInt(Get("max-players", "GAME_MAX_PLAYERS"), 8),
            GameVersion = Get("game-version", "GAME_VERSION") ?? "0.0.1",
            DevMode = HasFlag("dev-mode"),
            SessionValidateDisabled = ParseBool(
                Get(
                    "session-validate-disabled",
                    "GAME_SESSION_VALIDATE_DISABLED"
                )
            ),
        };
    }

    private static Dictionary<string, string> ParseCliArgsFromProcess()
    {
        var cli = ParseCliArgs(OS.GetCmdlineUserArgs());
        foreach (var arg in OS.GetCmdlineArgs())
        {
            if (!TryParseKnownCliArg(arg, out var key, out var value))
            {
                continue;
            }

            cli.TryAdd(key, value);
        }

        return cli;
    }

    private static Dictionary<string, string> ParseCliArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            if (TryParseKnownCliArg(arg, out var key, out var value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static bool TryParseKnownCliArg(
        string arg,
        out string key,
        out string value
    )
    {
        key = string.Empty;
        value = "true";
        if (!arg.StartsWith("--"))
        {
            return false;
        }

        var body = arg[2..];
        var eq = body.IndexOf('=');
        if (eq >= 0)
        {
            key = body[..eq];
            value = body[(eq + 1)..];
        }
        else
        {
            key = body;
        }

        return KnownCliKeys.Contains(key);
    }

    private static int ParseInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) ? value : fallback;

    private static bool ParseBool(string? raw) =>
        raw is "1" or "true" or "yes" or "on";
}
