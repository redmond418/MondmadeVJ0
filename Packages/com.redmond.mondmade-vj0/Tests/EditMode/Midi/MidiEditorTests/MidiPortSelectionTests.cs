using System.Collections.Generic;
using System.Reflection;
using MondmadeVJ0.Editor;
using NUnit.Framework;

namespace MondmadeVJ0.Tests
{
    public sealed class MidiPortSelectionTests
    {
        private static readonly MethodInfo FindAvailablePortMethod = typeof(MidiHubEditor).GetMethod(
            "FindAvailablePort", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void SelectsTheNextUnusedControlPortForSameNameSurfaces()
        {
            var ports = new[]
            {
                "APC mini mk2 Control",
                "APC mini mk2 Notes",
                "APC mini mk2 Control",
                "APC mini mk2 Notes"
            };

            Assert.That(FindAvailablePort(ports, new HashSet<int>()), Is.EqualTo(0));
            Assert.That(FindAvailablePort(ports, new HashSet<int> { 0 }), Is.EqualTo(2));
            Assert.That(FindAvailablePort(ports, new HashSet<int> { 0, 2 }), Is.EqualTo(-1));
        }

        [Test]
        public void PrefersControlPortAndAvoidsNotesPortFallback()
        {
            Assert.That(FindAvailablePort(new[] { "APC mini mk2 Notes", "APC mini mk2 Control" }, new HashSet<int>()), Is.EqualTo(1));
            Assert.That(FindAvailablePort(new[] { "APC mini mk2" }, new HashSet<int>()), Is.EqualTo(0));
            Assert.That(FindAvailablePort(new[] { "APC mini mk2 Notes" }, new HashSet<int>()), Is.EqualTo(-1));
        }

        private static int FindAvailablePort(string[] ports, ISet<int> usedOrdinals)
        {
            Assert.That(FindAvailablePortMethod, Is.Not.Null, "MidiHubEditor port selection helper should be present.");
            return (int)FindAvailablePortMethod.Invoke(null, new object[] { ports, "apc mini mk2", "control", usedOrdinals });
        }
    }
}
