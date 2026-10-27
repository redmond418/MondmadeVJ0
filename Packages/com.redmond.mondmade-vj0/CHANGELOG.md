# Changelog

## Unreleased

- Add an APC mini mk2 Editor simulator for virtual Pad, Button, Fader, LED feedback, and MIDI Learn testing without a physical controller.
- Add Float and Vector2/3/4 integrate modifiers that accumulate velocity over FrameClock delta time.
- Expand the GameObject Runtime template with Transport, installed MIDI/OSC modules, OSC Transport Sync, and a RouteHost as child objects.
- Show Base BPM and OSC port values as numeric fields instead of Range sliders.
- Refresh Route Inspector contents after editor Undo and Redo operations.
- Include the APC mini mk2 90-control Surface Profile as an importable package sample.
- Route Inspector now recursively displays parameters and compatible nested Source pickers across Sources, Modifiers, and Outputs, including inherited and list-based references, while preserving specialized PropertyDrawers.
- Made `FloatToVector2/3/4Source` copy the input float into every component; component replacement is available through `FloatToVector2/3/4ComponentSource`.
- Split `RouteHost` into its own script asset so QuickStart scenes resolve the component in Player builds.
- Bound OSC receive-queue and latest-message-cache memory use by packet count and address length.
- Bound pooled Raw MIDI input buffers to 8 MiB across the normal and Clock queues.

## 0.1.0

- Initial implementation of the MondmadeVJ0 package structure and runtime foundation.
