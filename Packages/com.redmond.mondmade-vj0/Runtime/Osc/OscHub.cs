using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using OscJack;
using UnityEngine;

namespace MondmadeVJ0
{
    [DisallowMultipleComponent]
    public sealed class OscHub : MonoBehaviour, IRuntimeModule
    {
        internal const int QueueCapacity = 8192;
        internal const int InputQueueAddressCharacterCapacity = 512 * 1024;
        internal const int LatestMessageCacheCapacity = 8192;
        internal const int LatestMessageCacheCharacterCapacity = 256 * 1024;
        [SerializeField] private int listenPort = 9000;
        [SerializeField] private string destinationAddress = "127.0.0.1";
        [SerializeField] private int destinationPort = 9001;
        private readonly ConcurrentQueue<OscPacket> inputQueue = new ConcurrentQueue<OscPacket>();
        private readonly Dictionary<string, LinkedListNode<OscPacket>> latestMessages = new Dictionary<string, LinkedListNode<OscPacket>>(StringComparer.Ordinal);
        private readonly LinkedList<OscPacket> latestMessageRecency = new LinkedList<OscPacket>();
        private readonly object callbackLock = new object();
        private Action<OscPacket>[] messageCallbacks = Array.Empty<Action<OscPacket>>();
        private OscServer server;
        private OscClient client;
        private RuntimeServices services;
        private int queuedCount, queuedAddressCharacters, cachedAddressCharacters;
        private long receivedCount, sentCount, droppedCount, malformedCount;
        public string ModuleName => nameof(OscHub);
        public long ReceivedCount => Interlocked.Read(ref receivedCount);
        public long SentCount => Interlocked.Read(ref sentCount);
        public long DroppedCount => Interlocked.Read(ref droppedCount);
        public long MalformedCount => Interlocked.Read(ref malformedCount);
        internal event Action<OscPacket> MessageReceived
        {
            add { lock (callbackLock) messageCallbacks = OscCallbackArrays.AddCallback(messageCallbacks, value); }
            remove { lock (callbackLock) messageCallbacks = OscCallbackArrays.RemoveCallback(messageCallbacks, value); }
        }

        public void Register(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            runtimeServices.Register(this);
        }

        public void Initialize(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            if (listenPort < 1 || listenPort > 65535) throw new InvalidOperationException("OSC listen port is out of range.");
            if (destinationPort < 1 || destinationPort > 65535) throw new InvalidOperationException("OSC destination port is out of range.");
            server = new OscServer(listenPort);
            server.MessageDispatcher.AddCallback(string.Empty, OnWorkerMessage);
            try { client = new OscClient(destinationAddress, destinationPort); }
            catch (Exception exception) { services.Report("OSC_OUTPUT", "Could not open OSC output destination.", exception: exception); }
        }

        private void OnWorkerMessage(string address, OscDataHandle data)
        {
            try
            {
                if (!OscAddress.IsValid(address) || data == null) { Interlocked.Increment(ref malformedCount); return; }
                var count = data.GetElementCount();
                if (count > 4) { Interlocked.Increment(ref malformedCount); return; }
                var tag0 = '\0'; var tag1 = '\0'; var tag2 = '\0'; var tag3 = '\0';
                var float0 = 0f; var float1 = 0f; var float2 = 0f; var float3 = 0f;
                var int0 = 0; var int1 = 0; var int2 = 0; var int3 = 0;
                for (var i = 0; i < count; i++)
                {
                    var tag = data.GetElementTypeTag(i);
                    if (tag == 'i')
                    {
                        var value = data.GetElementAsInt(i);
                        switch (i) { case 0: tag0 = tag; int0 = value; break; case 1: tag1 = tag; int1 = value; break; case 2: tag2 = tag; int2 = value; break; case 3: tag3 = tag; int3 = value; break; }
                    }
                    else if (tag == 'f')
                    {
                        var value = data.GetElementAsFloat(i);
                        switch (i) { case 0: tag0 = tag; float0 = value; break; case 1: tag1 = tag; float1 = value; break; case 2: tag2 = tag; float2 = value; break; case 3: tag3 = tag; float3 = value; break; }
                    }
                    else { Interlocked.Increment(ref malformedCount); return; }
                }
                var packet = new OscPacket(address, count, tag0, tag1, tag2, tag3, float0, float1, float2, float3, int0, int1, int2, int3);
                TryQueuePacket(packet);
            }
            catch { Interlocked.Increment(ref malformedCount); }
        }

        internal bool TryQueuePacket(OscPacket packet)
        {
            if (!OscAddress.IsValid(packet.Address) || packet.ArgumentCount < 0 || packet.ArgumentCount > 4)
            {
                Interlocked.Increment(ref malformedCount);
                return false;
            }
            for (var i = 0; i < packet.ArgumentCount; i++)
            {
                var tag = packet.GetTypeTag(i);
                if (tag == 'i' || tag == 'f') continue;
                Interlocked.Increment(ref malformedCount);
                return false;
            }
            if (Interlocked.Increment(ref queuedCount) > QueueCapacity)
            {
                Interlocked.Decrement(ref queuedCount);
                Interlocked.Increment(ref droppedCount);
                return false;
            }
            if (Interlocked.Add(ref queuedAddressCharacters, packet.Address.Length) > InputQueueAddressCharacterCapacity)
            {
                Interlocked.Add(ref queuedAddressCharacters, -packet.Address.Length);
                Interlocked.Decrement(ref queuedCount);
                Interlocked.Increment(ref droppedCount);
                return false;
            }
            inputQueue.Enqueue(packet);
            Interlocked.Increment(ref receivedCount);
            return true;
        }

        internal void DispatchPacket(OscPacket packet)
        {
            var callbacks = messageCallbacks;
            for (var i = 0; i < callbacks.Length; i++)
            {
                try { callbacks[i](packet); }
                catch (Exception exception) { services?.Report("OSC_SUBSCRIBER", "An OSC route subscriber failed.", exception: exception); }
            }
        }

        public void DrainInputQueue(RuntimeServices runtimeServices, int maxEvents)
        {
            var remaining = Math.Min(Math.Max(0, maxEvents), Volatile.Read(ref queuedCount));
            for (var count = 0; count < remaining && inputQueue.TryDequeue(out var packet); count++)
            {
                Interlocked.Decrement(ref queuedCount);
                Interlocked.Add(ref queuedAddressCharacters, -packet.Address.Length);
                RememberLatest(packet);
                DispatchPacket(packet);
            }
        }

        public bool TryGetLatest(string address, out OscPacket packet)
        {
            if (!latestMessages.TryGetValue(address, out var node))
            {
                packet = default;
                return false;
            }

            latestMessageRecency.Remove(node);
            latestMessageRecency.AddLast(node);
            packet = node.Value;
            return true;
        }

        private void RememberLatest(OscPacket packet)
        {
            var address = packet.Address;
            if (latestMessages.TryGetValue(address, out var existing))
            {
                existing.Value = packet.WithAddress(existing.Value.Address);
                latestMessageRecency.Remove(existing);
                latestMessageRecency.AddLast(existing);
                return;
            }

            if (address.Length > LatestMessageCacheCharacterCapacity) return;
            while (latestMessages.Count >= LatestMessageCacheCapacity ||
                   cachedAddressCharacters + address.Length > LatestMessageCacheCharacterCapacity)
            {
                var oldest = latestMessageRecency.First;
                if (oldest == null) break;
                latestMessageRecency.RemoveFirst();
                latestMessages.Remove(oldest.Value.Address);
                cachedAddressCharacters -= oldest.Value.Address.Length;
            }

            var node = latestMessageRecency.AddLast(packet);
            latestMessages.Add(address, node);
            cachedAddressCharacters += address.Length;
        }

        public void Send(string address, int value)
        {
            RequireClient().Send(address, value);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address, float value)
        {
            RequireClient().Send(address, value);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address, Vector2 value)
        {
            RequireClient().Send(address, value.x, value.y);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address, Vector3 value)
        {
            RequireClient().Send(address, value.x, value.y, value.z);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address, Vector4 value)
        {
            RequireClient().Send(address, value.x, value.y, value.z, value.w);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address, Color value)
        {
            RequireClient().Send(address, value.r, value.g, value.b, value.a);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address)
        {
            RequireClient().Send(address);
            Interlocked.Increment(ref sentCount);
        }
        public void Send(string address, bool value) => Send(address, value ? 1 : 0);

        private OscClient RequireClient() => client ?? throw new InvalidOperationException("OSC output destination is unavailable.");

        public void Tick(RuntimeServices runtimeServices, double deltaTime) { }
        public void FlushFrame(RuntimeServices runtimeServices) { }

        public void Shutdown(RuntimeServices runtimeServices)
        {
            if (server != null)
            {
                server.MessageDispatcher.RemoveCallback(string.Empty, OnWorkerMessage);
                server.Dispose();
                server = null;
            }
            client?.Dispose();
            client = null;
            while (inputQueue.TryDequeue(out _)) { }
            queuedCount = 0;
            Interlocked.Exchange(ref queuedAddressCharacters, 0);
            latestMessages.Clear();
            latestMessageRecency.Clear();
            cachedAddressCharacters = 0;
            lock (callbackLock) messageCallbacks = Array.Empty<Action<OscPacket>>();
            services = null;
        }
    }

    public readonly struct OscPacket
    {
        public readonly string Address;
        public readonly int ArgumentCount;
        public readonly char Tag0, Tag1, Tag2, Tag3;
        public readonly float Float0, Float1, Float2, Float3;
        public readonly int Int0, Int1, Int2, Int3;
        public OscPacket(string address, int count, char tag0, char tag1, char tag2, char tag3, float float0, float float1, float float2, float float3, int int0, int int1, int int2, int int3)
        { Address = address; ArgumentCount = count; Tag0 = tag0; Tag1 = tag1; Tag2 = tag2; Tag3 = tag3; Float0 = float0; Float1 = float1; Float2 = float2; Float3 = float3; Int0 = int0; Int1 = int1; Int2 = int2; Int3 = int3; }
        internal OscPacket WithAddress(string address)
            => new OscPacket(address, ArgumentCount, Tag0, Tag1, Tag2, Tag3, Float0, Float1, Float2, Float3, Int0, Int1, Int2, Int3);
        public char GetTypeTag(int index) => index switch { 0 when ArgumentCount > 0 => Tag0, 1 when ArgumentCount > 1 => Tag1, 2 when ArgumentCount > 2 => Tag2, 3 when ArgumentCount > 3 => Tag3, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        public float GetFloat(int index) => index switch { 0 when ArgumentCount > 0 => Float0, 1 when ArgumentCount > 1 => Float1, 2 when ArgumentCount > 2 => Float2, 3 when ArgumentCount > 3 => Float3, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        public int GetInt(int index) => index switch { 0 when ArgumentCount > 0 => Int0, 1 when ArgumentCount > 1 => Int1, 2 when ArgumentCount > 2 => Int2, 3 when ArgumentCount > 3 => Int3, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        public bool HasNumericShape(string signature)
        {
            if (ArgumentCount != signature.Length) return false;
            for (var i = 0; i < ArgumentCount; i++) if (GetTypeTag(i) != signature[i]) return false;
            return true;
        }
    }

    internal static class OscCallbackArrays
    {
        public static Action<T>[] AddCallback<T>(Action<T>[] callbacks, Action<T> callback)
        {
            if (callback == null) return callbacks;
            var result = new Action<T>[callbacks.Length + 1];
            Array.Copy(callbacks, result, callbacks.Length);
            result[result.Length - 1] = callback;
            return result;
        }

        public static Action<T>[] RemoveCallback<T>(Action<T>[] callbacks, Action<T> callback)
        {
            if (callback == null) return callbacks;
            for (var i = callbacks.Length - 1; i >= 0; i--)
                if (callbacks[i] == callback)
                {
                    var result = new Action<T>[callbacks.Length - 1];
                    if (i > 0) Array.Copy(callbacks, 0, result, 0, i);
                    if (i < callbacks.Length - 1) Array.Copy(callbacks, i + 1, result, i, callbacks.Length - i - 1);
                    return result;
                }
            return callbacks;
        }
    }

    public static class OscAddress
    {
        private static readonly char[] ForbiddenCharacters = { ' ', '\t', '\r', '\n', '#' };
        public static bool IsValid(string address) => !string.IsNullOrWhiteSpace(address) && address[0] == '/' && address.IndexOfAny(ForbiddenCharacters) < 0;
        public static void Require(string address)
        { if (!IsValid(address)) throw new InvalidOperationException($"OSC address '{address}' must start with '/' and contain no spaces or '#'."); }
    }
}

namespace OscJack
{
    /// <summary>Reads OSC argument tags from the pinned OscJack 2.0 data handle.</summary>
    public static class OscDataHandleTypeTagExtension
    {
        private static readonly FieldInfo TypeTagsField = typeof(OscDataHandle).GetField("_typeTags", BindingFlags.Instance | BindingFlags.NonPublic);
        public static char GetElementTypeTag(this OscDataHandle handle, int index)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (index < 0 || index >= handle.GetElementCount()) throw new ArgumentOutOfRangeException(nameof(index));
            if (TypeTagsField == null) throw new MissingFieldException(typeof(OscDataHandle).FullName, "_typeTags");
            if (TypeTagsField.GetValue(handle) is not System.Collections.Generic.IList<char> tags || index >= tags.Count)
                throw new InvalidOperationException("The pinned OscJack type tag buffer is unavailable.");
            return tags[index];
        }
    }
}
