using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using R3;
using UnityEngine;
using OscJack;

namespace MondmadeVJ0.Tests
{
    public sealed class OscSourceAndQueueTests
    {
        private GameObject hubObject;
        private OscHub hub;
        private RuntimeServices services;
        private GameObject runtimeObject;
        private MondmadeVJ0Runtime runtime;
        private RouteContext context;

        [SetUp]
        public void SetUp()
        {
            hubObject = new GameObject("OSC queue contract test");
            hub = hubObject.AddComponent<OscHub>();
            runtimeObject = new GameObject("OSC source contract runtime");
            runtimeObject.SetActive(false);
            runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            services = new RuntimeServices { Runtime = runtime };
            runtime.Services = services;
            hub.Register(services);
            context = new RouteContext(runtime, null, new RuntimeStateStore(), new BindingScope(), "osc-source-test");
        }

        [TearDown]
        public void TearDown()
        {
            hub?.Shutdown(services);
            if (hubObject != null) UnityEngine.Object.DestroyImmediate(hubObject);
            if (runtimeObject != null) UnityEngine.Object.DestroyImmediate(runtimeObject);
            outputSnapshots = null;
        }

        [Test]
        public void WorkerPacketsAreQueuedAndDeliveredOnTheDrainThread()
        {
            var callbackCount = 0;
            var callbackThread = 0;
            Action<OscPacket> callback = _ => { callbackCount++; callbackThread = Thread.CurrentThread.ManagedThreadId; };
            hub.MessageReceived += callback;
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var queued = Task.Run(() => hub.TryQueuePacket(FloatPacket("/vj/worker", 0.75f))).GetAwaiter().GetResult();

            Assert.That(queued, Is.True);
            Assert.That(callbackCount, Is.Zero);
            Assert.That(hub.TryGetLatest("/vj/worker", out _), Is.False);

            hub.DrainInputQueue(services, 1);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(callbackThread, Is.EqualTo(mainThread));
            Assert.That(hub.ReceivedCount, Is.EqualTo(1));
            Assert.That(hub.TryGetLatest("/vj/worker", out var latest), Is.True);
            Assert.That(latest.GetFloat(0), Is.EqualTo(0.75f));
        }

        [Test]
        public void QueueCapacityMatchesContractAndDropsNewestOverflow()
        {
            for (var i = 0; i < OscHub.QueueCapacity; i++)
                Assert.That(hub.TryQueuePacket(FloatPacket("/vj/queue/" + i, i)), Is.True);

            Assert.That(hub.TryQueuePacket(FloatPacket("/vj/overflow", 1f)), Is.False);
            Assert.That(hub.ReceivedCount, Is.EqualTo(8192));
            Assert.That(hub.DroppedCount, Is.EqualTo(1));
            Assert.That(hub.MalformedCount, Is.Zero);

            hub.DrainInputQueue(services, 4096);
            Assert.That(hub.TryGetLatest("/vj/queue/4095", out _), Is.True);
            Assert.That(hub.TryGetLatest("/vj/queue/4096", out _), Is.False);
            hub.DrainInputQueue(services, 4096);
            Assert.That(hub.TryGetLatest("/vj/queue/8191", out _), Is.True);
            Assert.That(hub.TryGetLatest("/vj/overflow", out _), Is.False);
        }

        [Test]
        public void LatestMessageCacheIsBoundedAndEvictsLeastRecentlyUsedAddress()
        {
            for (var i = 0; i < OscHub.LatestMessageCacheCapacity; i++)
                Assert.That(hub.TryQueuePacket(FloatPacket("/vj/cache/" + i, i)), Is.True);
            hub.DrainInputQueue(services, OscHub.QueueCapacity);

            Assert.That(hub.TryGetLatest("/vj/cache/0", out _), Is.True);
            Assert.That(hub.TryGetLatest("/vj/cache/" + (OscHub.LatestMessageCacheCapacity - 1), out _), Is.True);
            Assert.That(hub.TryQueuePacket(FloatPacket("/vj/cache/new", 1f)), Is.True);
            hub.DrainInputQueue(services, 1);

            Assert.That(hub.TryGetLatest("/vj/cache/0", out _), Is.True, "A recently read address should stay in the cache.");
            Assert.That(hub.TryGetLatest("/vj/cache/1", out _), Is.False, "The least recently used address should be evicted.");
            Assert.That(hub.TryGetLatest("/vj/cache/new", out _), Is.True);
        }

        [Test]
        public void LatestMessageCacheIsBoundedByAggregateAddressLength()
        {
            var firstAddress = "/" + new string('a', 140000);
            var secondAddress = "/" + new string('b', 140000);
            var oversizedAddress = "/" + new string('c', OscHub.LatestMessageCacheCharacterCapacity);

            Assert.That(hub.TryQueuePacket(FloatPacket(firstAddress, 1f)), Is.True);
            Assert.That(hub.TryQueuePacket(FloatPacket(secondAddress, 2f)), Is.True);
            hub.DrainInputQueue(services, 2);
            Assert.That(hub.TryGetLatest(firstAddress, out _), Is.False);
            Assert.That(hub.TryGetLatest(secondAddress, out _), Is.True);

            Assert.That(hub.TryQueuePacket(FloatPacket(oversizedAddress, 3f)), Is.True);
            hub.DrainInputQueue(services, 1);
            Assert.That(hub.TryGetLatest(oversizedAddress, out _), Is.False);
            Assert.That(hub.TryGetLatest(secondAddress, out _), Is.True);
        }

        [Test]
        public void InputQueueBoundsAggregateAddressLengthAndStillDispatchesAcceptedPackets()
        {
            var firstAddress = "/" + new string('a', OscHub.InputQueueAddressCharacterCapacity / 2);
            var overflowingAddress = "/" + new string('b', OscHub.InputQueueAddressCharacterCapacity / 2);
            var dispatched = 0;
            Action<OscPacket> callback = _ => dispatched++;
            hub.MessageReceived += callback;

            Assert.That(hub.TryQueuePacket(FloatPacket(firstAddress, 1f)), Is.True);
            Assert.That(hub.TryQueuePacket(FloatPacket(overflowingAddress, 2f)), Is.False);
            Assert.That(hub.ReceivedCount, Is.EqualTo(1));
            Assert.That(hub.DroppedCount, Is.EqualTo(1));

            hub.DrainInputQueue(services, OscHub.QueueCapacity);
            Assert.That(hub.TryQueuePacket(FloatPacket(overflowingAddress, 2f)), Is.True, "Draining should release the pending address budget.");
            hub.DrainInputQueue(services, 1);

            Assert.That(dispatched, Is.EqualTo(2));
            Assert.That(hub.TryGetLatest(firstAddress, out _), Is.False, "Addresses beyond the latest-value cache budget are delivered but not retained.");
            hub.MessageReceived -= callback;
        }

        [Test]
        public void QueueRejectsMalformedPacketsBeforeTheyReachRoutes()
        {
            Assert.That(hub.TryQueuePacket(new OscPacket("missing/slash", 1, 'f', '\0', '\0', '\0', 1f, 0f, 0f, 0f, 0, 0, 0, 0)), Is.False);
            Assert.That(hub.TryQueuePacket(new OscPacket("/vj/string", 1, 's', '\0', '\0', '\0', 0f, 0f, 0f, 0f, 0, 0, 0, 0)), Is.False);
            Assert.That(hub.ReceivedCount, Is.Zero);
            Assert.That(hub.MalformedCount, Is.EqualTo(2));
        }

        [Test]
        public void SourcesAcceptOnlyTheirDocumentedOscShapes()
        {
            Assert.That(OscPacketValueReader.TryFloat(FloatPacket("/vj", 1.25f), out var floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(1.25f));
            Assert.That(OscPacketValueReader.TryFloat(IntPacket("/vj", 1), out _), Is.False);

            Assert.That(OscPacketValueReader.TryInt(IntPacket("/vj", -7), out var intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(-7));
            Assert.That(OscPacketValueReader.TryInt(FloatPacket("/vj", 1f), out _), Is.False);

            Assert.That(OscPacketValueReader.TryBool(IntPacket("/vj", 1), out var boolValue), Is.True);
            Assert.That(boolValue, Is.True);
            Assert.That(OscPacketValueReader.TryBool(IntPacket("/vj", 2), out _), Is.False);
            Assert.That(OscPacketValueReader.TryBool(FloatPacket("/vj", 1f), out _), Is.False);

            Assert.That(OscPacketValueReader.TryVector2(FloatVectorPacket("/vj", 2), out var vector2), Is.True);
            Assert.That(vector2, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(OscPacketValueReader.TryVector2(FloatVectorPacket("/vj", 3), out _), Is.False);

            Assert.That(OscPacketValueReader.TryVector3(FloatVectorPacket("/vj", 3), out var vector3), Is.True);
            Assert.That(vector3, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(OscPacketValueReader.TryVector3(FloatVectorPacket("/vj", 2), out _), Is.False);

            Assert.That(OscPacketValueReader.TryVector4(FloatVectorPacket("/vj", 4), out var vector4), Is.True);
            Assert.That(vector4, Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            Assert.That(OscPacketValueReader.TryColor(FloatVectorPacket("/vj", 4), out var color), Is.True);
            Assert.That(color, Is.EqualTo(new Color(1f, 2f, 3f, 4f)));
            Assert.That(OscPacketValueReader.TryColor(FloatVectorPacket("/vj", 3), out _), Is.False);

            Assert.That(OscPacketValueReader.IsTrigger(new OscPacket("/vj", 0, '\0', '\0', '\0', '\0', 0f, 0f, 0f, 0f, 0, 0, 0, 0)), Is.True);
            Assert.That(OscPacketValueReader.IsTrigger(FloatPacket("/vj", 0f)), Is.False);
        }

        [Test]
        public void FloatSourceEmitsItsInitialValueAndUsesTheLatestValueWhenBoundLate()
        {
            var values = new List<float>();
            using (new OscFloatSource().Build(context).Subscribe(value => values.Add(value)))
            {
                Assert.That(values, Is.EqualTo(new[] { 0f }));
                hub.DispatchPacket(IntPacket("/vj/value", 4));
                hub.DispatchPacket(FloatPacket("/vj/other", 0.5f));
                Assert.That(values, Is.EqualTo(new[] { 0f }));
                hub.DispatchPacket(FloatPacket("/vj/value", 0.75f));
                Assert.That(values, Is.EqualTo(new[] { 0f, 0.75f }));
            }

            Assert.That(hub.TryQueuePacket(FloatPacket("/vj/value", 0.25f)), Is.True);
            hub.DrainInputQueue(services, 1);
            values.Clear();
            using (new OscFloatSource().Build(context).Subscribe(value => values.Add(value)))
                Assert.That(values, Is.EqualTo(new[] { 0.25f }));
        }

        [Test, Timeout(10000)]
        public void UdpLoopbackPreservesInputAndOutputTypeTagsAndMainThreadDelivery()
        {
            var listenPort = FindAvailableUdpPort();
            var outputPort = FindAvailableUdpPort();
            while (outputPort == listenPort) outputPort = FindAvailableUdpPort();
            SetField(hub, "listenPort", listenPort);
            SetField(hub, "destinationAddress", "127.0.0.1");
            SetField(hub, "destinationPort", outputPort);
            using var outputServer = new OscServer(outputPort);
            using var sender = new OscClient("127.0.0.1", listenPort);
            using var outputReceived = new ManualResetEventSlim();
            var outputTag = '\0';
            var outputValue = 0f;
            OscMessageDispatcher.MessageCallback outputCallback = (address, data) =>
            {
                if (!string.Equals(address, "/vj/out", StringComparison.Ordinal)) return;
                outputTag = data.GetElementTypeTag(0);
                outputValue = data.GetElementAsFloat(0);
                outputReceived.Set();
            };
            outputServer.MessageDispatcher.AddCallback("/vj/out", outputCallback);

            var inputCount = 0;
            var inputThread = 0;
            Action<OscPacket> inputCallback = packet =>
            {
                if (!string.Equals(packet.Address, "/vj/in", StringComparison.Ordinal)) return;
                inputCount++;
                inputThread = Thread.CurrentThread.ManagedThreadId;
            };
            hub.MessageReceived += inputCallback;
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                hub.Initialize(services);
                sender.Send("/vj/in", 0.75f);
                Assert.That(SpinWait.SpinUntil(() => hub.ReceivedCount >= 1, 2000), Is.True, "OSC receive worker must enqueue the UDP packet.");
                Assert.That(inputCount, Is.Zero, "Worker callback must not invoke a Unity route subscriber.");
                hub.DrainInputQueue(services, 4096);
                Assert.That(inputCount, Is.EqualTo(1));
                Assert.That(inputThread, Is.EqualTo(mainThread));
                Assert.That(hub.TryGetLatest("/vj/in", out var inputPacket), Is.True);
                Assert.That(inputPacket.GetTypeTag(0), Is.EqualTo('f'));
                Assert.That(inputPacket.GetFloat(0), Is.EqualTo(0.75f));

                sender.Send("/vj/string", "not numeric");
                Assert.That(SpinWait.SpinUntil(() => hub.MalformedCount >= 1, 2000), Is.True, "Unsupported OSC types must be rejected by the receive adapter.");
                Assert.That(hub.TryGetLatest("/vj/string", out _), Is.False);

                hub.Send("/vj/out", 0.625f);
                Assert.That(outputReceived.Wait(2000), Is.True, "OSC output must reach the loopback receiver.");
                Assert.That(outputTag, Is.EqualTo('f'));
                Assert.That(outputValue, Is.EqualTo(0.625f));
                Assert.That(hub.SentCount, Is.EqualTo(1));
            }
            finally
            {
                hub.MessageReceived -= inputCallback;
                outputServer.MessageDispatcher.RemoveCallback("/vj/out", outputCallback);
                hub.Shutdown(services);
            }
        }

        [Test, Timeout(10000)]
        public void EveryOscOutputNodeEmitsItsDocumentedTypeShapeOverUdp()
        {
            var listenPort = FindAvailableUdpPort();
            var outputPort = FindAvailableUdpPort();
            while (outputPort == listenPort) outputPort = FindAvailableUdpPort();
            SetField(hub, "listenPort", listenPort);
            SetField(hub, "destinationAddress", "127.0.0.1");
            SetField(hub, "destinationPort", outputPort);
            using var outputServer = new OscServer(outputPort);
            outputSnapshots = new ConcurrentDictionary<string, OscOutputSnapshot>(StringComparer.Ordinal);
            OscMessageDispatcher.MessageCallback outputCallback = (address, data) =>
            {
                var count = data.GetElementCount();
                var tags = new char[count];
                var floats = new float[count];
                var ints = new int[count];
                for (var i = 0; i < count; i++)
                {
                    tags[i] = data.GetElementTypeTag(i);
                    if (tags[i] == 'f') floats[i] = data.GetElementAsFloat(i);
                    else if (tags[i] == 'i') ints[i] = data.GetElementAsInt(i);
                }
                outputSnapshots[address] = new OscOutputSnapshot(new string(tags), floats, ints);
            };
            outputServer.MessageDispatcher.AddCallback(string.Empty, outputCallback);
            try
            {
                hub.Initialize(services);
                SendOutput(new OscFloatOutput(), "/out/float", 0.75f);
                SendOutput(new OscIntOutput(), "/out/int", -7);
                SendOutput(new OscBoolOutput(), "/out/bool", true);
                SendOutput(new OscVector2Output(), "/out/vector2", new Vector2(1f, 2f));
                SendOutput(new OscVector3Output(), "/out/vector3", new Vector3(3f, 4f, 5f));
                SendOutput(new OscVector4Output(), "/out/vector4", new Vector4(6f, 7f, 8f, 9f));
                SendOutput(new OscColorOutput(), "/out/color", new Color(0.1f, 0.2f, 0.3f, 0.4f));
                var trigger = new OscTriggerOutput();
                SetSerializedField(trigger, "address", "/out/trigger");
                trigger.Initialize(context);
                trigger.Invoke();

                Assert.That(SpinWait.SpinUntil(() => outputSnapshots.Count == 8, 2000), Is.True, "Each typed OSC output must reach the UDP receiver.");
                AssertSnapshot("/out/float", "f", 0.75f);
                AssertIntSnapshot("/out/int", "i", -7);
                AssertIntSnapshot("/out/bool", "i", 1);
                AssertSnapshot("/out/vector2", "ff", 1f, 2f);
                AssertSnapshot("/out/vector3", "fff", 3f, 4f, 5f);
                AssertSnapshot("/out/vector4", "ffff", 6f, 7f, 8f, 9f);
                AssertSnapshot("/out/color", "ffff", 0.1f, 0.2f, 0.3f, 0.4f);
                Assert.That(outputSnapshots["/out/trigger"].TypeTags, Is.Empty);
                Assert.That(hub.SentCount, Is.EqualTo(8));
            }
            finally
            {
                outputServer.MessageDispatcher.RemoveCallback(string.Empty, outputCallback);
                hub.Shutdown(services);
            }
        }

        private void SendOutput<T>(IValueOutput<T> output, string address, T value)
        {
            SetSerializedField(output, "address", address);
            output.Initialize(context);
            output.Write(value);
        }

        private void AssertSnapshot(string address, string expectedTags, params float[] expectedValues)
        {
            Assert.That(outputSnapshots.ContainsKey(address), Is.True, "Expected OSC packet at " + address);
            var snapshot = outputSnapshots[address];
            Assert.That(snapshot.TypeTags, Is.EqualTo(expectedTags));
            Assert.That(snapshot.FloatValues, Is.EqualTo(expectedValues));
        }

        private void AssertIntSnapshot(string address, string expectedTags, int expectedValue)
        {
            Assert.That(outputSnapshots.ContainsKey(address), Is.True, "Expected OSC packet at " + address);
            var snapshot = outputSnapshots[address];
            Assert.That(snapshot.TypeTags, Is.EqualTo(expectedTags));
            Assert.That(snapshot.IntValues, Is.EqualTo(new[] { expectedValue }));
        }

        private ConcurrentDictionary<string, OscOutputSnapshot> outputSnapshots;

        private sealed class OscOutputSnapshot
        {
            public readonly string TypeTags;
            public readonly float[] FloatValues;
            public readonly int[] IntValues;
            public OscOutputSnapshot(string typeTags, float[] floatValues, int[] intValues)
            { TypeTags = typeTags; FloatValues = floatValues; IntValues = intValues; }
        }

        private static OscPacket FloatPacket(string address, float value)
            => new OscPacket(address, 1, 'f', '\0', '\0', '\0', value, 0f, 0f, 0f, 0, 0, 0, 0);

        private static OscPacket IntPacket(string address, int value)
            => new OscPacket(address, 1, 'i', '\0', '\0', '\0', 0f, 0f, 0f, 0f, value, 0, 0, 0);

        private static OscPacket FloatVectorPacket(string address, int count)
            => new OscPacket(address, count, 'f', count > 1 ? 'f' : '\0', count > 2 ? 'f' : '\0', count > 3 ? 'f' : '\0', 1f, 2f, 3f, 4f, 0, 0, 0, 0);

        private static int FindAvailableUdpPort()
        {
            using var client = new UdpClient(0);
            return ((IPEndPoint)client.Client.LocalEndPoint).Port;
        }

        private static void SetField<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected serialized field '{name}'.");
            field.SetValue(target, value);
        }

        private static void SetSerializedField<T>(object target, string name, T value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            Assert.Fail($"Expected serialized field '{name}' on {target.GetType().Name}.");
        }
    }
}
