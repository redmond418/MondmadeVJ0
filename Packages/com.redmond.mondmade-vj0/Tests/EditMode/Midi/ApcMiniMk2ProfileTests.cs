using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class ApcMiniMk2ProfileTests
    {
        [Test]
        public void ApcProfileContainsAllLogicalControls()
        {
            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            try
            {
                Assert.That(profile.Controls.Count, Is.EqualTo(90));
                Assert.That(profile.ProductName, Is.EqualTo("APC mini mk2"));
                Assert.That(profile.DriverId, Is.EqualTo("apc-mini-mk2"));
                Assert.That(profile.Find("pad_0_0").inputNumber, Is.EqualTo(0));
                Assert.That(profile.Find("pad_0_0").outputNumber, Is.EqualTo(0));
                Assert.That(profile.Find("pad_0_7").inputNumber, Is.EqualTo(56));
                Assert.That(profile.Find("pad_0_7").outputNumber, Is.EqualTo(56));
                Assert.That(profile.Find("pad_7_7").inputNumber, Is.EqualTo(63));
                Assert.That(profile.Find("pad_7_7").outputNumber, Is.EqualTo(63));
                Assert.That(profile.Find("fader_9").inputNumber, Is.EqualTo(0x38));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void ApcProfileCanBeSavedAndReloadedAsAnAsset()
        {
            var path = "Assets/MondmadeVJ0-APC-profile-" + Guid.NewGuid().ToString("N") + ".asset";
            var profile = MidiSurfaceProfile.CreateApcMiniMk2Profile();
            try
            {
                Assert.That(MonoScript.FromScriptableObject(profile), Is.Not.Null);
                AssetDatabase.CreateAsset(profile, path);
                AssetDatabase.SaveAssets();

                var reloaded = AssetDatabase.LoadAssetAtPath<MidiSurfaceProfile>(path);
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.Controls.Count, Is.EqualTo(90));
                Assert.That(reloaded.Find("scene_8").inputNumber, Is.EqualTo(0x77));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [Test]
        public void QuickStartAssetMatchesApcMiniMk2PadNoteLayout()
        {
            var profile = AssetDatabase.LoadAssetAtPath<MidiSurfaceProfile>(
                "Assets/MondmadeVJ0.Dev/Midi/APC-mini-mk2-QuickStart.asset");
            if (profile == null)
                Assert.Ignore("The development QuickStart asset is not included in package-only consumer projects.");
            Assert.That(profile.Controls.Count, Is.EqualTo(90));

            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var control = profile.Find($"pad_{x}_{y}");
                var note = y * 8 + x;
                Assert.That(control, Is.Not.Null, $"Pad [{x},{y}] must exist.");
                Assert.That(control.inputNumber, Is.EqualTo(note), $"Pad [{x},{y}] input note.");
                Assert.That(control.outputNumber, Is.EqualTo(note), $"Pad [{x},{y}] output note.");
            }
        }

        [Test]
        public void SolidApcPadPalettePayloadConvertsToExpectedRgbAtSelectedBrightness()
        {
            var control = new MidiSurfaceControl
            {
                id = "pad",
                outputAddressType = MidiAddressType.Note,
                outputNumber = 0,
                hasLed = true
            };
            var fullBrightness = MidiLedEncoder.Encode("apc-mini-mk2", control,
                new MidiLedStyle { color = Color.green, brightness = 1f, effect = MidiLedEffect.Solid });
            MidiLedEncoder.EncodeApcPadRgb(fullBrightness, out var red, out var green, out var blue);
            Assert.That(fullBrightness.Value, Is.EqualTo(21));
            Assert.That(red, Is.Zero);
            Assert.That(green, Is.EqualTo(255));
            Assert.That(blue, Is.Zero);

            var dim = MidiLedEncoder.Encode("apc-mini-mk2", control,
                new MidiLedStyle { color = Color.green, brightness = 0.1f, effect = MidiLedEffect.Solid });
            MidiLedEncoder.EncodeApcPadRgb(dim, out red, out green, out blue);
            Assert.That(green, Is.EqualTo(26));
        }
    }
}
