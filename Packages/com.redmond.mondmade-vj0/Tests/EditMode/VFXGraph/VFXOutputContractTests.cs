using System;
using System.IO;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
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
        private string generatedAssetPath;
        private RenderPipelineAsset previousRenderPipeline;
        private RenderPipelineAsset temporaryRenderPipeline;

        [TearDown]
        public void TearDown()
        {
            if (targetObject != null) UnityEngine.Object.DestroyImmediate(targetObject);
            targetObject = null;
            if (!string.IsNullOrEmpty(generatedAssetPath)) AssetDatabase.DeleteAsset(generatedAssetPath);
            generatedAssetPath = null;
            if (temporaryRenderPipeline != null)
            {
                GraphicsSettings.defaultRenderPipeline = previousRenderPipeline;
                UnityEngine.Object.DestroyImmediate(temporaryRenderPipeline);
                temporaryRenderPipeline = null;
                previousRenderPipeline = null;
            }
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
        public void FloatOutputWritesToARealExposedVfxProperty()
        {
            var effect = LoadSampleVfxEffect("VFX Float Output Contract Target");

            const string propertyName = "Spawn Rate";
            var propertyId = Shader.PropertyToID(propertyName);
            Assert.That(effect.HasFloat(propertyId), Is.True, "The sample exposes Spawn Rate as a float.");

            var output = new VFXFloatOutput();
            SetField(output, "target", effect);
            SetField(output, "propertyName", propertyName);
            output.Initialize(null);
            output.Write(27.5f);

            Assert.That(effect.GetFloat(propertyId), Is.EqualTo(27.5f).Within(0.0001f));
        }

        [Test]
        public void ExposedValueOutputsRejectAnExistingPropertyOfTheWrongType()
        {
            var effect = LoadSampleVfxEffect("VFX Wrong Type Output Contract Target");
            var floatPropertyId = Shader.PropertyToID("Spawn Rate");
            var boolPropertyId = Shader.PropertyToID("Use Flipbook");
            Assert.That(effect.HasFloat(floatPropertyId), Is.True);
            Assert.That(effect.HasBool(boolPropertyId), Is.True);

            foreach (var outputType in ExposedValueOutputTypes)
            {
                var output = Activator.CreateInstance(outputType);
                SetField(output, "target", effect);
                SetField(output, "propertyName", outputType == typeof(VFXFloatOutput) ? "Use Flipbook" : "Spawn Rate");

                var wrongType = InitializeError(output);
                Assert.That(wrongType.Message, Does.Contain("missing or is not"), outputType.Name);
            }
        }

        private VisualEffect LoadSampleVfxEffect(string objectName)
        {
            UseTemporaryUniversalRenderPipelineWhenNeeded();

            var package = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.visualeffectgraph");
            Assert.That(package, Is.Not.Null, "VFX Graph package must be resolvable from the VisualEffect assembly.");

            var samplePath = Path.Combine(package.resolvedPath, "Samples~", "VFXGraphAdditions", "VFX", "Flames.vfx");
            Assert.That(File.Exists(samplePath), Is.True, "The VFX Graph Flames sample must be present in the installed package.");

            generatedAssetPath = $"Assets/__MondmadeVJ0VFXOutputContract-{Guid.NewGuid():N}.vfx";
            File.Copy(samplePath, generatedAssetPath);
            AssetDatabase.ImportAsset(generatedAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(generatedAssetPath);
            Assert.That(asset, Is.Not.Null, "The installed VFX Graph sample must import as a VisualEffectAsset.");

            targetObject = new GameObject(objectName);
            var effect = targetObject.AddComponent<VisualEffect>();
            effect.visualEffectAsset = asset;
            effect.Reinit();
            return effect;
        }

        private void UseTemporaryUniversalRenderPipelineWhenNeeded()
        {
            if (GraphicsSettings.currentRenderPipeline != null) return;

            var pipelineType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            Assert.That(pipelineType, Is.Not.Null, "A supported render pipeline must be configured to compile a VFX Graph.");

            var createMethod = pipelineType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            Assert.That(createMethod, Is.Not.Null, "The installed URP package must expose its transient asset factory.");
            temporaryRenderPipeline = (RenderPipelineAsset)createMethod.Invoke(null, new object[] { null });
            Assert.That(temporaryRenderPipeline, Is.Not.Null);

            previousRenderPipeline = GraphicsSettings.defaultRenderPipeline;
            GraphicsSettings.defaultRenderPipeline = temporaryRenderPipeline;
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.SameAs(temporaryRenderPipeline), "The temporary URP must be active while importing the VFX sample.");
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
