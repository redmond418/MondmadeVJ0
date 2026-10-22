using System;
using UnityEngine;
using UnityEngine.VFX;

namespace MondmadeVJ0
{
    internal static class VFXOutputHelpers
    {
        public static int Resolve(VisualEffect effect, string property, Func<VisualEffect, int, bool> has, string type)
        {
            if (effect == null) throw new InvalidOperationException("VisualEffect target is not assigned.");
            if (string.IsNullOrWhiteSpace(property)) throw new InvalidOperationException("VFX exposed property name is empty.");
            var id = Shader.PropertyToID(property);
            if (!has(effect, id)) throw new InvalidOperationException($"VFX property '{property}' is missing or is not {type}.");
            return id;
        }
    }

    [Serializable]
    public sealed class VFXFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasFloat(id), "Float");
        public void Write(float value) => target.SetFloat(propertyId, value);
    }

    [Serializable]
    public sealed class VFXIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasInt(id), "Int");
        public void Write(int value) => target.SetInt(propertyId, value);
    }

    [Serializable]
    public sealed class VFXUIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasUInt(id), "UInt");
        public void Write(int value) => target.SetUInt(propertyId, (uint)Math.Max(0, value));
    }

    [Serializable]
    public sealed class VFXBoolOutput : RouteNode, IValueOutput<bool>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasBool(id), "Bool");
        public void Write(bool value) => target.SetBool(propertyId, value);
    }

    [Serializable]
    public sealed class VFXVector2Output : RouteNode, IValueOutput<Vector2>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasVector2(id), "Vector2");
        public void Write(Vector2 value) => target.SetVector2(propertyId, value);
    }

    [Serializable]
    public sealed class VFXVector3Output : RouteNode, IValueOutput<Vector3>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasVector3(id), "Vector3");
        public void Write(Vector3 value) => target.SetVector3(propertyId, value);
    }

    [Serializable]
    public sealed class VFXVector4Output : RouteNode, IValueOutput<Vector4>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasVector4(id), "Vector4");
        public void Write(Vector4 value) => target.SetVector4(propertyId, value);
    }

    [Serializable]
    public sealed class VFXColorOutput : RouteNode, IValueOutput<Color>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasVector4(id), "Color/Vector4");
        public void Write(Color value) => target.SetVector4(propertyId, value);
    }

    public enum VFXMatrixElement { Translation, EulerRotation, Scale }

    [Serializable]
    public sealed class VFXMatrixOutput : RouteNode, IValueOutput<Vector3>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [SerializeField] private VFXMatrixElement element;
        [SerializeField] private Vector3 position;
        [SerializeField] private Vector3 eulerRotation;
        [SerializeField] private Vector3 scale = Vector3.one;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasMatrix4x4(id), "Matrix4x4");
        public void Write(Vector3 value)
        {
            switch (element)
            {
                case VFXMatrixElement.Translation: position = value; break;
                case VFXMatrixElement.EulerRotation: eulerRotation = value; break;
                case VFXMatrixElement.Scale: scale = value; break;
            }
            target.SetMatrix4x4(propertyId, Matrix4x4.TRS(position, Quaternion.Euler(eulerRotation), scale));
        }
    }

    [Serializable]
    public sealed class VFXTextureOutput : RouteNode, IValueOutput<Texture>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context) => propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasTexture(id), "Texture");
        public void Write(Texture value) => target.SetTexture(propertyId, value);
    }

    [Serializable]
    public sealed class VFXTextureIndexOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string propertyName;
        [SerializeField] private Texture[] textures = Array.Empty<Texture>();
        [NonSerialized] private int propertyId;
        public void Initialize(RouteContext context)
        {
            propertyId = VFXOutputHelpers.Resolve(target, propertyName, static (effect, id) => effect.HasTexture(id), "Texture");
            if (textures == null || textures.Length == 0) throw new InvalidOperationException("VFX texture list is empty.");
            for (var i = 0; i < textures.Length; i++) if (textures[i] == null) throw new InvalidOperationException($"VFX texture {i} is not assigned.");
        }
        public void Write(int value) => target.SetTexture(propertyId, textures[Mathf.Clamp(value, 0, textures.Length - 1)]);
    }

    [Serializable]
    public sealed class VFXTriggerEventOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private VisualEffect target;
        [SerializeField] private string eventName = "OnPlay";
        [NonSerialized] private int eventId;
        public void Initialize(RouteContext context)
        {
            if (target == null) throw new InvalidOperationException("VisualEffect target is not assigned.");
            if (string.IsNullOrWhiteSpace(eventName)) throw new InvalidOperationException("VFX event name is empty.");
            eventId = Shader.PropertyToID(eventName);
        }
        public void Invoke() => target.SendEvent(eventId);
    }
}
