using System;
using Game.Network;
using Godot;

namespace Game;

public partial class Bootstrap : Node
{
    private const string MainScenePath = "res://scenes/main.tscn";
    private const int ShutdownHardTimeoutMs = 25_000;

    private enum PostMainLoadAction
    {
        None,
        StartServer,
        JoinServer,
    }

    private BootConfig _config = BootConfig.Parse();
    private PostMainLoadAction _postMainLoadAction = PostMainLoadAction.None;
    private string _joinHost = "127.0.0.1";
    private int _joinPort = 7000;
    private bool _shutdownRequested;
    private long _shutdownRequestedAtMs;

    public override void _Ready()
    {
        _config = BootConfig.Parse();
        InstallSignalHandlers();

        var connect = _config.ConnectEndpoint is { } endpoint
            ? $"{endpoint.Host}:{endpoint.Port}"
            : "none";
        GD.Print(
            $"[Bootstrap] dedicated_server={OS.HasFeature("dedicated_server")} "
                + $"isServer={_config.IsServer} connect={connect} port={_config.Port}"
        );

        if (_config.ConnectEndpoint is { } connectEndpoint)
        {
            _postMainLoadAction = PostMainLoadAction.JoinServer;
            _joinHost = connectEndpoint.Host;
            _joinPort = connectEndpoint.Port;
            BeginLoadMainScene();
            return;
        }

        if (_config.IsServer || OS.HasFeature("dedicated_server"))
        {
            _postMainLoadAction = PostMainLoadAction.StartServer;
            BeginLoadMainScene();
        }
    }

    public override void _Process(double delta)
    {
        TryFinishMainSceneBoot();

        if (!_shutdownRequested)
        {
            return;
        }

        var networkManager = GetNodeOrNull<NetworkManager>(
            "/root/NetworkManager"
        );
        if (networkManager is null)
        {
            GetTree().Quit(0);
            return;
        }

        if (!networkManager.IsShutdownDrainComplete())
        {
            var elapsed = (long)Time.GetTicksMsec() - _shutdownRequestedAtMs;
            if (elapsed >= ShutdownHardTimeoutMs)
            {
                GD.PrintErr(
                    "[Bootstrap] Shutdown hard timeout reached; forcing quit"
                );
                GetTree().Quit(0);
            }

            return;
        }

        GetTree().Quit(0);
    }

    private void BeginLoadMainScene()
    {
        GetTree()
            .CallDeferred(
                SceneTree.MethodName.ChangeSceneToFile,
                MainScenePath
            );
    }

    private void TryFinishMainSceneBoot()
    {
        if (_postMainLoadAction == PostMainLoadAction.None)
        {
            return;
        }

        var tree = GetTree();
        if (
            tree.CurrentScene is null
            || tree.CurrentScene.SceneFilePath != MainScenePath
        )
        {
            return;
        }

        var action = _postMainLoadAction;
        _postMainLoadAction = PostMainLoadAction.None;

        var networkManager = GetNodeOrNull<NetworkManager>(
            "/root/NetworkManager"
        );
        if (networkManager is null)
        {
            GD.PushError(
                "[Bootstrap] NetworkManager autoload missing after scene load"
            );
            return;
        }

        networkManager.ApplyBootConfig(_config);
        switch (action)
        {
            case PostMainLoadAction.StartServer:
                networkManager.StartServer();
                break;
            case PostMainLoadAction.JoinServer:
                networkManager.JoinServer(_joinHost, _joinPort);
                break;
        }
    }

    private void InstallSignalHandlers()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            OnProcessExit(null, EventArgs.Empty);
        };
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _shutdownRequestedAtMs = (long)Time.GetTicksMsec();
        Callable.From(HandleProcessExitOnMainThread).CallDeferred();
    }

    private void HandleProcessExitOnMainThread()
    {
        var networkManager = GetNodeOrNull<NetworkManager>(
            "/root/NetworkManager"
        );
        networkManager?.RequestShutdown();
    }
}
