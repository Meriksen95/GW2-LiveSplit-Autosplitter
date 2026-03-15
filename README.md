# Guild Wars 2 - `GW2 Auto Splitter` - LiveSplit

This tool connects Guild Wars 2 (via Mumble Link) with LiveSplit, allowing automatic start, reset, and splits based on in-game events.

## Installation

1. [Download](https://github.com/Meriksen95/ECHO-Raid-Pack/releases/latest/download/ECHO-Raid-Pack.taco) the zip file containing:
   - `GW2AutoSplitter` (folder)
   - `LiveSplit.GW2.dll`
   - `GW2 - Raid Full Clear.lss`
   - `GW2 - Autosplitter.lsl`

2. Move the `GW2AutoSplitter` folder and `LiveSplit.GW2.dll` into your `LiveSplit/Components` folder.

3. Start LiveSplit and load the provided splits and layout files:
   - Right-click the LiveSplit window -> **Open Splits** -> **From File** -> `GW2 - Raid Full Clear.lss`
   - Right-click the LiveSplit window -> **Open Layout** -> **From File** -> `GW2 - Autosplitter.lsl`

4. Configure the component:
   - Right-click the LiveSplit window
   - Choose **Edit Layout**
   - Double-click **GW2 Auto Splitter**
   - Select `FullWing` or `Route`
   - If needed, select a route file

Changes to JSON files inside `GW2AutoSplitter` are reloaded automatically.

# Documentation

## Mode: FullWing

The mode `FullWing` is used when running an entire raid wing from start to finish.

Config files are loaded from `GW2AutoSplitter/fullwings` and matched by `mapId`.

```json
{
  "mapId": 1062,
  "splits": [
    {
      "name": "Vale Guardian",
      "trigger": {
        "type": "circle",
        "x": -121.3,
        "z": -523.9,
        "radius": 16,
        "combatState": "inCombat"
      }
    }
  ]
}
```

Each split contains:

- `name` - split name shown in LiveSplit
- `trigger` - a single trigger
- `ORtrigger` - one or more trigger options and can be used on its own without `trigger`

Example with `ORtrigger`:

```json
{
  "name": "Escort",
  "ORtrigger": [
    {
      "type": "circle",
      "x": 10.0,
      "z": 25.0,
      "radius": 12
    },
    {
      "type": "map",
      "mapId": 1122
    }
  ]
}
```

Use `ORtrigger` when a split can happen in multiple ways. In that case, keep all trigger options inside `ORtrigger`.

### Trigger types

- `circle` - 2D area using `x`, `z`, and `radius`
- `sphere` - 3D area using `x`, `y`, `z`, and `radius`
- `polygon` - area defined by at least three points
- `map` - triggers when entering a specific `mapId`
- `map_not` - triggers when leaving a specific `mapId`

Examples:

```json
{
  "type": "circle",
  "x": -121.3,
  "z": -523.9,
  "radius": 16,
  "combatState": "inCombat"
}
```

```json
{
  "type": "polygon",
  "points": [
    { "x": -142.3, "z": -456.5 },
    { "x": -111.2, "z": -456.9 },
    { "x": -104.5, "z": -469.6 }
  ]
}
```

```json
{
  "type": "sphere",
  "x": 214.2,
  "y": 12.5,
  "z": -31.5,
  "radius": 25
}
```

```json
{
  "type": "map",
  "mapId": 1062
}
```

```json
{
  "type": "map_not",
  "mapId": 1062
}
```

### Optional trigger fields

- `combatState` - `inCombat` or `outOfCombat`
- `name` - stores the trigger name after it fires and automatically blocks the next trigger if it has the same name

Two triggers in a row must not use the same `name`, or the second one will never fire.

## Mode: Route

The mode `Route` is used when you want to clear encounters in a custom order.

This mode uses two folders inside `GW2AutoSplitter`:

- `encounters`
- `routes`

### Encounters

The `encounters` folder contains reusable encounter definitions:

```json
{
  "id": "w7_sabir",
  "name": "Sabir",
  "mapId": 1155,
  "splits": [
    {
      "name": "Sabir",
      "trigger": {
        "type": "circle",
        "x": 214.2,
        "z": -31.5,
        "radius": 25,
        "combatState": "inCombat"
      }
    }
  ]
}
```

Each encounter defines `id`, `name`, `mapId`, and `splits`. The split format is the same as in `FullWing`.

### Routes

The `routes` folder defines the order encounters should run in:

```json
{
  "name": "Boss Hop",
  "encounters": [
    "w1_vg",
    "w3_kc",
    "w1_sab",
    "w4_sama"
  ]
}
```

The autosplitter loads the listed encounter IDs and combines them into one run.
