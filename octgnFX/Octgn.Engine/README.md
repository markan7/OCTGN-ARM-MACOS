# Octgn.Engine

The game engine of the WPF client (`Octgn.JodsEngine`) built for .NET 10, without WPF, for macOS and
Linux (step 3 of the macOS port). It is the engine a cross-platform user interface plays with
(`Octgn.Desktop`, in Avalonia):

- the table model: `Card`, `Group`, `Pile`, `Table`, `Player`, `Counter`, `Marker`, the actions, the
  selection, the game and player save states, the history and the replays;
- the protocol client (`ClientSocket`, `Handler`, the generated stubs);
- the IronPython script engine (`Scripting/Engine.cs`) and the script API versions 3.1.0.0, 3.1.0.1
  and 3.1.0.2 with their Python side.

## Shared sources

The source files are those of `Octgn.JodsEngine`, linked (see `JodsEngineSource` in the project), not
copied: the Windows client compiles them as before. In them, `#if NETCOREAPP` marks what this build does
in place of WPF; the `#else` branch, or the code under `#if !NETCOREAPP`, is the WPF code, unchanged.
The legacy WPF project does not define `NETFRAMEWORK`, hence `NETCOREAPP` here.

A few classes of the shared files are `partial`, so that this build adds members in `Portable/`
without changing the WPF code: `Program`, `GameEngine`, `Card`, `Player`, `ScriptApi`, `Handler`.

## Portable/

| File | What it is |
|---|---|
| `PlayUi.cs` | `IPlayUi`: what the engine asks of the user interface (script questions, pile views, errors, sounds…), and `HeadlessPlayUi`, a user interface without user. |
| `GameSession.cs` | Starts a game as the WPF loaders, `JoinGameLauncher` and `PlayWindow` do: data folder, game database, local server, connection, `StartTable` once the game starts. |
| `Dispatcher.cs` | The game dispatcher (`Program.Dispatcher`), in place of WPF's: a user interface gives its UI thread, `DispatcherThread` is a thread of its own. |
| `Media.cs`, `Input.cs` | `Color` (from `Octgn.Core.Media`), `Point`, `ColorConverter`, `KeyGesture`. |
| `Pictures.cs` | `Card.GetPictureUri` and `Player.SleeveImage`: which picture to show; the user interface loads it. |
| `PackUri.cs` | The WPF resources the game data refers to by `pack://application:,,,/` URIs, copied next to the assembly. |
| `StandIns.cs` | Same-named stand-ins for the services the shared code calls (`Sounds`, `Properties.Resources`, `SubscriptionModule`, `PilePosition`). |
| `ScriptApi.cs`, `ClientHandler.cs`, `Program.cs` | The members of the shared classes this build replaces (`lookAt`, pack names, `Exit`, `LaunchUrl`). |
| `GameMessageText.cs` | The text of a game message, for the game log and the chat. |

## Not in this build (yet)

Sounds are played by the user interface (`IPlayUi.PlaySound`); subscriptions are not checked (deck
sleeves of online games, subscriber sounds); Windows Forms windows of game scripts cannot be shown;
proxy pictures need a renderer (`Octgn.ProxyGenerator.Rendering.ProxyRendering.Renderer`).

## Tests

`Octgn.Test.CrossPlatform/Engine/GameEngineTests.cs` plays a local game end to end with this engine:
the .NET 10 server, a deck, the game's scripts (draw, questions, flip, random numbers, shuffle, lookAt).
