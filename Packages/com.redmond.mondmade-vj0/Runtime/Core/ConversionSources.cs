using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    public enum FloatIntRounding { ToEven, Floor, Ceiling, Truncate }
    public enum VectorComponent { X, Y, Z, W }

    [Serializable]
    public abstract class MapValueSource<TIn, TOut> : RouteNode, IValueSource<TOut>
    {
        [SerializeReference] private IValueSource<TIn> input;
        public IValueSource<TIn> Input { get => input; set => input = value; }
        protected abstract TOut Convert(TIn value);
        protected virtual void Validate() { }
        public Observable<TOut> Build(RouteContext context)
        {
            if (input == null) throw new InvalidOperationException($"{GetType().Name} input is missing.");
            Validate();
            return input.Build(context).Select(Convert);
        }
    }

    [Serializable] public sealed class IntToFloatSource : MapValueSource<int, float> { protected override float Convert(int value) => value; }

    [Serializable]
    public sealed class BoolToFloatSource : MapValueSource<bool, float>
    {
        [SerializeField] private float falseValue;
        [SerializeField] private float trueValue = 1f;
        protected override float Convert(bool value) => value ? trueValue : falseValue;
    }

    [Serializable]
    public sealed class BoolToIntSource : MapValueSource<bool, int>
    {
        [SerializeField] private int falseValue;
        [SerializeField] private int trueValue = 1;
        protected override int Convert(bool value) => value ? trueValue : falseValue;
    }

    [Serializable]
    public sealed class FloatToIntSource : MapValueSource<float, int>
    {
        [SerializeField] private FloatIntRounding rounding = FloatIntRounding.ToEven;
        protected override int Convert(float value)
        {
            if (float.IsNaN(value)) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            var rounded = rounding == FloatIntRounding.Floor ? Math.Floor(value) : rounding == FloatIntRounding.Ceiling ? Math.Ceiling(value) : rounding == FloatIntRounding.Truncate ? Math.Truncate(value) : Math.Round(value, MidpointRounding.ToEven);
            return (int)rounded;
        }
    }

    [Serializable]
    public sealed class FloatToBoolSource : MapValueSource<float, bool>
    {
        [SerializeField] private float threshold = 0.5f;
        [SerializeField] private bool inclusive = true;
        protected override bool Convert(float value) => inclusive ? value >= threshold : value > threshold;
    }

    [Serializable]
    public sealed class IndexToFloatSource : MapValueSource<int, float>
    {
        [SerializeField] private float[] values = { 0f, 1f };
        protected override float Convert(int value) => values[Mathf.Clamp(value, 0, values.Length - 1)];
        protected override void Validate()
        {
            if (values == null || values.Length == 0) throw new InvalidOperationException("IndexToFloatSource lookup cannot be empty.");
        }
    }

    [Serializable]
    public sealed class IndexToIntSource : MapValueSource<int, int>
    {
        [SerializeField] private int[] values = { 0, 1 };
        protected override int Convert(int value) => values[Mathf.Clamp(value, 0, values.Length - 1)];
        protected override void Validate()
        {
            if (values == null || values.Length == 0) throw new InvalidOperationException("IndexToIntSource lookup cannot be empty.");
        }
    }

    [Serializable]
    public sealed class IndexToBoolSource : MapValueSource<int, bool>
    {
        [SerializeField] private bool[] values = { false, true };
        protected override bool Convert(int value) => values[Mathf.Clamp(value, 0, values.Length - 1)];
        protected override void Validate()
        {
            if (values == null || values.Length == 0) throw new InvalidOperationException("IndexToBoolSource lookup cannot be empty.");
        }
    }

    [Serializable]
    public sealed class IndexToColorSource : MapValueSource<int, Color>
    {
        [SerializeField] private Color[] values = { Color.black, Color.white };
        protected override Color Convert(int value) => values[Mathf.Clamp(value, 0, values.Length - 1)];
        protected override void Validate()
        {
            if (values == null || values.Length == 0) throw new InvalidOperationException("IndexToColorSource lookup cannot be empty.");
        }
    }

    [Serializable]
    public sealed class Vector2ToFloatSource : MapValueSource<Vector2, float>
    {
        [SerializeField] private VectorComponent component;
        protected override float Convert(Vector2 value) => component == VectorComponent.Y ? value.y : value.x;
    }

    [Serializable]
    public sealed class Vector3ToFloatSource : MapValueSource<Vector3, float>
    {
        [SerializeField] private VectorComponent component;
        protected override float Convert(Vector3 value) => component == VectorComponent.Y ? value.y : component == VectorComponent.Z ? value.z : value.x;
    }

    [Serializable]
    public sealed class Vector4ToFloatSource : MapValueSource<Vector4, float>
    {
        [SerializeField] private VectorComponent component;
        protected override float Convert(Vector4 value) => component == VectorComponent.Y ? value.y : component == VectorComponent.Z ? value.z : component == VectorComponent.W ? value.w : value.x;
    }

    [Serializable]
    public sealed class FloatToVector2Source : MapValueSource<float, Vector2>
    {
        [SerializeField] private Vector2 fixedComponents;
        [SerializeField] private VectorComponent component = VectorComponent.X;
        protected override Vector2 Convert(float value)
        {
            var result = fixedComponents;
            if (component == VectorComponent.Y) result.y = value; else result.x = value;
            return result;
        }
    }

    [Serializable]
    public sealed class FloatToVector3Source : MapValueSource<float, Vector3>
    {
        [SerializeField] private Vector3 fixedComponents;
        [SerializeField] private VectorComponent component = VectorComponent.X;
        protected override Vector3 Convert(float value)
        {
            var result = fixedComponents;
            if (component == VectorComponent.Y) result.y = value; else if (component == VectorComponent.Z) result.z = value; else result.x = value;
            return result;
        }
    }

    [Serializable]
    public sealed class FloatToVector4Source : MapValueSource<float, Vector4>
    {
        [SerializeField] private Vector4 fixedComponents;
        [SerializeField] private VectorComponent component = VectorComponent.X;
        protected override Vector4 Convert(float value)
        {
            var result = fixedComponents;
            if (component == VectorComponent.Y) result.y = value; else if (component == VectorComponent.Z) result.z = value; else if (component == VectorComponent.W) result.w = value; else result.x = value;
            return result;
        }
    }

    [Serializable]
    public sealed class FloatToGradientColorSource : MapValueSource<float, Color>
    {
        [SerializeField] private Gradient gradient = new Gradient();
        protected override Color Convert(float value)
        {
            if (gradient == null) throw new InvalidOperationException("Gradient is not assigned.");
            return gradient.Evaluate(Mathf.Clamp01(value));
        }
    }
}
