using JetBrains.Annotations;
using OpenDreamClient.States.Connecting;
using OpenDreamClient.States.MainMenu;
using Robust.Client;
using Robust.Client.State;
using Robust.Shared.Log;

namespace OpenDreamClient.States;

/// <summary>
///     Handles changing the UI state depending on connection status.
/// </summary>
[UsedImplicitly]
public sealed class DreamUserInterfaceStateManager {
    [Dependency] private readonly IGameController _gameController = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly IBaseClient _client = default!;

    private ISawmill _sawmill = default!;
    private bool _launcherConnectKicked;

    public void Initialize() {
        _sawmill = Logger.GetSawmill("opendream.state");

        _sawmill.Info(
            $"UI state manager init: RunLevel={_client.RunLevel}, FromLauncher={_gameController.LaunchState?.FromLauncher}, ConnectEndpoint={_gameController.LaunchState?.ConnectEndpoint}");

        _client.RunLevelChanged += ((_, args) => {
            _sawmill.Info($"RunLevel changed: {args.OldLevel} -> {args.NewLevel}");

            switch (args.NewLevel) {
                case ClientRunLevel.InGame:
                case ClientRunLevel.Connected:
                case ClientRunLevel.SinglePlayerGame:
                    _stateManager.RequestStateChange<InGameState>();
                    break;

                case ClientRunLevel.Initialize when args.OldLevel < ClientRunLevel.Connected:
                    RequestMainMenuOrLauncherReconnect();
                    break;

                // When we disconnect from the server:
                case ClientRunLevel.Error:
                case ClientRunLevel.Initialize when args.OldLevel >= ClientRunLevel.Connected:
                    // TODO: Reconnect without returning to the launcher
                    // The client currently believes its still connected at this point and will refuse
                    if (_gameController.LaunchState is {
                            FromLauncher: true,
                            Ss14Address: not null
                        }) {
                        _gameController.Redial(_gameController.LaunchState.Ss14Address, "Connection lost; attempting reconnect");

                        break;
                    }

                    _stateManager.RequestStateChange<MainMenuState>();
                    break;

                case ClientRunLevel.Connecting:
                    _stateManager.RequestStateChange<ConnectingState>();
                    break;
            }
        });
    }

    /// <summary>
    ///     Called from EntryPoint.PostInit() after the UI manager is ready. Drives the initial
    ///     state from the current RunLevel (in case Robust's auto-connect raced ahead of our
    ///     event subscription) and, when launched by the SS14 launcher, explicitly kicks a
    ///     connect to LaunchState.ConnectEndpoint — stock OpenDream's MainMenu splash is a
    ///     dead-end in launcher mode since Robust rejects manual ConnectToServer calls there.
    /// </summary>
    public void PostInitialize() {
        _sawmill.Info($"Post-init: RunLevel={_client.RunLevel}");

        switch (_client.RunLevel) {
            case ClientRunLevel.InGame:
            case ClientRunLevel.Connected:
            case ClientRunLevel.SinglePlayerGame:
                _stateManager.RequestStateChange<InGameState>();
                return;

            case ClientRunLevel.Connecting:
                _stateManager.RequestStateChange<ConnectingState>();
                return;
        }

        RequestMainMenuOrLauncherReconnect();
    }

    private void RequestMainMenuOrLauncherReconnect() {
        if (!_launcherConnectKicked
            && _gameController.LaunchState is { FromLauncher: true, ConnectEndpoint: { } endpoint }) {
            _launcherConnectKicked = true;
            _sawmill.Info($"Launcher-mode detected at idle; connecting to {endpoint}");
            _client.ConnectToServer(endpoint);
            return;
        }

        _sawmill.Info("Falling back to MainMenuState (launcher connect unavailable or already attempted)");
        _stateManager.RequestStateChange<MainMenuState>();
    }
}
