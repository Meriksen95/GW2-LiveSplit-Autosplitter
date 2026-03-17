# Guild Wars 2 - `GW2 Auto Splitter` - LiveSplit

This tool connects Guild Wars 2 (via Mumble Link) with LiveSplit, allowing automatic start, reset, and splits based on in-game events.

## Installation

1. [Download](https://github.com/Meriksen95/ECHO-Raid-Pack/releases/latest/download/ECHO-Raid-Pack.taco) the package.

2. Move the `GW2AutoSplitter` folder and `LiveSplit.GW2.dll` into your `LiveSplit/Components` folder.

3. `GW2AutoSplitter` now contains four folders and one file:
   - `encounters` - reusable encounter definitions for `Route` mode
   - `fullwings` - full wing configs for `FullWing` mode
   - `routes` - route definitions that reference encounters
   - `Split files - visual` - provided `.lss` split files and `.lsl` layout file
   - `component-settings.xml` - persisted component settings created/updated by the autosplitter

4. Start LiveSplit and load the provided visual files if you want the included split and layout setup:
   - Right-click the LiveSplit window -> **Open Splits** -> **From File** -> choose an `.lss` file from `GW2AutoSplitter/Split files - visual`
   - Right-click the LiveSplit window -> **Open Layout** -> **From File** -> choose the `.lsl` file from `GW2AutoSplitter/Split files - visual`

5. If you do not use the provided `.lsl` layout, add the component manually:
   - Right-click the LiveSplit window
   - Choose **Edit Layout**
   - Click the `+` button
   - Under `Controls`, add `GW2 Auto Splitter`

6. Configure the component:
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
      "name": "place1",
      "type": "circle",
      "x": 10.0,
      "z": 25.0,
      "radius": 12
    },
    {
      "name": "entering map",
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
  "combatState": "inCombat",
  "yAbove": 10.0
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
- `yAbove` - only triggers if the player's `y` value is strictly greater than this value
- `yBelow` - only triggers if the player's `y` value is strictly lower than this value
- `name` - stores the trigger name after it fires and automatically blocks the next trigger if it has the same name

Two triggers in a row must not use the same `name`, or the second one will never fire.

`yAbove` and `yBelow` can be combined to limit a trigger to a vertical range. This is useful when two triggers overlap on `x`/`z` but sit on different heights.


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

Each encounter defines `id`, `mapId`, and `splits`. For consistency, the visible split name should be placed on the split itself with `name`. The split format is the same as in `FullWing`.

Routes reference encounters by their `id`. The encounter file name does not need to match the `id`, although keeping them similar makes the files easier to manage.

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
