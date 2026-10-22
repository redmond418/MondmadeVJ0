using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace MondmadeVJ0
{
    public enum VolumeOverrideMode { Always, FollowExisting, Automatically }
    public enum MaterialOutputMode { Instance, SharedAsset }

    internal static class RenderingOutputServices
    {
        public static RenderingResourceService GetResourceService(RouteContext context)
        {
            if (context.Services.TryGet<RenderingResourceService>(out var service)) return service;
            service = new RenderingResourceService();
            context.Services.Register(service);
            return service;
        }
    }

    internal static class RenderOutputHelpers
    {
        public static int ValidateProperty(Material material, string propertyName, ShaderPropertyType expected, string label)
        {
            if (material == null) throw new InvalidOperationException($"{label} Material is not assigned.");
            if (string.IsNullOrWhiteSpace(propertyName)) throw new InvalidOperationException($"{label} property name is empty.");
            var shader = material.shader;
            var index = shader == null ? -1 : shader.FindPropertyIndex(propertyName);
            if (index < 0) throw new InvalidOperationException($"{label} property '{propertyName}' does not exist on shader '{shader?.name}'.");
            var actual = shader.GetPropertyType(index);
            var matches = expected == ShaderPropertyType.Vector && actual == ShaderPropertyType.Color
                       || expected == ShaderPropertyType.Color && actual == ShaderPropertyType.Vector
                       || expected == ShaderPropertyType.Float && actual == ShaderPropertyType.Range
                       || actual == expected;
            if (!matches) throw new InvalidOperationException($"{label} property '{propertyName}' is {actual}, expected {expected}.");
            return Shader.PropertyToID(propertyName);
        }

        public static int ValidateRenderer(Renderer renderer, int materialIndex, string propertyName, ShaderPropertyType expected)
        {
            if (renderer == null) throw new InvalidOperationException("Renderer target is not assigned.");
            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length) throw new InvalidOperationException($"Renderer material index {materialIndex} is out of range.");
            return ValidateProperty(materials[materialIndex], propertyName, expected, "Renderer");
        }
    }

    [Serializable]
    public abstract class RendererPropertyOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] protected Renderer target;
        [SerializeField] protected string propertyName;
        [SerializeField] protected int materialIndex;
        [NonSerialized] protected int propertyId;
        [NonSerialized] protected MaterialPropertyBlock propertyBlock;
        protected abstract ShaderPropertyType PropertyType { get; }
        protected abstract void Set(MaterialPropertyBlock block, int id, T value);
        public void Initialize(RouteContext context)
        {
            propertyId = RenderOutputHelpers.ValidateRenderer(target, materialIndex, propertyName, PropertyType);
            propertyBlock = new MaterialPropertyBlock();
        }
        public void Write(T value)
        {
            target.GetPropertyBlock(propertyBlock, materialIndex);
            Set(propertyBlock, propertyId, value);
            target.SetPropertyBlock(propertyBlock, materialIndex);
        }
    }

    [Serializable] public sealed class RendererFloatOutput : RendererPropertyOutput<float> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Float; protected override void Set(MaterialPropertyBlock block,int id,float value)=>block.SetFloat(id,value); }
    [Serializable] public sealed class RendererIntOutput : RendererPropertyOutput<int> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Int; protected override void Set(MaterialPropertyBlock block,int id,int value)=>block.SetInt(id,value); }
    [Serializable] public sealed class RendererVectorOutput : RendererPropertyOutput<Vector4> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Vector; protected override void Set(MaterialPropertyBlock block,int id,Vector4 value)=>block.SetVector(id,value); }
    [Serializable] public sealed class RendererColorOutput : RendererPropertyOutput<Color> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Color; protected override void Set(MaterialPropertyBlock block,int id,Color value)=>block.SetColor(id,value); }
    [Serializable] public sealed class RendererTextureOutput : RendererPropertyOutput<Texture> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Texture; protected override void Set(MaterialPropertyBlock block,int id,Texture value)=>block.SetTexture(id,value); }

    [Serializable]
    public sealed class RendererTextureIndexOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private Renderer target;
        [SerializeField] private string propertyName;
        [SerializeField] private int materialIndex;
        [SerializeField] private Texture[] textures = Array.Empty<Texture>();
        [NonSerialized] private int propertyId;
        [NonSerialized] private MaterialPropertyBlock propertyBlock;
        public void Initialize(RouteContext context)
        {
            propertyId = RenderOutputHelpers.ValidateRenderer(target, materialIndex, propertyName, ShaderPropertyType.Texture);
            if (textures == null || textures.Length == 0) throw new InvalidOperationException("Renderer texture list is empty.");
            for (var i = 0; i < textures.Length; i++) if (textures[i] == null) throw new InvalidOperationException($"Renderer texture {i} is not assigned.");
            propertyBlock = new MaterialPropertyBlock();
        }
        public void Write(int value)
        {
            target.GetPropertyBlock(propertyBlock, materialIndex);
            propertyBlock.SetTexture(propertyId, textures[Mathf.Clamp(value, 0, textures.Length - 1)]);
            target.SetPropertyBlock(propertyBlock, materialIndex);
        }
    }

    [Serializable]
    public abstract class MaterialPropertyOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] private MaterialOutputMode outputMode = MaterialOutputMode.Instance;
        [SerializeField] protected Renderer renderer;
        [SerializeField] protected int materialIndex;
        // Kept under the original serialized field name for existing Shared Asset references.
        [SerializeField] protected Material target;
        [SerializeField] protected string propertyName;
        [NonSerialized] protected int propertyId;
        [NonSerialized] protected Material outputMaterial;
        protected abstract ShaderPropertyType PropertyType { get; }
        protected abstract void Set(int id, T value);
        public void Initialize(RouteContext context)
        {
            RenderingResourceService.ResourceLease lease = null;
            var material = target;
            if (outputMode == MaterialOutputMode.Instance)
            {
                RenderOutputHelpers.ValidateRenderer(renderer, materialIndex, propertyName, PropertyType);
                lease = RenderingOutputServices.GetResourceService(context).AcquireMaterial(renderer, materialIndex);
                material = lease.Material;
            }
            try
            {
                propertyId = RenderOutputHelpers.ValidateProperty(material, propertyName, PropertyType, "Material");
                outputMaterial = material;
                context.Scope.Add(() => outputMaterial = null);
                if (lease != null) context.Scope.Add(lease);
            }
            catch
            {
                lease?.Dispose();
                outputMaterial = null;
                throw;
            }
        }

        public void Write(T value) => Set(propertyId, value);
    }

    [Serializable] public sealed class MaterialFloatOutput : MaterialPropertyOutput<float> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Float; protected override void Set(int id,float value)=>outputMaterial.SetFloat(id,value); }
    [Serializable] public sealed class MaterialIntOutput : MaterialPropertyOutput<int> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Int; protected override void Set(int id,int value)=>outputMaterial.SetInt(id,value); }
    [Serializable] public sealed class MaterialVectorOutput : MaterialPropertyOutput<Vector4> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Vector; protected override void Set(int id,Vector4 value)=>outputMaterial.SetVector(id,value); }
    [Serializable] public sealed class MaterialColorOutput : MaterialPropertyOutput<Color> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Color; protected override void Set(int id,Color value)=>outputMaterial.SetColor(id,value); }
    [Serializable] public sealed class MaterialTextureOutput : MaterialPropertyOutput<Texture> { protected override ShaderPropertyType PropertyType => ShaderPropertyType.Texture; protected override void Set(int id,Texture value)=>outputMaterial.SetTexture(id,value); }

    public abstract class VolumeParameterOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] private Volume target;
        [SerializeField] private string componentTypeName;
        [SerializeField] private string parameterName;
        [SerializeField] private VolumeOverrideMode overrideMode = VolumeOverrideMode.Always;
        [NonSerialized] private VolumeParameter parameter;
        [NonSerialized] private Action<T> setValue;
        [NonSerialized] private T initialValue;

        public void Initialize(RouteContext context)
        {
            if (target == null) throw new InvalidOperationException("Volume target is not assigned.");
            var type = ResolveComponentType(componentTypeName);
            var lease = RenderingOutputServices.GetResourceService(context).AcquireVolumeProfile(target);
            try
            {
                var profile = lease.Profile;
                var component = profile.components.FirstOrDefault(candidate => candidate != null && candidate.GetType() == type);
                if (component == null && overrideMode == VolumeOverrideMode.Automatically)
                    component = profile.Add(type, overrides: true);
                if (component == null) throw new InvalidOperationException($"Volume profile does not contain {type.Name}.");
                if (string.IsNullOrWhiteSpace(parameterName)) throw new InvalidOperationException("Volume parameter name is empty.");
                var field = type.GetField(parameterName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || !typeof(VolumeParameter<T>).IsAssignableFrom(field.FieldType))
                    throw new InvalidOperationException($"Volume parameter '{type.Name}.{parameterName}' is not a {typeof(T).Name} parameter.");
                parameter = field.GetValue(component) as VolumeParameter;
                var typed = field.GetValue(component) as VolumeParameter<T>;
                if (parameter == null || typed == null) throw new InvalidOperationException($"Volume parameter '{type.Name}.{parameterName}' is null.");
                var setter = typeof(VolumeParameter<T>).GetProperty(nameof(VolumeParameter<T>.value))?.GetSetMethod();
                if (setter == null) throw new InvalidOperationException($"Volume parameter '{type.Name}.{parameterName}' has no setter.");
                setValue = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), typed, setter);
                initialValue = typed.value;
                if (overrideMode == VolumeOverrideMode.Always) parameter.overrideState = true;
                if (!component.active) context.Report("VOLUME_COMPONENT_INACTIVE", $"Volume component {type.Name} is inactive; its parameter is writable but may not affect the rendered result.", this);
                context.Scope.Add(ResetRuntimeState);
                context.Scope.Add(lease);
            }
            catch
            {
                lease.Dispose();
                ResetRuntimeState();
                throw;
            }
        }

        public void Write(T value)
        {
            switch (overrideMode)
            {
                case VolumeOverrideMode.Always:
                    parameter.overrideState = true;
                    break;
                case VolumeOverrideMode.Automatically:
                    parameter.overrideState = !EqualityComparer<T>.Default.Equals(value, initialValue);
                    break;
            }
            setValue(value);
        }

        private void ResetRuntimeState()
        {
            parameter = null;
            setValue = null;
            initialValue = default;
        }

        private static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) throw new InvalidOperationException("Volume component type is empty.");
            var type = Type.GetType(typeName, throwOnError: false);
            if (type == null)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((type = assembly.GetType(typeName, throwOnError: false)) != null) break;
            if (type == null || !typeof(VolumeComponent).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException($"'{typeName}' is not a concrete VolumeComponent type.");
            return type;
        }
    }

    [Serializable] public sealed class VolumeFloatOutput : VolumeParameterOutput<float> { }
    [Serializable] public sealed class VolumeIntOutput : VolumeParameterOutput<int> { }
    [Serializable] public sealed class VolumeBoolOutput : VolumeParameterOutput<bool> { }
    [Serializable] public sealed class VolumeColorOutput : VolumeParameterOutput<Color> { }
    [Serializable] public sealed class VolumeVector2Output : VolumeParameterOutput<Vector2> { }
    [Serializable] public sealed class VolumeVectorOutput : VolumeParameterOutput<Vector3> { }
    [Serializable] public sealed class VolumeVector4Output : VolumeParameterOutput<Vector4> { }
}
