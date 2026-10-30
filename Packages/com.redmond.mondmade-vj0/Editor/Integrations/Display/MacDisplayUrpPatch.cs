using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace MondmadeVJ0.Display.Editor
{
    /// <summary>Applies the macOS multi-display backbuffer workaround to an embedded URP 17.3 package.</summary>
    [InitializeOnLoad]
    internal static class MacDisplayUrpPatch
    {
        private const string PackageName = "com.unity.render-pipelines.universal";
        private const string SourcePath = "Packages/" + PackageName + "/Runtime/UniversalRendererRenderGraph.cs";
        private const string PendingKey = "MondmadeVJ0.MacDisplayUrpPatch.Pending";
        private const string Marker = "MONDMADE_VJ0_MAC_DISPLAY_BACKBUFFERS";
        private const string AttemptsKey = "MondmadeVJ0.MacDisplayUrpPatch.Attempts";
        private static EmbedRequest embedRequest;
        private static double startedAt;

        static MacDisplayUrpPatch()
        {
            if (SessionState.GetBool(PendingKey, false))
            {
                startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update += Poll;
            }
        }

        [MenuItem("Tools/Mondmade VJ0/Display Output/Apply macOS URP Fix")]
        internal static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[MondmadeVJ0] macOS URP fix: stop Play Mode before applying the fix.");
                return;
            }
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName);
            if (package == null || !package.version.StartsWith("17.3.", StringComparison.Ordinal))
            {
                Debug.LogError("[MondmadeVJ0] macOS URP fix supports URP 17.3.x only; no files were changed.");
                return;
            }
            if (package.source == PackageSource.Embedded)
            {
                ApplyToEmbedded();
                return;
            }
            SessionState.SetBool(PendingKey, true);
            SessionState.SetInt(AttemptsKey, 0);
            startedAt = EditorApplication.timeSinceStartup;
            embedRequest = Client.Embed(PackageName);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            Debug.Log("[MondmadeVJ0] Embedding URP to apply the macOS multi-display fix.");
        }

        private static void Poll()
        {
            if (embedRequest != null && embedRequest.IsCompleted && embedRequest.Status == StatusCode.Failure)
            {
                Debug.LogError("[MondmadeVJ0] Could not embed URP: " + embedRequest.Error.message);
                Finish();
                return;
            }
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName);
            if (File.Exists(SourcePath) && package != null && package.source == PackageSource.Embedded
                && !EditorApplication.isCompiling && !EditorApplication.isUpdating
                && EditorApplication.timeSinceStartup - startedAt >= 5
                && (embedRequest == null || embedRequest.IsCompleted))
            {
                if (IsApplied())
                {
                    Finish();
                    Debug.Log("[MondmadeVJ0] Embedded URP patch verified after import.");
                }
                else if (SessionState.GetInt(AttemptsKey, 0) >= 3)
                {
                    Debug.LogError("[MondmadeVJ0] Embedded URP patch did not persist; run Apply macOS URP Fix again.");
                    Finish();
                }
                else
                {
                    SessionState.SetInt(AttemptsKey, SessionState.GetInt(AttemptsKey, 0) + 1);
                    if (!ApplyToEmbedded()) Finish();
                    else startedAt = EditorApplication.timeSinceStartup;
                }
            }
            else if (EditorApplication.timeSinceStartup - startedAt > 180)
            {
                Debug.LogError("[MondmadeVJ0] Embedding URP timed out; run Apply macOS URP Fix again.");
                Finish();
            }
        }

        private static void Finish()
        {
            EditorApplication.update -= Poll;
            SessionState.SetBool(PendingKey, false);
            embedRequest = null;
        }

        private static bool ApplyToEmbedded()
        {
            try
            {
                var original = File.ReadAllText(SourcePath);
                var source = PatchSource(original);
                if (!string.Equals(original, source, StringComparison.Ordinal))
                {
                    File.WriteAllText(SourcePath, source);
                    AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceUpdate);
                }
                EnableRenderGraph();
                Debug.Log("[MondmadeVJ0] macOS multi-display URP fix applied. Render Graph is enabled; rebuild the Player.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[MondmadeVJ0] macOS URP fix failed: " + exception.Message);
                return false;
            }
        }

        internal static string PatchSource(string source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Contains(Marker))
            {
                if (!source.Contains("targetColorId = new RenderTargetIdentifier(display.colorBuffer);")
                    || !source.Contains("targetDepthId = new RenderTargetIdentifier(display.depthBuffer);")
                    || !source.Contains("displays[targetDisplay].renderingWidth")
                    || !source.Contains("displays[targetDisplay].renderingHeight"))
                    throw new InvalidOperationException("The embedded URP patch is incomplete; inspect the source before building.");
                return source;
            }
            const string targets = "            RenderTargetIdentifier targetDepthId = cameraData.targetTexture != null ? new RenderTargetIdentifier(cameraData.targetTexture) : BuiltinRenderTextureType.Depth;";
            const string replacement = targets + @"
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            // MONDMADE_VJ0_MAC_DISPLAY_BACKBUFFERS: bind each display explicitly.
            if (cameraData.targetTexture == null && cameraData.camera.targetDisplay >= 0 && cameraData.camera.targetDisplay < Display.displays.Length)
            {
                var display = Display.displays[cameraData.camera.targetDisplay];
                targetColorId = new RenderTargetIdentifier(display.colorBuffer);
                targetDepthId = new RenderTargetIdentifier(display.depthBuffer);
            }
#endif";
            const string dimensions = "                importInfo.width = Screen.width;\n                importInfo.height = Screen.height;";
            const string dimensionsReplacement = @"#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
                var targetDisplay = cameraData.camera.targetDisplay;
                var displays = Display.displays;
                importInfo.width = targetDisplay >= 0 && targetDisplay < displays.Length ? displays[targetDisplay].renderingWidth : Screen.width;
                importInfo.height = targetDisplay >= 0 && targetDisplay < displays.Length ? displays[targetDisplay].renderingHeight : Screen.height;
#else
                importInfo.width = Screen.width;
                importInfo.height = Screen.height;
#endif";
            source = source.Replace("\r\n", "\n");
            if (Count(source, targets) != 1 || Count(source, dimensions) != 1)
                throw new InvalidOperationException("URP source did not match the expected 17.3 layout; no source changes were written.");
            return source.Replace(targets, replacement).Replace(dimensions, dimensionsReplacement);
        }

        private static void EnableRenderGraph()
        {
            var pipelineType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipeline, Unity.RenderPipelines.Universal.Runtime");
            var settings = pipelineType != null ? GraphicsSettings.GetSettingsForRenderPipeline(pipelineType) : null;
            if (settings == null) throw new InvalidOperationException("URP Global Settings not found.");
            var serialized = new SerializedObject(settings);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.name != "m_EnableRenderCompatibilityMode") continue;
                property.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(settings);
                return;
            }
            throw new InvalidOperationException("Render Graph setting not found.");
        }

        internal static bool IsApplied()
        {
            if (!File.Exists(SourcePath)) return false;
            var source = File.ReadAllText(SourcePath);
            if (!source.Contains(Marker)
                || !source.Contains("targetColorId = new RenderTargetIdentifier(display.colorBuffer);")
                || !source.Contains("targetDepthId = new RenderTargetIdentifier(display.depthBuffer);")
                || !source.Contains("displays[targetDisplay].renderingWidth")
                || !source.Contains("displays[targetDisplay].renderingHeight")) return false;
            var pipelineType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipeline, Unity.RenderPipelines.Universal.Runtime");
            var settings = pipelineType != null ? GraphicsSettings.GetSettingsForRenderPipeline(pipelineType) : null;
            if (settings == null) return false;
            var iterator = new SerializedObject(settings).GetIterator();
            while (iterator.Next(true))
                if (iterator.name == "m_EnableRenderCompatibilityMode") return !iterator.boolValue;
            return false;
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            for (var i = 0; (i = source.IndexOf(value, i, StringComparison.Ordinal)) >= 0; i += value.Length) count++;
            return count;
        }
    }
    internal sealed class MacDisplayUrpBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX) return;
            const string modulePath = "Packages/com.redmond.mondmade-vj0/Runtime/Display/DisplayOutputModule.cs";
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path)) continue;
                var dependencies = AssetDatabase.GetDependencies(scene.path, true);
                if (Array.IndexOf(dependencies, modulePath) < 0) continue;
                if (!MacDisplayUrpPatch.IsApplied())
                    throw new BuildFailedException("MondmadeVJ0: macOS multi-display requires the URP fix. Run Tools > Mondmade VJ0 > Display Output > Apply macOS URP Fix and wait for compilation before building.");
                return;
            }
        }
    }

}
