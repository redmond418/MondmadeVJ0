using System;
using System.Reflection;
using NUnit.Framework;

namespace MondmadeVJ0.Tests.Display
{
    public sealed class MacDisplayUrpPatchTests
    {
        private const string Original = "            RenderTargetIdentifier targetDepthId = cameraData.targetTexture != null ? new RenderTargetIdentifier(cameraData.targetTexture) : BuiltinRenderTextureType.Depth;\n                importInfo.width = Screen.width;\n                importInfo.height = Screen.height;";

        private static string Patch(string source)
        {
            var type = Type.GetType("MondmadeVJ0.Display.Editor.MacDisplayUrpPatch, MondmadeVJ0.Display.Editor", true);
            return (string)type.GetMethod("PatchSource", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source });
        }

        [Test]
        public void PatchesBothBuffersAndDimensionsAndIsIdempotent()
        {
            var patched = Patch(Original);
            Assert.That(patched, Does.Contain("new RenderTargetIdentifier(display.colorBuffer)"));
            Assert.That(patched, Does.Contain("new RenderTargetIdentifier(display.depthBuffer)"));
            Assert.That(patched, Does.Contain("displays[targetDisplay].renderingWidth"));
            Assert.That(patched, Does.Contain("displays[targetDisplay].renderingHeight"));
            Assert.That(Patch(patched), Is.EqualTo(patched));
        }

        [Test]
        public void RejectsChangedDuplicateOrIncompleteSource()
        {
            Assert.That(Assert.Throws<TargetInvocationException>(() => Patch("unknown URP source")).InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(Assert.Throws<TargetInvocationException>(() => Patch(Original + "\n" + Original)).InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(Assert.Throws<TargetInvocationException>(() => Patch("MONDMADE_VJ0_MAC_DISPLAY_BACKBUFFERS")).InnerException, Is.TypeOf<InvalidOperationException>());
        }
    }
}
