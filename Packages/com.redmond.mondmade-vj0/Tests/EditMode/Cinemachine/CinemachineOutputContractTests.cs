using System;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;

namespace MondmadeVJ0.Tests.Cinemachine
{
    public sealed class CinemachineOutputContractTests
    {
        private GameObject[] targetObjects;

        [SetUp]
        public void SetUp()
        {
            targetObjects = new GameObject[3];
            for (var i = 0; i < targetObjects.Length; i++)
                targetObjects[i] = new GameObject("Cinemachine Output Target " + i);
        }

        [TearDown]
        public void TearDown()
        {
            if (targetObjects == null) return;
            foreach (var target in targetObjects)
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void CameraIndexOutputSelectsPriorityAndClampsIndex()
        {
            var cameras = new[] { AddCamera(0), AddCamera(1), AddCamera(2) };
            var output = new CinemachineCameraIndexOutput();
            SetField(output, "cameras", cameras);
            SetField(output, "activePriority", 90);
            SetField(output, "inactivePriority", 5);
            output.Initialize(null);
            output.Write(99);

            Assert.That(cameras[0].Priority.Value, Is.EqualTo(5));
            Assert.That(cameras[1].Priority.Value, Is.EqualTo(5));
            Assert.That(cameras[2].Priority.Value, Is.EqualTo(90));
        }

        [Test]
        public void CameraIndexOutputCanSwitchGameObjectActivation()
        {
            var cameras = new[] { AddCamera(0), AddCamera(1) };
            var output = new CinemachineCameraIndexOutput();
            SetField(output, "cameras", cameras);
            SetField(output, "switchMode", CinemachineSwitchMode.GameObjectActive);
            output.Initialize(null);
            output.Write(1);

            Assert.That(targetObjects[0].activeSelf, Is.False);
            Assert.That(targetObjects[1].activeSelf, Is.True);
        }

        [Test]
        public void LensOutputsApplyFieldOfViewClampAndDutchValue()
        {
            var camera = AddCamera(0);
            var fov = new CinemachineFovFloatOutput();
            SetField(fov, "target", camera);
            fov.Initialize(null);
            fov.Write(200f);
            Assert.That(camera.Lens.FieldOfView, Is.EqualTo(179f));

            var dutch = new CinemachineDutchFloatOutput();
            SetField(dutch, "target", camera);
            dutch.Initialize(null);
            dutch.Write(-12.5f);
            Assert.That(camera.Lens.Dutch, Is.EqualTo(-12.5f));
        }

        [Test]
        public void PriorityOutputWritesCameraPriority()
        {
            var camera = AddCamera(0);
            var output = new CinemachinePriorityIntOutput();
            SetField(output, "target", camera);
            output.Initialize(null);
            output.Write(42);

            Assert.That(camera.Priority.Value, Is.EqualTo(42));
        }

        [Test]
        public void CameraIndexOutputRejectsEmptyOrMissingTargets()
        {
            var empty = new CinemachineCameraIndexOutput();
            Assert.Throws<InvalidOperationException>(() => empty.Initialize(null));

            var missing = new CinemachineCameraIndexOutput();
            SetField(missing, "cameras", new CinemachineCamera[] { null });
            Assert.Throws<InvalidOperationException>(() => missing.Initialize(null));

            Assert.Throws<InvalidOperationException>(() => new CinemachineFovFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new CinemachineDutchFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new CinemachinePriorityIntOutput().Initialize(null));
        }

        private CinemachineCamera AddCamera(int index) => targetObjects[index].AddComponent<CinemachineCamera>();

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
    }
}
