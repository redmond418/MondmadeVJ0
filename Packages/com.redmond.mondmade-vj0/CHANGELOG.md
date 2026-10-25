# Changelog

## Unreleased

- Route Inspector now recursively displays parameters and compatible nested Source pickers across Sources, Modifiers, and Outputs, including inherited and list-based references, while preserving specialized PropertyDrawers.
- Made `FloatToVector2/3/4Source` copy the input float into every component; component replacement is available through `FloatToVector2/3/4ComponentSource`.
- Split `RouteHost` into its own script asset so QuickStart scenes resolve the component in Player builds.
- Bound OSC receive-queue and latest-message-cache memory use by packet count and address length.
- Bound pooled Raw MIDI input buffers to 8 MiB across the normal and Clock queues.

## 0.1.0

- Initial implementation of the MondmadeVJ0 package structure and runtime foundation.
