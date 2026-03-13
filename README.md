# Guild Wars 2 - Raid LiveSplit Autosplitter

This tool connects Guild Wars 2 (via the Mumble Link) with LiveSplit, allowing automatic start, reset, and splits based on in-game events.

## Installation Steps

1. [Download](https://github.com/Meriksen95/ECHO-Raid-Pack/releases/latest/download/ECHO-Raid-Pack.taco) the zip file containing:
   - GW2Splitter (folder)
   - LiveSplit.GW2.dll
   - GW2 - Raid Full Clear.lss
   - GW2 - Autosplitter.lsl

2. Move `GW2Splitter` folder and `LiveSplit.GW2.dll` into your `LiveSplit/components` folder.

4. Start LiveSplit and load the `GW2 - Raid Full Clear.lss` and `GW2 - Autosplitter.lsl`. These contain the visuals and the techincal part behind LiveSplit.

To load the files you do the following:
- Right click LiveSplit window and choose 'open splits', choose from file and find `GW2 - Raid Full Clear.lss`. 
- Right click LiveSplit window and choose 'open layout', choose from file and find `GW2 - Autosplitter.lsl`. 


# Documentation

## Mode: FullWing

The mode `FullWing` is used when running an entire raid wing from start to finish.

Most wings are linear and only allow one path. Wing 7 and Wing 8 are exceptions. In these wings you must defeat **Sabir and Greer first**, as the splits (checkpoints) are defined in that order.

Below is an example of how splits can be defined:

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
    },
    {
      "name": "Spirit Woods",
      "trigger": {
        "type": "polygon",
        "points": [
          { "x": -142.3909, "z": -456.5393 },
          { "x": -111.2666, "z": -456.9705 },
          { "x": -104.5161, "z": -469.6414 },
          { "x": -148.2325, "z": -472.3882 }
        ]
      }
    }
  ]
```
The top section contains the `mapId`, which identifies the map instance, followed by the list of `splits`.

Each split contains two objects:

- `name` – the name of the split  
- `trigger` – defines when the split should occur  

### Trigger types

Triggers define a location or event that creates a split.  
Three trigger types are supported:

**circle**  
A circular trigger area defined by `x`, `z`, and a `radius`.

**polygon**  
A custom trigger area defined by multiple points.  
Requires at least **three points**, but can contain any number.

**map**  
Triggers a split when the current map ID matches the defined `mapId`.

### Optional conditions

Triggers can include the optional field `combatState`.

Possible values:

- `inCombat`
- `outOfCombat`

This allows splits to trigger only when the player enters or leaves combat.


## Mode: Routes

The mode `Routes` is used when a wing can be completed in different orders.  
Instead of defining all splits in a single file, encounters are separated and then combined into routes.

This mode uses the two folders inside `GW2Splitter`:

- `encounters`
- `routes`

### Encounters

The `encounters` folder contains files that define individual encounters.

Each encounter file uses the **same syntax as `FullWing`**, meaning it contains a `mapId`, a split `name`, and a `trigger`.
Note that multiple splits can be contained within a singular encounter. ex: a start split and an end split one for starting combat and ending combat.
Example:

```json
{
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
}```
