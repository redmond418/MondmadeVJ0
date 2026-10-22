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
                Assert.That(profile.Find("pad_0_0").inputNumber, Is.EqualTo(56));
                Assert.That(profile.Find("pad_0_0").outputNumber, Is.EqualTo(56));
                Assert.That(profile.Find("pad_7_7").inputNumber, Is.EqualTo(7));
                Assert.That(profile.Find("pad_7_7").outputNumber, Is.EqualTo(7));
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
    }
}
