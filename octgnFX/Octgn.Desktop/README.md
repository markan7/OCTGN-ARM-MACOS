# Octgn.Desktop

A first OCTGN game table in Avalonia 12, for macOS (Apple silicon) and Linux (step 3 of the macOS
port). It plays with the engine of the Windows client, built for .NET 10 (`Octgn.Engine`).

## Run

Clone with the submodules, install the .NET 10 SDK, then:

```sh
cd octgnFX/Octgn.Desktop
dotnet run -c Release
```

The first window lists the games of the game database. Add one from its folder (the folder of its
`definition.xml`, as in an unpacked `.o8g`), choose a nickname, and host a game on this computer: the
game server (`Octgn.Online.StandAloneServer`) starts in a process of its own. Another computer, or a
second instance, joins it with "Join" and the address and port of the game.

Command line: `--game <game folder>` hosts that game right away, `--deck <file.o8d>` loads a deck once
the game has started, `--nick <name>`, `--data <folder>` (another OCTGN data folder), `--verbose` (logs).

```sh
git clone https://github.com/twistedsistem/OCTGN-Marvel-Champions /tmp/mc
dotnet run -c Release -- --game /tmp/mc/055c536f-adba-4bc2-acbf-9aefb9756046
```

## The table

- **Table**: drag a card to move it (with the selected cards); Shift held when the drag starts: the
  card lands face down. Drag empty space to pan; Shift and drag to select. Scroll to pan; ⌘ or Ctrl and
  scroll, or pinch, to zoom; the toolbar's −, Fit and + too.
- **Double click**: the default action of the game (Draw on a deck, for instance).
- **Right click**: the actions of the game for a card or a group, "Move to" a pile, look at a pile.
- **Keyboard**: the game's shortcuts; Ctrl in a shortcut of the game is Ctrl or ⌘.
- **Player area**: counters, the expanded piles (the hand of most games) spread out, the other piles
  (yours, then the shared ones) in a row that scrolls.
- **Right panel**: the hovered card, larger, and the chat.

The questions of the scripts (askChoice, askInteger, askString, confirm, askMarker, askCard, card
lists, files) are windows of the table.

## Not there yet

Proxy pictures (cards without a picture show their name over the game's generic front), sounds,
limited games (sealed, draft), the deck editor, the other players' piles (a summary line only),
two-sided tables, replays, the game lobby and online games, notes, the turn and phase controls.

## Tests

`Octgn.Desktop.Test` runs the table without a screen (Avalonia's headless platform, Skia drawing) and
saves pictures of it: `dotnet run -c Release` in that folder. With `OCTGN_MARVEL_CHAMPIONS` set to the
folder of Marvel Champions, it also loads a hero of that game through its scripts.
