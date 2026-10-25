using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MondmadeVJ0
{
    [Serializable]
    public sealed class MidiSurfaceControl
    {
        public string id = "control";
        public string label = "Control";
        public MidiControlType type;
        public MidiAddressType inputAddressType = MidiAddressType.ControlChange;
        [Range(-1, 15)] public int channel = -1;
        [Range(0, 127)] public int inputNumber;
        public MidiRelativeMode relativeMode;
        public float initialValue;
        public float minimum;
        public float maximum = 1f;
        [Range(0f, 1f)] public float pressThreshold = 0.5f;
        public string groupId;
        public int groupIndex;
        public bool hasLed;
        public MidiAddressType outputAddressType = MidiAddressType.Note;
        [Range(0, 15)] public int outputChannel;
        [Range(0, 127)] public int outputNumber;
    }

    [CreateAssetMenu(menuName = "Mondmade VJ0/MIDI Surface Profile", fileName = "MidiSurfaceProfile")]
    public sealed class MidiSurfaceProfile : ScriptableObject
    {
        [SerializeField] private string driverId = "generic";
        [SerializeField] private string productName = "";
        [SerializeField] private MidiSurfaceControl[] controls = Array.Empty<MidiSurfaceControl>();

        public string DriverId => driverId;
        public string ProductName => productName;
        public IReadOnlyList<MidiSurfaceControl> Controls => controls;
        public MidiSurfaceControl Find(string id) => controls?.FirstOrDefault(control => control != null && string.Equals(control.id, id, StringComparison.Ordinal));

        public void Configure(string driver, string product, MidiSurfaceControl[] definitions)
        {
            driverId = driver ?? "generic";
            productName = product ?? string.Empty;
            controls = definitions ?? Array.Empty<MidiSurfaceControl>();
        }

        public static MidiSurfaceProfile CreateApcMiniMk2Profile()
        {
            var profile = CreateInstance<MidiSurfaceProfile>();
            var definitions = new List<MidiSurfaceControl>(90);
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var note = y * 8 + x;
                    definitions.Add(new MidiSurfaceControl { id = $"pad_{x}_{y}", label = $"Pad [{x},{y}]", type = MidiControlType.VelocityButton, inputAddressType = MidiAddressType.Note, channel = -1, inputNumber = note, groupId = "grid", groupIndex = y * 8 + x, hasLed = true, outputAddressType = MidiAddressType.Note, outputNumber = note });
                }
            }
            for (var i = 0; i < 8; i++)
                definitions.Add(new MidiSurfaceControl { id = $"track_{i + 1}", label = $"Track Button {i + 1}", type = MidiControlType.Button, inputAddressType = MidiAddressType.Note, channel = 0, inputNumber = 0x64 + i, groupId = "track", groupIndex = i, hasLed = true, outputAddressType = MidiAddressType.Note, outputNumber = 0x64 + i });
            for (var i = 0; i < 8; i++)
                definitions.Add(new MidiSurfaceControl { id = $"scene_{i + 1}", label = $"Scene Launch {i + 1}", type = MidiControlType.Button, inputAddressType = MidiAddressType.Note, channel = 0, inputNumber = 0x70 + i, groupId = "scene", groupIndex = i, hasLed = true, outputAddressType = MidiAddressType.Note, outputNumber = 0x70 + i });
            definitions.Add(new MidiSurfaceControl { id = "shift", label = "Shift", type = MidiControlType.Button, inputAddressType = MidiAddressType.Note, channel = 0, inputNumber = 0x7A });
            for (var i = 0; i < 9; i++)
                definitions.Add(new MidiSurfaceControl { id = $"fader_{i + 1}", label = $"Fader {i + 1}", type = MidiControlType.AbsoluteFader, inputAddressType = MidiAddressType.ControlChange, channel = 0, inputNumber = 0x30 + i, minimum = 0f, maximum = 1f });
            profile.Configure("apc-mini-mk2", "APC mini mk2", definitions.ToArray());
            return profile;
        }
    }
}
