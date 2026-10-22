using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MondmadeVJ0
{
    internal sealed class RenderingResourceService
    {
        private readonly Dictionary<RendererSlotKey, MaterialEntry> materials = new Dictionary<RendererSlotKey, MaterialEntry>();
        private readonly Dictionary<Volume, VolumeProfileEntry> profiles = new Dictionary<Volume, VolumeProfileEntry>(ReferenceComparer<Volume>.Instance);

        public ResourceLease AcquireMaterial(Renderer renderer, int materialIndex)
        {
            if (renderer == null) throw new InvalidOperationException("Material Instance mode requires a Renderer.");
            var key = new RendererSlotKey(renderer, materialIndex);
            if (materials.TryGetValue(key, out var existing))
            {
                EnsureStillAssigned(renderer, materialIndex, existing.Instance);
                existing.ReferenceCount++;
                return new ResourceLease(existing.Instance, null, () => ReleaseMaterial(key, existing));
            }

            var slots = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= slots.Length)
                throw new InvalidOperationException($"Renderer material index {materialIndex} is out of range.");
            var original = slots[materialIndex];
            if (original == null) throw new InvalidOperationException($"Renderer material slot {materialIndex} is empty.");

            var instance = new Material(original)
            {
                name = original.name + " (Mondmade VJ0 Runtime)",
                hideFlags = HideFlags.DontSave
            };
            try
            {
                slots[materialIndex] = instance;
                renderer.sharedMaterials = slots;
                var entry = new MaterialEntry(original, instance);
                materials.Add(key, entry);
                return new ResourceLease(instance, null, () => ReleaseMaterial(key, entry));
            }
            catch
            {
                if (renderer != null)
                {
                    var assignedSlots = renderer.sharedMaterials;
                    if (materialIndex >= 0 && materialIndex < assignedSlots.Length && assignedSlots[materialIndex] == instance)
                    {
                        assignedSlots[materialIndex] = original;
                        renderer.sharedMaterials = assignedSlots;
                    }
                }
                DestroyOwned(instance);
                throw;
            }
        }

        public ResourceLease AcquireVolumeProfile(Volume volume)
        {
            if (volume == null) throw new InvalidOperationException("Volume target is not assigned.");
            if (profiles.TryGetValue(volume, out var existing))
            {
                if (volume.profile != existing.Instance)
                    throw new InvalidOperationException("Volume profile changed while a Runtime clone is leased.");
                existing.ReferenceCount++;
                return new ResourceLease(null, existing.Instance, () => ReleaseProfile(volume, existing));
            }

            var hadInternalProfile = volume.HasInstantiatedProfile();
            var original = hadInternalProfile ? volume.profile : volume.sharedProfile;
            if (original == null) throw new InvalidOperationException("Volume has no profile.");
            var instance = UnityEngine.Object.Instantiate(original);
            instance.name = original.name + " (Mondmade VJ0 Runtime)";
            instance.hideFlags = HideFlags.DontSave;
            try
            {
                volume.profile = instance;
                var entry = new VolumeProfileEntry(original, instance, hadInternalProfile);
                profiles.Add(volume, entry);
                return new ResourceLease(null, instance, () => ReleaseProfile(volume, entry));
            }
            catch
            {
                if (volume != null && volume.profile == instance)
                    volume.profile = hadInternalProfile ? original : null;
                DestroyOwned(instance);
                throw;
            }
        }

        private void ReleaseMaterial(RendererSlotKey key, MaterialEntry entry)
        {
            if (--entry.ReferenceCount > 0) return;
            materials.Remove(key);
            var renderer = key.Renderer;
            if (renderer != null)
            {
                var slots = renderer.sharedMaterials;
                if (key.MaterialIndex >= 0 && key.MaterialIndex < slots.Length && slots[key.MaterialIndex] == entry.Instance)
                {
                    slots[key.MaterialIndex] = entry.Original;
                    renderer.sharedMaterials = slots;
                }
            }
            DestroyOwned(entry.Instance);
        }

        private void ReleaseProfile(Volume volume, VolumeProfileEntry entry)
        {
            if (--entry.ReferenceCount > 0) return;
            profiles.Remove(volume);
            if (volume != null && volume.profile == entry.Instance)
                volume.profile = entry.HadInternalProfile ? entry.Original : null;
            DestroyOwned(entry.Instance);
        }

        private static void EnsureStillAssigned(Renderer renderer, int materialIndex, Material expected)
        {
            var slots = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= slots.Length || slots[materialIndex] != expected)
                throw new InvalidOperationException($"Renderer material slot {materialIndex} changed while a Runtime clone is leased.");
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        internal sealed class ResourceLease : IDisposable
        {
            private Action release;
            public Material Material { get; }
            public VolumeProfile Profile { get; }

            internal ResourceLease(Material material, VolumeProfile profile, Action release)
            {
                Material = material;
                Profile = profile;
                this.release = release;
            }

            public void Dispose()
            {
                var action = release;
                release = null;
                action?.Invoke();
            }
        }

        private sealed class MaterialEntry
        {
            public readonly Material Original;
            public readonly Material Instance;
            public int ReferenceCount = 1;
            public MaterialEntry(Material original, Material instance) { Original = original; Instance = instance; }
        }

        private sealed class VolumeProfileEntry
        {
            public readonly VolumeProfile Original;
            public readonly VolumeProfile Instance;
            public readonly bool HadInternalProfile;
            public int ReferenceCount = 1;
            public VolumeProfileEntry(VolumeProfile original, VolumeProfile instance, bool hadInternalProfile)
            {
                Original = original;
                Instance = instance;
                HadInternalProfile = hadInternalProfile;
            }
        }

        private readonly struct RendererSlotKey : IEquatable<RendererSlotKey>
        {
            public readonly Renderer Renderer;
            public readonly int MaterialIndex;
            public RendererSlotKey(Renderer renderer, int materialIndex) { Renderer = renderer; MaterialIndex = materialIndex; }
            public bool Equals(RendererSlotKey other) => ReferenceEquals(Renderer, other.Renderer) && MaterialIndex == other.MaterialIndex;
            public override bool Equals(object obj) => obj is RendererSlotKey other && Equals(other);
            public override int GetHashCode() => unchecked(RuntimeHelpers.GetHashCode(Renderer) * 397 ^ MaterialIndex);
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
