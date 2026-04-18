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

        // If the launcher-driven auto-connect already advanced RunLevel before we subscribed,
        // drive the initial state from the current level instead of waiting on a future event.
        switch (_client.RunLevel) {
            case ClientRunLevel.InGame:
            case ClientRunLevel.Connected:
            case ClientRunLevel.SinglePlayerGame:
                _stateManager.RequestStateChange<InGameState>();
                break;

            case ClientRunLevel.Connecting:
                _stateManager.RequestStateChange<ConnectingState>();
                break;

            default:
                RequestMainMenuOrLauncherReconnect();
                break;
        }
    }

    /// <summary>
    ///     If the client was launched by the SS14 launcher but ended up sitting at the
    ///     Initialize run-level (no auto-connect fired, or it fired and bounced back),
    ///     explicitly kick a connect to LaunchState.ConnectEndpoint instead of parking at the
    ///     splash screen — the splash's Connect button is a no-op in launcher mode.
    /// </summary>
    private void RequestMainMenuOrLauncherReconnect() {
        if (_gameController.LaunchState is { FromLauncher: true, ConnectEndpoint: { } endpoint }) {
            _sawmill.Info($"Launcher-mode detected at idle; connecting to {endpoint}");
            _client.ConnectToServer(endpoint);
            return;
        }

        _stateManager.RequestStateChange<MainMenuState>();
    }
}
