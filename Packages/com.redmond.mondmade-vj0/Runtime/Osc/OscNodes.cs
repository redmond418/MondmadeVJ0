using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    internal static class OscPacketValueReader
    {
        public static bool TryFloat(OscPacket packet, out float value)
        { value = packet.HasNumericShape("f") ? packet.GetFloat(0) : default; return packet.HasNumericShape("f"); }
        public static bool TryInt(OscPacket packet, out int value)
        { value = packet.HasNumericShape("i") ? packet.GetInt(0) : default; return packet.HasNumericShape("i"); }
        public static bool TryBool(OscPacket packet, out bool value)
        {
            value = false;
            if (!packet.HasNumericShape("i") || (packet.GetInt(0) != 0 && packet.GetInt(0) != 1)) return false;
            value = packet.GetInt(0) != 0;
            return true;
        }
        public static bool TryVector2(OscPacket packet, out Vector2 value)
        { value = packet.HasNumericShape("ff") ? new Vector2(packet.GetFloat(0), packet.GetFloat(1)) : default; return packet.HasNumericShape("ff"); }
        public static bool TryVector3(OscPacket packet, out Vector3 value)
        { value = packet.HasNumericShape("fff") ? new Vector3(packet.GetFloat(0), packet.GetFloat(1), packet.GetFloat(2)) : default; return packet.HasNumericShape("fff"); }
        public static bool TryVector4(OscPacket packet, out Vector4 value)
        { value = packet.HasNumericShape("ffff") ? new Vector4(packet.GetFloat(0), packet.GetFloat(1), packet.GetFloat(2), packet.GetFloat(3)) : default; return packet.HasNumericShape("ffff"); }
        public static bool TryColor(OscPacket packet, out Color value)
        { value = packet.HasNumericShape("ffff") ? new Color(packet.GetFloat(0), packet.GetFloat(1), packet.GetFloat(2), packet.GetFloat(3)) : default; return packet.HasNumericShape("ffff"); }
        public static bool IsTrigger(OscPacket packet) => packet.ArgumentCount == 0;
    }

    [Serializable]
    public abstract class OscValueSource<T> : RouteNode, IValueSource<T>
    {
        [SerializeField] protected string address = "/vj/value";
        [SerializeField] protected T initialValue;
        protected abstract bool TryRead(OscPacket packet, out T value);
        public Observable<T> Build(RouteContext context)
        {
            OscAddress.Require(address);
            var hub = context.Services.Get<OscHub>();
            return Observable.Create<T>(observer =>
            {
                var current = initialValue;
                if (hub.TryGetLatest(address, out var previous) && TryRead(previous, out var latest)) current = latest;
                observer.OnNext(current);
                Action<OscPacket> callback = packet =>
                {
                    if (!string.Equals(packet.Address, address, StringComparison.Ordinal) || !TryRead(packet, out var value)) return;
                    observer.OnNext(value);
                };
                hub.MessageReceived += callback;
                return Disposable.Create(() => hub.MessageReceived -= callback);
            });
        }
    }

    [Serializable]
    public sealed class OscFloatSource : OscValueSource<float>
    {
        protected override bool TryRead(OscPacket packet, out float value)
            => OscPacketValueReader.TryFloat(packet, out value);
    }

    [Serializable]
    public sealed class OscIntSource : OscValueSource<int>
    {
        protected override bool TryRead(OscPacket packet, out int value)
            => OscPacketValueReader.TryInt(packet, out value);
    }

    [Serializable]
    public sealed class OscBoolSource : OscValueSource<bool>
    {
        protected override bool TryRead(OscPacket packet, out bool value)
            => OscPacketValueReader.TryBool(packet, out value);
    }

    [Serializable]
    public sealed class OscVector2Source : OscValueSource<Vector2>
    {
        protected override bool TryRead(OscPacket packet, out Vector2 value)
            => OscPacketValueReader.TryVector2(packet, out value);
    }

    [Serializable]
    public sealed class OscVector3Source : OscValueSource<Vector3>
    {
        protected override bool TryRead(OscPacket packet, out Vector3 value)
            => OscPacketValueReader.TryVector3(packet, out value);
    }

    [Serializable]
    public sealed class OscVector4Source : OscValueSource<Vector4>
    {
        protected override bool TryRead(OscPacket packet, out Vector4 value)
            => OscPacketValueReader.TryVector4(packet, out value);
    }

    [Serializable]
    public sealed class OscColorSource : OscValueSource<Color>
    {
        protected override bool TryRead(OscPacket packet, out Color value)
            => OscPacketValueReader.TryColor(packet, out value);
    }

    [Serializable]
    public sealed class OscTriggerSource : RouteNode, ITriggerSource
    {
        [SerializeField] private string address = "/vj/trigger";
        public Observable<Unit> Build(RouteContext context)
        {
            OscAddress.Require(address);
            var hub = context.Services.Get<OscHub>();
            return Observable.Create<Unit>(observer =>
            {
                Action<OscPacket> callback = packet => { if (OscPacketValueReader.IsTrigger(packet) && string.Equals(packet.Address, address, StringComparison.Ordinal)) observer.OnNext(Unit.Default); };
                hub.MessageReceived += callback;
                return Disposable.Create(() => hub.MessageReceived -= callback);
            });
        }
    }

    [Serializable]
    public abstract class OscValueOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] protected string address = "/vj/value";
        [NonSerialized] protected OscHub Hub;
        public void Initialize(RouteContext context)
        {
            OscAddress.Require(address);
            Hub = context.Services.Get<OscHub>();
        }
        public abstract void Write(T value);
    }

    [Serializable] public sealed class OscFloatOutput : OscValueOutput<float> { public override void Write(float value) => Hub.Send(address, value); }
    [Serializable] public sealed class OscIntOutput : OscValueOutput<int> { public override void Write(int value) => Hub.Send(address, value); }
    [Serializable] public sealed class OscBoolOutput : OscValueOutput<bool> { public override void Write(bool value) => Hub.Send(address, value); }
    [Serializable] public sealed class OscVector2Output : OscValueOutput<Vector2> { public override void Write(Vector2 value) => Hub.Send(address, value); }
    [Serializable] public sealed class OscVector3Output : OscValueOutput<Vector3> { public override void Write(Vector3 value) => Hub.Send(address, value); }
    [Serializable] public sealed class OscVector4Output : OscValueOutput<Vector4> { public override void Write(Vector4 value) => Hub.Send(address, value); }
    [Serializable] public sealed class OscColorOutput : OscValueOutput<Color> { public override void Write(Color value) => Hub.Send(address, value); }

    [Serializable]
    public sealed class OscTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private string address = "/vj/trigger";
        [NonSerialized] private OscHub hub;
        public void Initialize(RouteContext context)
        {
            OscAddress.Require(address);
            hub = context.Services.Get<OscHub>();
        }
        public void Invoke() => hub.Send(address);
    }
}
