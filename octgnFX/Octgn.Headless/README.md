# Octgn.Headless

Proof of concept without a user interface for macOS and Linux (step 2 of the macOS port).
It checks, on .NET 10, that the shared libraries can:

1. load a game definition and its sets (Octgn.DataNew);
2. host a local game with the .NET 10 build of Octgn.Online.StandAloneServer (HostedGameProcess);
3. find it on the LAN through its broadcast (MS-NRBF payload read without BinaryFormatter);
4. join it with a minimal client of the game protocol (Hello, Welcome);
5. run the game's IronPython 2.7 scripts with the 3.1.0.2 scripting API, including the game's
   OnTableLoaded handlers; random numbers, chat messages and global variables go through the server;
6. read JSON as game scripts do (`System.Web.Extensions`, provided by Octgn.Compat.SystemWebExtensions);
7. let the server stop after the last player leaves.

## Run

Clone with the submodules (`git clone --recurse-submodules`, or `git submodule update --init --recursive`):
IronPython is built from `octgnFX/External/ironpython2`.

```sh
cd octgnFX/Octgn.Headless
dotnet run -c Release                                   # the Basic-Game template of Octgn.GameStudio
dotnet run -c Release -- --game <unpacked game folder>  # any game: the folder that holds definition.xml
```

Options: `--verbose` (logs and protocol messages), `--keep` (keeps the temporary data folder).
The exit code is 0 when every step succeeds.

Marvel Champions, for instance:

```sh
git clone https://github.com/twistedsistem/OCTGN-Marvel-Champions /tmp/mc
dotnet run -c Release -- --game /tmp/mc/055c536f-adba-4bc2-acbf-9aefb9756046
```

## Limits

The scripting API (`HeadlessScriptApi`) only covers what game scripts need at load time and in the
actions above: there is no table, no card and no user. Questions (`confirm`) are answered "yes".
The real engine comes with the Avalonia client (step 3 and later).
