using System;
using System.Collections.Generic;
using System.Linq;
using NugetForUnity;
using NugetForUnity.Models;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace MondmadeVJ0.Editor.Setup
{
    public sealed class SetupWindow : EditorWindow
    {
        private const string R3Version = "1.3.0";
        private const string R3UnsafeVersion = "6.0.0";
        private const string R3UnsafePackageId = "System.Runtime.CompilerServices.Unsafe";
        private const string InputSystemId = "com.unity.inputsystem@1.18.0";
        private const string UrpId = "com.unity.render-pipelines.universal@17.3.0";
        private const string MinisUrl = "https://github.com/keijiro/Minis.git?path=/Packages/jp.keijiro.minis#efd9e01d563804bfd35961beecf1fc78e89e0873";
        private const string RtMidiUrl = "https://github.com/keijiro/jp.keijiro.rtmidi.git?path=/Packages/jp.keijiro.rtmidi#7a67cbd8e741a7f9ef879912f5f1eef4671c1b09";
        private const string OscUrl = "https://github.com/redmond418/OscJack.git?path=/Packages/jp.keijiro.osc-jack#7ef8fa692030079d2ad6d1a314680b96fbd9a328";
        private const string LaspUrl = "https://github.com/keijiro/Lasp.git?path=/Packages/jp.keijiro.lasp#b57e37fbb07f4492dd9b71defd29c9250e131280";
        private const string LibsoundioUrl = "https://github.com/keijiro/jp.keijiro.libsoundio.git?path=/Packages/jp.keijiro.libsoundio#661056c56634384d81cb52a9b03fd34adb95c34d";
        private const string CinemachineId = "com.unity.cinemachine@3.1.6";
        private const string VfxGraphId = "com.unity.visualeffectgraph@17.3.0";
        private const string UguiId = "com.unity.ugui@2.0.0";
        private static readonly string[] MidiPackages = { MinisUrl, RtMidiUrl };
        private static readonly string[] InputSystemPackages = { InputSystemId };
        private static readonly string[] UrpPackages = { UrpId };
        private static readonly string[] OscPackages = { OscUrl };
        private static readonly string[] LaspPackages = { LaspUrl, LibsoundioUrl };
        private static readonly string[] CinemachinePackages = { CinemachineId };
        private static readonly string[] VfxPackages = { VfxGraphId };
        private static readonly string[] DisplayPackages = { UguiId };
        private static AddAndRemoveRequest request;
        private static string operation;

        [MenuItem("Tools/Mondmade VJ0/Setup")]
        public static void Open()
        {
            var window = GetWindow<SetupWindow>();
            window.titleContent = new GUIContent("Mondmade VJ0 Setup");
            window.minSize = new Vector2(440, 480);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Mondmade VJ0", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("R3本体はNuGet、外部IntegrationはUPMで管理します。CoreにはUnity標準Audio module、Animator / Videoには対応する標準moduleが必要です。", MessageType.Info);

            DrawR3Status();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Optional integrations", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("必要な外部Integrationだけ選択してください。Input System、URP、uGUI / TextMeshProも任意です。", MessageType.None);

            var inputSystem = EditorPrefs.GetBool("MondmadeVJ0.Setup.InputSystem", false);
            var urp = EditorPrefs.GetBool("MondmadeVJ0.Setup.Urp", false);
            var midi = EditorPrefs.GetBool("MondmadeVJ0.Setup.Midi", false);
            var osc = EditorPrefs.GetBool("MondmadeVJ0.Setup.Osc", false);
            var lasp = EditorPrefs.GetBool("MondmadeVJ0.Setup.Lasp", false);
            var cinemachine = EditorPrefs.GetBool("MondmadeVJ0.Setup.Cinemachine", false);
            var vfx = EditorPrefs.GetBool("MondmadeVJ0.Setup.Vfx", false);
            var display = EditorPrefs.GetBool("MondmadeVJ0.Setup.Display", false);
            inputSystem = EditorGUILayout.ToggleLeft("Input System 1.18.0", inputSystem);
            urp = EditorGUILayout.ToggleLeft("Universal Render Pipeline 17.3.0", urp);
            midi = EditorGUILayout.ToggleLeft("MIDI (Minis 1.3.2 + RtMidi 2.2.0)", midi);
            osc = EditorGUILayout.ToggleLeft("OSC (OSC Jack fork)", osc);
            lasp = EditorGUILayout.ToggleLeft("Audio analysis (Lasp 2.1.8)", lasp);
            cinemachine = EditorGUILayout.ToggleLeft("Cinemachine 3.1.6", cinemachine);
            vfx = EditorGUILayout.ToggleLeft("VFX Graph 17.3.0", vfx);
            display = EditorGUILayout.ToggleLeft("uGUI 2.0.0 + TextMeshPro (Display output)", display);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.InputSystem", inputSystem);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Urp", urp);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Midi", midi);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Osc", osc);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Lasp", lasp);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Cinemachine", cinemachine);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Vfx", vfx);
            EditorPrefs.SetBool("MondmadeVJ0.Setup.Display", display);

            using (new EditorGUI.DisabledScope(request != null))
            {
                if (GUILayout.Button("Install selected integrations"))
                    InstallSelected(inputSystem, urp, midi, osc, lasp, cinemachine, vfx, display);
            }

            if (!string.IsNullOrEmpty(operation))
                EditorGUILayout.HelpBox(operation, request == null ? MessageType.Info : MessageType.None);
        }

        private void DrawR3Status()
        {
            var r3Version = GetInstalledNugetVersion("R3");
            var unsafeVersion = GetInstalledNugetVersion(R3UnsafePackageId);
            if (r3Version == R3Version && unsafeVersion == R3UnsafeVersion)
            {
                EditorGUILayout.HelpBox($"Required R3 NuGet {R3Version} and {R3UnsafePackageId} {R3UnsafeVersion} are installed.", MessageType.Info);
                return;
            }

            var missing = new List<string>();
            if (r3Version != R3Version)
                missing.Add(string.IsNullOrEmpty(r3Version) ? $"R3 {R3Version}" : $"R3 {r3Version} (requires {R3Version})");
            if (unsafeVersion != R3UnsafeVersion)
                missing.Add(string.IsNullOrEmpty(unsafeVersion)
                    ? $"{R3UnsafePackageId} {R3UnsafeVersion}"
                    : $"{R3UnsafePackageId} {unsafeVersion} (requires {R3UnsafeVersion})");
            EditorGUILayout.HelpBox("Required NuGet packages are missing or have different versions: " + string.Join(", ", missing) + ".", MessageType.Warning);
            using (new EditorGUI.DisabledScope(request != null))
            {
                if (GUILayout.Button("Install required R3 NuGet and dependencies"))
                    InstallR3();
            }
        }

        private static string GetInstalledNugetVersion(string packageId)
        {
            try
            {
                var package = InstalledPackagesManager.InstalledPackages
                    .FirstOrDefault(item => string.Equals(item.Id, packageId, StringComparison.OrdinalIgnoreCase));
                return package?.Version ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void InstallR3()
        {
            operation = $"Installing R3 NuGet {R3Version} and its required packages…";
            try
            {
                InstallRequiredNuget("R3", R3Version);
                InstallRequiredNuget(R3UnsafePackageId, R3UnsafeVersion);
                operation = $"Installed R3 NuGet {R3Version} and {R3UnsafePackageId} {R3UnsafeVersion}. Unity will recompile the package assemblies.";
                Debug.Log($"[MondmadeVJ0] {operation}");
            }
            catch (Exception exception)
            {
                operation = $"R3 install failed: {exception.Message}";
                Debug.LogException(exception);
            }
            Resources.FindObjectsOfTypeAll<SetupWindow>().FirstOrDefault()?.Repaint();
        }

        private static void InstallRequiredNuget(string packageId, string version)
        {
            if (GetInstalledNugetVersion(packageId) == version) return;
            if (!NugetPackageInstaller.InstallIdentifier(new NugetPackageIdentifier(packageId, version)))
                throw new InvalidOperationException($"NuGetForUnity could not install {packageId} {version}.");
        }

        private static void InstallSelected(bool inputSystem, bool urp, bool midi, bool osc, bool lasp, bool cinemachine, bool vfx, bool display)
        {
            var packages = new List<string>();
            if (inputSystem) packages.AddRange(InputSystemPackages);
            if (urp) packages.AddRange(UrpPackages);
            if (midi) packages.AddRange(MidiPackages);
            if (osc) packages.AddRange(OscPackages);
            if (lasp) packages.AddRange(LaspPackages);
            if (cinemachine) packages.AddRange(CinemachinePackages);
            if (vfx) packages.AddRange(VfxPackages);
            if (display) packages.AddRange(DisplayPackages);
            if (packages.Count == 0)
            {
                operation = "No optional integrations selected.";
                return;
            }

            operation = "Resolving selected UPM packages…";
            request = Client.AddAndRemove(packages.Distinct().ToArray(), Array.Empty<string>());
            EditorApplication.update += PollRequest;
        }

        private static void PollRequest()
        {
            if (request == null || !request.IsCompleted)
                return;

            EditorApplication.update -= PollRequest;
            if (request.Status == StatusCode.Success)
            {
                operation = "Installed: " + string.Join(", ", request.Result.Select(package => $"{package.name}@{package.version}"));
                Debug.Log("[MondmadeVJ0] " + operation);
            }
            else
            {
                operation = "UPM install failed: " + request.Error?.message;
                Debug.LogError("[MondmadeVJ0] " + operation);
            }
            request = null;
            GetWindow<SetupWindow>().Repaint();
        }
    }
}
