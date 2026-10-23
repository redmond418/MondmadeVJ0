using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MondmadeVJ0.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MondmadeVJ0.Tests.Rendering
{
    [Serializable]
    [VolumeComponentMenu("Mondmade VJ0 Tests/Rendering Output")]
    public sealed class RenderingOutputTestComponent : VolumeComponent
    {
        public ClampedFloatParameter amount = new ClampedFloatParameter(0.25f, 0f, 1f);
        public ClampedFloatParameter secondary = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter followed = new ClampedFloatParameter(0.2f, 0f, 1f);
        public IntParameter integer = new IntParameter(3);
        public BoolParameter toggle = new BoolParameter(false);
        public ColorParameter color = new ColorParameter(Color.black);
        public Vector2Parameter vector2 = new Vector2Parameter(Vector2.zero);
        public Vector3Parameter vector3 = new Vector3Parameter(Vector3.zero);
        public Vector4Parameter vector4 = new Vector4Parameter(Vector4.zero);
    }

    public sealed class RenderingOutputResourceTests
    {
        private static readonly object[] ShaderPropertyTypeMismatchCases =
        {
            new object[] { typeof(RendererFloatOutput), "_ContractInt" },
            new object[] { typeof(RendererIntOutput), "_ContractFloat" },
            new object[] { typeof(RendererVectorOutput), "_ContractFloat" },
            new object[] { typeof(RendererColorOutput), "_ContractInt" },
            new object[] { typeof(RendererTextureOutput), "_ContractFloat" },
            new object[] { typeof(RendererTextureIndexOutput), "_ContractInt" },
            new object[] { typeof(MaterialFloatOutput), "_ContractInt" },
            new object[] { typeof(MaterialIntOutput), "_ContractFloat" },
            new object[] { typeof(MaterialVectorOutput), "_ContractFloat" },
            new object[] { typeof(MaterialColorOutput), "_ContractInt" },
            new object[] { typeof(MaterialTextureOutput), "_ContractFloat" },
        };

        private static readonly object[] VolumeParameterTypeMismatchCases =
        {
            new object[] { typeof(VolumeFloatOutput), nameof(RenderingOutputTestComponent.integer) },
            new object[] { typeof(VolumeIntOutput), nameof(RenderingOutputTestComponent.amount) },
            new object[] { typeof(VolumeBoolOutput), nameof(RenderingOutputTestComponent.amount) },
            new object[] { typeof(VolumeColorOutput), nameof(RenderingOutputTestComponent.vector4) },
            new object[] { typeof(VolumeVector2Output), nameof(RenderingOutputTestComponent.vector3) },
            new object[] { typeof(VolumeVectorOutput), nameof(RenderingOutputTestComponent.vector2) },
            new object[] { typeof(VolumeVector4Output), nameof(RenderingOutputTestComponent.color) },
        };

        private readonly List<UnityEngine.Object> cleanup = new List<UnityEngine.Object>();
        private readonly List<BindingScope> scopes = new List<BindingScope>();
        private GameObject runtimeObject;
        private GameObject hostObject;
        private MondmadeVJ0Runtime runtime;
        private RouteHost host;

        [SetUp]
        public void SetUp()
        {
            runtimeObject = new GameObject("Rendering Output Runtime");
            runtimeObject.SetActive(false);
            runtime = runtimeObject.AddComponent<MondmadeVJ0Runtime>();
            var services = new RuntimeServices { Runtime = runtime };
            services.Register<IFrameClock>(runtime.Clock);
            services.Register(runtime.Clock);
            services.Register(new FrameEndScheduler());
            runtime.Services = services;
            hostObject = new GameObject("Rendering Output Host");
            host = hostObject.AddComponent<RouteHost>();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = scopes.Count - 1; i >= 0; i--) scopes[i]?.Dispose();
            if (runtimeObject != null) UnityEngine.Object.DestroyImmediate(runtimeObject);
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
            for (var i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) UnityEngine.Object.DestroyImmediate(cleanup[i]);
            scopes.Clear();
            cleanup.Clear();
        }

        [Test]
        public void MaterialInstanceIsSharedBetweenOutputsAndRestoredAfterLastLease()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The built-in Standard shader is unavailable in this Editor.");
            var shared = Own(new Material(shader));
            shared.SetFloat("_Glossiness", 0.25f);
            var rendererObject = Own(new GameObject("Material Output Renderer"));
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = shared;

            var firstScope = NewScope();
            var secondScope = NewScope();
            var first = new MaterialFloatOutput();
            var second = new MaterialFloatOutput();
            ConfigureMaterialOutput(first, renderer, "_Glossiness");
            ConfigureMaterialOutput(second, renderer, "_Glossiness");
            first.Initialize(Context(firstScope));
            second.Initialize(Context(secondScope));
            var instance = renderer.sharedMaterial;

            Assert.That(instance, Is.Not.SameAs(shared));
            first.Write(0.8f);
            second.Write(0.6f);
            Assert.That(renderer.sharedMaterial, Is.SameAs(instance));
            Assert.That(instance.GetFloat("_Glossiness"), Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(shared.GetFloat("_Glossiness"), Is.EqualTo(0.25f).Within(0.001f));

            firstScope.Dispose();
            Assert.That(renderer.sharedMaterial, Is.SameAs(instance));
            secondScope.Dispose();
            Assert.That(renderer.sharedMaterial, Is.SameAs(shared));
            Assert.That(instance == null, Is.True);
        }

        [Test]
        public void SharedAssetModeWritesDirectlyToTheExplicitMaterial()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The built-in Standard shader is unavailable in this Editor.");
            var shared = Own(new Material(shader));
            var output = new MaterialFloatOutput();
            SetField(output, "outputMode", MaterialOutputMode.SharedAsset);
            SetField(output, "target", shared);
            SetField(output, "propertyName", "_Glossiness");
            var scope = NewScope();

            output.Initialize(Context(scope));
            output.Write(0.8f);

            Assert.That(shared.GetFloat("_Glossiness"), Is.EqualTo(0.8f).Within(0.001f));
            scope.Dispose();
            Assert.That(shared.GetFloat("_Glossiness"), Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void RendererMaterialPropertyBlockWritePreservesOtherProperties()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The built-in Standard shader is unavailable in this Editor.");
            var material = Own(new Material(shader));
            var rendererObject = Own(new GameObject("Property Block Renderer"));
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var propertyBlock = new MaterialPropertyBlock();
            var preservedId = Shader.PropertyToID("_Metallic");
            propertyBlock.SetFloat(preservedId, 0.37f);
            renderer.SetPropertyBlock(propertyBlock, 0);
            var output = new RendererFloatOutput();
            SetField(output, "target", renderer);
            SetField(output, "materialIndex", 0);
            SetField(output, "propertyName", "_Glossiness");
            var scope = NewScope();

            output.Initialize(Context(scope));
            output.Write(0.82f);
            renderer.GetPropertyBlock(propertyBlock, 0);

            Assert.That(propertyBlock.GetFloat(preservedId), Is.EqualTo(0.37f).Within(0.001f));
            Assert.That(propertyBlock.GetFloat(Shader.PropertyToID("_Glossiness")), Is.EqualTo(0.82f).Within(0.001f));
        }

        [Test]
        public void VolumeProfileCloneIsSharedAndOverrideModesFollowTheirContracts()
        {
            var sharedProfile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            var sharedComponent = sharedProfile.Add<RenderingOutputTestComponent>(overrides: true);
            sharedComponent.amount.value = 0.25f;
            sharedComponent.amount.overrideState = false;
            sharedComponent.secondary.value = 0.5f;
            sharedComponent.secondary.overrideState = false;
            sharedComponent.followed.value = 0.2f;
            sharedComponent.followed.overrideState = false;
            sharedComponent.active = false;

            var volumeObject = Own(new GameObject("Volume Output Target"));
            var volume = volumeObject.AddComponent<Volume>();
            volume.sharedProfile = sharedProfile;
            var originalRuntimeProfile = volume.profile;
            var autoScope = NewScope();
            var alwaysScope = NewScope();
            var followScope = NewScope();
            var automatic = new VolumeFloatOutput();
            var always = new VolumeFloatOutput();
            var follow = new VolumeFloatOutput();
            ConfigureVolumeOutput(automatic, volume, nameof(RenderingOutputTestComponent.amount), VolumeOverrideMode.Automatically);
            ConfigureVolumeOutput(always, volume, nameof(RenderingOutputTestComponent.secondary), VolumeOverrideMode.Always);
            ConfigureVolumeOutput(follow, volume, nameof(RenderingOutputTestComponent.followed), VolumeOverrideMode.FollowExisting);

            automatic.Initialize(Context(autoScope));
            var runtimeProfile = volume.profile;
            always.Initialize(Context(alwaysScope));
            follow.Initialize(Context(followScope));
            Assert.That(runtimeProfile, Is.Not.SameAs(originalRuntimeProfile));
            Assert.That(volume.profile, Is.SameAs(runtimeProfile));

            var runtimeComponent = runtimeProfile.components.OfType<RenderingOutputTestComponent>().Single();
            automatic.Write(0.25f);
            Assert.That(runtimeComponent.amount.overrideState, Is.False);
            automatic.Write(0.75f);
            Assert.That(runtimeComponent.amount.overrideState, Is.True);
            automatic.Write(0.25f);
            Assert.That(runtimeComponent.amount.overrideState, Is.False);

            Assert.That(runtimeComponent.secondary.overrideState, Is.True);
            always.Write(0.9f);
            Assert.That(runtimeComponent.secondary.overrideState, Is.True);
            follow.Write(0.6f);
            Assert.That(runtimeComponent.followed.value, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(runtimeComponent.followed.overrideState, Is.False);
            Assert.That(runtimeComponent.active, Is.False);

            Assert.That(sharedComponent.amount.value, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(sharedComponent.secondary.value, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(runtime.Services.GetDiagnostics().Any(item => item.Code == "VOLUME_COMPONENT_INACTIVE"), Is.True);

            autoScope.Dispose();
            alwaysScope.Dispose();
            Assert.That(volume.profile, Is.SameAs(runtimeProfile));
            followScope.Dispose();
            Assert.That(volume.profile, Is.SameAs(originalRuntimeProfile));
            Assert.That(runtimeProfile == null, Is.True);
            Assert.That(volume.sharedProfile, Is.SameAs(sharedProfile));
        }

        [Test]
        public void AutomaticallyAddsMissingComponentToRuntimeCloneOnly()
        {
            var sharedProfile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            var volumeObject = Own(new GameObject("Automatic Volume Target"));
            var volume = volumeObject.AddComponent<Volume>();
            volume.sharedProfile = sharedProfile;
            var scope = NewScope();
            var output = new VolumeFloatOutput();
            ConfigureVolumeOutput(output, volume, nameof(RenderingOutputTestComponent.amount), VolumeOverrideMode.Automatically);

            output.Initialize(Context(scope));

            Assert.That(sharedProfile.components, Is.Empty);
            Assert.That(volume.profile.components.OfType<RenderingOutputTestComponent>().Count(), Is.EqualTo(1));
            scope.Dispose();
            Assert.That(sharedProfile.components, Is.Empty);
            Assert.That(volume.HasInstantiatedProfile(), Is.False);
        }

        [Test]
        public void VolumeOutputsWriteEverySupportedValueType()
        {
            var sharedProfile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            sharedProfile.Add<RenderingOutputTestComponent>(overrides: true);
            var volumeObject = Own(new GameObject("Typed Volume Output Target"));
            var volume = volumeObject.AddComponent<Volume>();
            volume.sharedProfile = sharedProfile;
            var scope = NewScope();

            var integer = new VolumeIntOutput();
            var boolean = new VolumeBoolOutput();
            var color = new VolumeColorOutput();
            var vector2 = new VolumeVector2Output();
            var vector3 = new VolumeVectorOutput();
            var vector4 = new VolumeVector4Output();
            ConfigureVolumeOutput(integer, volume, nameof(RenderingOutputTestComponent.integer), VolumeOverrideMode.Always);
            ConfigureVolumeOutput(boolean, volume, nameof(RenderingOutputTestComponent.toggle), VolumeOverrideMode.Always);
            ConfigureVolumeOutput(color, volume, nameof(RenderingOutputTestComponent.color), VolumeOverrideMode.Always);
            ConfigureVolumeOutput(vector2, volume, nameof(RenderingOutputTestComponent.vector2), VolumeOverrideMode.Always);
            ConfigureVolumeOutput(vector3, volume, nameof(RenderingOutputTestComponent.vector3), VolumeOverrideMode.Always);
            ConfigureVolumeOutput(vector4, volume, nameof(RenderingOutputTestComponent.vector4), VolumeOverrideMode.Always);

            integer.Initialize(Context(scope));
            boolean.Initialize(Context(scope));
            color.Initialize(Context(scope));
            vector2.Initialize(Context(scope));
            vector3.Initialize(Context(scope));
            vector4.Initialize(Context(scope));

            var expectedColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            var expectedVector2 = new Vector2(1f, 2f);
            var expectedVector3 = new Vector3(3f, 4f, 5f);
            var expectedVector4 = new Vector4(6f, 7f, 8f, 9f);
            integer.Write(17);
            boolean.Write(true);
            color.Write(expectedColor);
            vector2.Write(expectedVector2);
            vector3.Write(expectedVector3);
            vector4.Write(expectedVector4);

            var component = volume.profile.components.OfType<RenderingOutputTestComponent>().Single();
            Assert.That(component.integer.value, Is.EqualTo(17));
            Assert.That(component.toggle.value, Is.True);
            Assert.That(component.color.value, Is.EqualTo(expectedColor));
            Assert.That(component.vector2.value, Is.EqualTo(expectedVector2));
            Assert.That(component.vector3.value, Is.EqualTo(expectedVector3));
            Assert.That(component.vector4.value, Is.EqualTo(expectedVector4));
        }

        [TestCaseSource(nameof(VolumeParameterTypeMismatchCases))]
        public void VolumeOutputsRejectMissingTargetsAndWrongParameterTypes(Type outputType, string wrongParameterName)
        {
            var outputWithoutTarget = Activator.CreateInstance(outputType);
            SetField(outputWithoutTarget, "componentTypeName", typeof(RenderingOutputTestComponent).AssemblyQualifiedName);
            SetField(outputWithoutTarget, "parameterName", wrongParameterName);
            var missingTarget = InitializeError(outputWithoutTarget, null);
            Assert.That(missingTarget.Message, Does.Contain("Volume target is not assigned"), outputType.Name);

            var profile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            profile.Add<RenderingOutputTestComponent>(overrides: true);
            var volumeObject = Own(new GameObject("Wrong Type Volume Output Target"));
            var volume = volumeObject.AddComponent<Volume>();
            volume.sharedProfile = profile;

            var output = Activator.CreateInstance(outputType);
            ConfigureVolumeOutput(output, volume, wrongParameterName, VolumeOverrideMode.Always);
            var wrongType = InitializeError(output, Context(NewScope()));
            Assert.That(wrongType.Message, Does.Contain("is not a"), outputType.Name);
        }

        [Test]
        public void RendererOutputsWriteEverySupportedValueTypeAndClampTextureIndex()
        {
            var shader = Shader.Find("Hidden/MondmadeVJ0/OutputContractProperties");
            Assert.That(shader, Is.Not.Null, "The rendering output test shader must be imported.");
            var material = Own(new Material(shader));
            var rendererObject = Own(new GameObject("Typed Renderer Output Target"));
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var firstTexture = Own(new Texture2D(2, 2));
            var secondTexture = Own(new Texture2D(2, 2));

            var floatOutput = new RendererFloatOutput();
            var intOutput = new RendererIntOutput();
            var vectorOutput = new RendererVectorOutput();
            var colorOutput = new RendererColorOutput();
            var textureOutput = new RendererTextureOutput();
            var textureIndexOutput = new RendererTextureIndexOutput();
            ConfigureRendererOutput(floatOutput, renderer, "_ContractFloat");
            ConfigureRendererOutput(intOutput, renderer, "_ContractInt");
            ConfigureRendererOutput(vectorOutput, renderer, "_ContractVector");
            ConfigureRendererOutput(colorOutput, renderer, "_ContractColor");
            ConfigureRendererOutput(textureOutput, renderer, "_ContractTexture");
            ConfigureRendererOutput(textureIndexOutput, renderer, "_ContractTexture");
            SetField(textureIndexOutput, "textures", new Texture[] { firstTexture, secondTexture });

            floatOutput.Initialize(null);
            intOutput.Initialize(null);
            vectorOutput.Initialize(null);
            colorOutput.Initialize(null);
            textureOutput.Initialize(null);
            textureIndexOutput.Initialize(null);

            var expectedVector = new Vector4(1f, 2f, 3f, 4f);
            var expectedColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            floatOutput.Write(0.75f);
            intOutput.Write(17);
            vectorOutput.Write(expectedVector);
            colorOutput.Write(expectedColor);
            textureOutput.Write(firstTexture);
            textureIndexOutput.Write(99);

            var propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock, 0);
            Assert.That(propertyBlock.GetFloat("_ContractFloat"), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(propertyBlock.GetInteger("_ContractInt"), Is.EqualTo(17));
            Assert.That(propertyBlock.GetVector("_ContractVector"), Is.EqualTo(expectedVector));
            var actualColor = propertyBlock.GetColor("_ContractColor");
            Assert.That(actualColor.r, Is.EqualTo(expectedColor.r).Within(0.001f));
            Assert.That(actualColor.g, Is.EqualTo(expectedColor.g).Within(0.001f));
            Assert.That(actualColor.b, Is.EqualTo(expectedColor.b).Within(0.001f));
            Assert.That(actualColor.a, Is.EqualTo(expectedColor.a).Within(0.001f));
            Assert.That(propertyBlock.GetTexture("_ContractTexture"), Is.SameAs(secondTexture));
        }

        [Test]
        public void MaterialOutputsWriteEverySupportedValueTypeAndReleaseTheirSharedLease()
        {
            var shader = Shader.Find("Hidden/MondmadeVJ0/OutputContractProperties");
            Assert.That(shader, Is.Not.Null, "The rendering output test shader must be imported.");
            var sharedMaterial = Own(new Material(shader));
            var rendererObject = Own(new GameObject("Typed Material Output Target"));
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            var texture = Own(new Texture2D(2, 2));
            var scope = NewScope();

            var floatOutput = new MaterialFloatOutput();
            var intOutput = new MaterialIntOutput();
            var vectorOutput = new MaterialVectorOutput();
            var colorOutput = new MaterialColorOutput();
            var textureOutput = new MaterialTextureOutput();
            ConfigureMaterialOutput(floatOutput, renderer, "_ContractFloat");
            ConfigureMaterialOutput(intOutput, renderer, "_ContractInt");
            ConfigureMaterialOutput(vectorOutput, renderer, "_ContractVector");
            ConfigureMaterialOutput(colorOutput, renderer, "_ContractColor");
            ConfigureMaterialOutput(textureOutput, renderer, "_ContractTexture");
            floatOutput.Initialize(Context(scope));
            intOutput.Initialize(Context(scope));
            vectorOutput.Initialize(Context(scope));
            colorOutput.Initialize(Context(scope));
            textureOutput.Initialize(Context(scope));

            var expectedVector = new Vector4(5f, 6f, 7f, 8f);
            var expectedColor = new Color(0.4f, 0.3f, 0.2f, 0.1f);
            floatOutput.Write(0.5f);
            intOutput.Write(23);
            vectorOutput.Write(expectedVector);
            colorOutput.Write(expectedColor);
            textureOutput.Write(texture);

            var runtimeMaterial = renderer.sharedMaterial;
            Assert.That(runtimeMaterial, Is.Not.SameAs(sharedMaterial));
            Assert.That(runtimeMaterial.GetFloat("_ContractFloat"), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(runtimeMaterial.GetInteger("_ContractInt"), Is.EqualTo(23));
            Assert.That(runtimeMaterial.GetVector("_ContractVector"), Is.EqualTo(expectedVector));
            var actualColor = runtimeMaterial.GetColor("_ContractColor");
            Assert.That(actualColor.r, Is.EqualTo(expectedColor.r).Within(0.001f));
            Assert.That(actualColor.g, Is.EqualTo(expectedColor.g).Within(0.001f));
            Assert.That(actualColor.b, Is.EqualTo(expectedColor.b).Within(0.001f));
            Assert.That(actualColor.a, Is.EqualTo(expectedColor.a).Within(0.001f));
            Assert.That(runtimeMaterial.GetTexture("_ContractTexture"), Is.SameAs(texture));

            scope.Dispose();
            Assert.That(renderer.sharedMaterial, Is.SameAs(sharedMaterial));
        }

        [TestCaseSource(nameof(ShaderPropertyTypeMismatchCases))]
        public void RendererAndMaterialOutputsRejectMissingTargetsAndWrongShaderPropertyTypes(Type outputType, string wrongPropertyName)
        {
            var missingTarget = Activator.CreateInstance(outputType);
            SetField(missingTarget, "propertyName", wrongPropertyName);
            var missingTargetError = InitializeError(missingTarget, null);
            Assert.That(missingTargetError.Message, Does.Contain("target is not assigned"), outputType.Name);

            var shader = Shader.Find("Hidden/MondmadeVJ0/OutputContractProperties");
            Assert.That(shader, Is.Not.Null, "The rendering output test shader must be imported.");
            var material = Own(new Material(shader));
            var rendererObject = Own(new GameObject("Wrong Shader Property Type Output Target"));
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var output = Activator.CreateInstance(outputType);

            if (IsRendererOutput(outputType))
            {
                ConfigureRendererOutput(output, renderer, wrongPropertyName);
            }
            else
            {
                ConfigureMaterialOutput(output, renderer, wrongPropertyName);
            }

            var wrongType = InitializeError(output, null);
            Assert.That(wrongType.Message, Does.Contain("expected"), outputType.Name);
        }

        [Test]
        public void ValidationReportsWritersTargetingTheSameRendererSlotAndProperty()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The built-in Standard shader is unavailable in this Editor.");
            var shared = Own(new Material(shader));
            var rendererObject = Own(new GameObject("Conflicting Renderer"));
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = shared;

            var rendererRoute = new FloatRoute();
            rendererRoute.Configure("Renderer writer", new ConstantFloatSource());
            var rendererOutput = new RendererFloatOutput();
            SetField(rendererOutput, "target", renderer);
            SetField(rendererOutput, "propertyName", "_Glossiness");
            rendererRoute.Outputs.Add(rendererOutput);
            host.AddRoute(rendererRoute);

            var materialRoute = new FloatRoute();
            materialRoute.Configure("Material writer", new ConstantFloatSource());
            var materialOutput = new MaterialFloatOutput();
            ConfigureMaterialOutput(materialOutput, renderer, "_Glossiness");
            materialRoute.Outputs.Add(materialOutput);
            host.AddRoute(materialRoute);

            var issues = MondmadeVJ0Validation.Validate(host);

            Assert.That(issues.Any(issue => issue.Contains("conflicts with") && issue.Contains("slot 0 property '_Glossiness'")), Is.True);
        }

        [Test]
        public void ValidationReportsWritersTargetingTheSameVolumeParameter()
        {
            var profile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            profile.Add<RenderingOutputTestComponent>(overrides: true);
            var volumeObject = Own(new GameObject("Conflicting Volume"));
            var volume = volumeObject.AddComponent<Volume>();
            volume.sharedProfile = profile;

            var firstRoute = new FloatRoute();
            firstRoute.Configure("Volume writer A", new ConstantFloatSource());
            var first = new VolumeFloatOutput();
            ConfigureVolumeOutput(first, volume, nameof(RenderingOutputTestComponent.amount), VolumeOverrideMode.Always);
            firstRoute.Outputs.Add(first);
            host.AddRoute(firstRoute);

            var secondRoute = new FloatRoute();
            secondRoute.Configure("Volume writer B", new ConstantFloatSource());
            var second = new VolumeFloatOutput();
            ConfigureVolumeOutput(second, volume, nameof(RenderingOutputTestComponent.amount), VolumeOverrideMode.Always);
            secondRoute.Outputs.Add(second);
            host.AddRoute(secondRoute);

            var issues = MondmadeVJ0Validation.Validate(host);

            Assert.That(issues.Any(issue => issue.Contains("conflicts with") && issue.Contains("RenderingOutputTestComponent.amount")), Is.True);
        }

        [Test]
        public void TransformVectorAndGenericWritersConflictWithAxesButDifferentAxesAreAllowed()
        {
            var transform = new GameObject("Transform Output Target").transform;
            cleanup.Add(transform.gameObject);

            var axisXRoute = new FloatRoute();
            axisXRoute.Configure("Axis X", new ConstantFloatSource());
            var axisX = new TransformPositionAxisFloatOutput();
            SetField(axisX, "target", transform);
            SetField(axisX, "axis", TransformAxis.X);
            axisXRoute.Outputs.Add(axisX);
            host.AddRoute(axisXRoute);

            var axisYRoute = new FloatRoute();
            axisYRoute.Configure("Axis Y", new ConstantFloatSource());
            var axisY = new TransformPositionAxisFloatOutput();
            SetField(axisY, "target", transform);
            SetField(axisY, "axis", TransformAxis.Y);
            axisYRoute.Outputs.Add(axisY);
            host.AddRoute(axisYRoute);

            Assert.That(MondmadeVJ0Validation.Validate(host).Any(issue => issue.Contains("conflicts with")), Is.False);

            var vectorRoute = new Vector3Route();
            vectorRoute.Configure("Full vector", new ConstantVector3Source());
            var vector = new TransformPositionVector3Output();
            SetField(vector, "target", transform);
            vectorRoute.Outputs.Add(vector);
            host.AddRoute(vectorRoute);

            var genericRoute = new Vector3Route();
            genericRoute.Configure("Generic vector", new ConstantVector3Source());
            var generic = new ComponentMemberVector3Output();
            SetField(generic, "target", transform);
            SetField(generic, "memberName", "localPosition");
            genericRoute.Outputs.Add(generic);
            host.AddRoute(genericRoute);

            var issues = MondmadeVJ0Validation.Validate(host);

            Assert.That(issues.Any(issue => issue.Contains("Full vector") && issue.Contains("Axis X") && issue.Contains("localPosition")), Is.True);
            Assert.That(issues.Any(issue => issue.Contains("Full vector") && issue.Contains("Axis Y") && issue.Contains("localPosition")), Is.True);
            Assert.That(issues.Any(issue => issue.Contains("Generic vector") && issue.Contains("Axis X") && issue.Contains("localPosition")), Is.True);
        }

        private RouteContext Context(BindingScope scope)
            => new RouteContext(runtime, host, new RuntimeStateStore(), scope, Guid.NewGuid().ToString("N"));

        private BindingScope NewScope()
        {
            var scope = new BindingScope();
            scopes.Add(scope);
            return scope;
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            cleanup.Add(value);
            return value;
        }

        private static void ConfigureMaterialOutput(object output, Renderer renderer, string propertyName)
        {
            SetField(output, "renderer", renderer);
            SetField(output, "propertyName", propertyName);
            SetField(output, "materialIndex", 0);
        }

        private static void ConfigureRendererOutput(object output, Renderer renderer, string propertyName)
        {
            SetField(output, "target", renderer);
            SetField(output, "propertyName", propertyName);
            SetField(output, "materialIndex", 0);
        }

        private static void ConfigureVolumeOutput(object output, Volume volume, string parameterName, VolumeOverrideMode mode)
        {
            SetField(output, "target", volume);
            SetField(output, "componentTypeName", typeof(RenderingOutputTestComponent).AssemblyQualifiedName);
            SetField(output, "parameterName", parameterName);
            SetField(output, "overrideMode", mode);
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

        private static bool IsRendererOutput(Type outputType) =>
            outputType == typeof(RendererFloatOutput)
            || outputType == typeof(RendererIntOutput)
            || outputType == typeof(RendererVectorOutput)
            || outputType == typeof(RendererColorOutput)
            || outputType == typeof(RendererTextureOutput)
            || outputType == typeof(RendererTextureIndexOutput);

        private static InvalidOperationException InitializeError(object output, RouteContext context)
        {
            var initialize = output.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(initialize, Is.Not.Null, output.GetType().Name + " must expose Initialize(RouteContext).");
            var exception = Assert.Throws<TargetInvocationException>(() => initialize.Invoke(output, new object[] { context }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>(), output.GetType().Name);
            return (InvalidOperationException)exception.InnerException;
        }
    }
}
