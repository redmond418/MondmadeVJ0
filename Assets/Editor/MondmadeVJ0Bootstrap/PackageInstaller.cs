using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using NugetForUnity;
using NugetForUnity.Models;

namespace MondmadeVJ0Bootstrap
{
    public static class PackageInstaller
    {
        private static readonly string[] DevelopmentDependencies =
        {
            "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#dc69078430149db8cd93069f502cefe148745b4b",
            "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#c2af83c9d4f8cdaada9d4a0e94de2f195d8e1d01",
            "https://github.com/keijiro/Minis.git?path=/Packages/jp.keijiro.minis#efd9e01d563804bfd35961beecf1fc78e89e0873",
            "https://github.com/keijiro/jp.keijiro.rtmidi.git?path=/Packages/jp.keijiro.rtmidi#7a67cbd8e741a7f9ef879912f5f1eef4671c1b09",
            "https://github.com/keijiro/Lasp.git?path=/Packages/jp.keijiro.lasp#b57e37fbb07f4492dd9b71defd29c9250e131280",
            "https://github.com/keijiro/jp.keijiro.libsoundio.git?path=/Packages/jp.keijiro.libsoundio#661056c56634384d81cb52a9b03fd34adb95c34d",
            "https://github.com/redmond418/OscJack.git?path=/Packages/jp.keijiro.osc-jack#7ef8fa692030079d2ad6d1a314680b96fbd9a328",
            "com.unity.cinemachine@3.1.6",
            "com.unity.visualeffectgraph@17.3.0",
        };

        private static AddAndRemoveRequest request;
        private static double deadline;

        [MenuItem("Tools/Mondmade VJ0/Install Development Dependencies")]
        public static void InstallDependencies()
        {
            Debug.Log("[MondmadeVJ0Bootstrap] Resolving the package and verified development integrations.");
            Begin(DevelopmentDependencies);
        }

        [MenuItem("Tools/Mondmade VJ0/Install Local Package")]
        public static void InstallPackage()
        {
            Begin(new[] { "file:Packages/com.redmond.mondmade-vj0" });
        }

        public static void InstallR3Nuget()
        {
            var installed = NugetPackageInstaller.InstallIdentifier(
                new NugetPackageIdentifier("R3", "1.3.0"));
            Debug.Log(installed
                ? "[MondmadeVJ0Bootstrap] Installed R3 NuGet 1.3.0."
                : "[MondmadeVJ0Bootstrap] Failed to install R3 NuGet 1.3.0.");
            EditorApplication.Exit(installed ? 0 : 1);
        }

        private static void Begin(string[] packages)
        {
            request = Client.AddAndRemove(packages, new string[0]);
            deadline = EditorApplication.timeSinceStartup + 900;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (request == null) return;
            if (!request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup <= deadline) return;
                EditorApplication.update -= Poll;
                Debug.LogError("[MondmadeVJ0Bootstrap] Timed out while resolving UPM packages.");
                EditorApplication.Exit(2);
                return;
            }

            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Success)
            {
                foreach (var package in request.Result.OrderBy(p => p.name))
                    Debug.Log($"[MondmadeVJ0Bootstrap] Resolved {package.name}@{package.version}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[MondmadeVJ0Bootstrap] {request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
