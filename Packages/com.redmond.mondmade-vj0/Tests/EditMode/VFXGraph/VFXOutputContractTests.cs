using System;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.VFX;

namespace MondmadeVJ0.Tests.VFXGraph
{
    public sealed class VFXOutputContractTests
    {
        private static readonly Type[] ExposedValueOutputTypes =
        {
            typeof(VFXFloatOutput),
            typeof(VFXIntOutput),
            typeof(VFXUIntOutput),
            typeof(VFXBoolOutput),
            typeof(VFXVector2Output),
            typeof(VFXVector3Output),
            typeof(VFXVector4Output),
            typeof(VFXColorOutput),
            typeof(VFXMatrixOutput),
            typeof(VFXTextureOutput),
            typeof(VFXTextureIndexOutput),
        };

        private GameObject targetObject;

        [TearDown]
        public void TearDown()
        {
            if (targetObject != null) UnityEngine.Object.DestroyImmediate(targetObject);
            targetObject = null;
        }

        [TestCaseSource(nameof(ExposedValueOutputTypes))]
        public void ExposedValueOutputsRejectMissingTargetsAndUnexposedProperties(Type outputType)
        {
            var output = Activator.CreateInstance(outputType);
            var missingTarget = InitializeError(output);
            Assert.That(missingTarget.Message, Does.Contain("target is not assigned"), outputType.Name);

            targetObject = new GameObject("VFX Output Contract Target");
            var effect = targetObject.AddComponent<VisualEffect>();
            Assert.That(effect, Is.Not.Null);
            SetField(output, "target", effect);
            SetField(output, "propertyName", "MondmadeDefinitelyMissing");

            var missingProperty = InitializeError(output);
            Assert.That(missingProperty.Message, Does.Contain("missing or is not"), outputType.Name);
        }

        [Test]
        public void TriggerOutputRejectsMissingTargetAndEmptyEventName()
        {
            var output = new VFXTriggerEventOutput();
            Assert.That(InitializeError(output).Message, Does.Contain("target is not assigned"));

            targetObject = new GameObject("VFX Trigger Output Target");
            SetField(output, "target", targetObject.AddComponent<VisualEffect>());
            SetField(output, "eventName", " ");
            Assert.That(InitializeError(output).Message, Does.Contain("event name is empty"));
        }

        private static InvalidOperationException InitializeError(object output)
        {
            var initialize = output.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(initialize, Is.Not.Null, output.GetType().Name + " must expose Initialize(RouteContext).");
            var exception = Assert.Throws<TargetInvocationException>(() => initialize.Invoke(output, new object[] { null }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>(), output.GetType().Name);
            return (InvalidOperationException)exception.InnerException;
        }

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
