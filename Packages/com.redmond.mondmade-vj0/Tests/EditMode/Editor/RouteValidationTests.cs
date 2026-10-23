using System;
using System.Linq;
using System.Reflection;
using MondmadeVJ0.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class RouteValidationTests
    {
        private GameObject signalAObject;
        private GameObject signalBObject;
        private GameObject hostAObject;
        private GameObject hostBObject;
        private SharedFloatSignal signalA;
        private SharedFloatSignal signalB;
        private RouteHost hostA;
        private RouteHost hostB;
        private UnityEngine.Object extraAsset;
        private string extraAssetPath;

        [SetUp]
        public void SetUp()
        {
            signalAObject = new GameObject("Signal A");
            signalBObject = new GameObject("Signal B");
            signalA = signalAObject.AddComponent<SharedFloatSignal>();
            signalB = signalBObject.AddComponent<SharedFloatSignal>();
            hostAObject = new GameObject("Route host A");
            hostBObject = new GameObject("Route host B");
            hostA = hostAObject.AddComponent<RouteHost>();
            hostB = hostBObject.AddComponent<RouteHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (hostAObject != null) UnityEngine.Object.DestroyImmediate(hostAObject);
            if (hostBObject != null) UnityEngine.Object.DestroyImmediate(hostBObject);
            if (signalAObject != null) UnityEngine.Object.DestroyImmediate(signalAObject);
            if (signalBObject != null) UnityEngine.Object.DestroyImmediate(signalBObject);
            if (!string.IsNullOrEmpty(extraAssetPath))
            {
                UnityEditor.AssetDatabase.DeleteAsset(extraAssetPath);
                extraAsset = null;
                extraAssetPath = null;
            }
            if (extraAsset != null) UnityEngine.Object.DestroyImmediate(extraAsset);
        }

        [Test]
        public void SharedSignalCycleAcrossRouteHostsIsReported()
        {
            hostA.AddRoute(CreateSignalRoute("A to B", signalA, signalB));
            hostB.AddRoute(CreateSignalRoute("B to A", signalB, signalA));

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("feedback cycle")), Is.True);
        }

        [Test]
        public void MissingCompositeInputIsReportedBeforePlay()
        {
            var source = new FloatBinarySource();
            SetField(source, "left", new ConstantFloatSource { Value = 1f });
            var route = new FloatRoute();
            route.Configure("incomplete composite", source);
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("connected node 'right' is missing")), Is.True);
        }

        [Test]
        public void MissingOutputIsReportedBeforePlay()
        {
            var route = new FloatRoute();
            route.Configure("output required", new ConstantFloatSource());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("output required") && issue.Contains("no Output is assigned")), Is.True);
        }

        [Test]
        public void EveryOutputNodeIsOfferedOnlyForItsDeclaredValueOrTriggerContract()
        {
            var allTypesMethod = typeof(RouteHostEditor).GetMethod("AllTypes", BindingFlags.Static | BindingFlags.NonPublic);
            var nodeTypesMethod = typeof(RouteHostEditor).GetMethod("NodeTypes", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(allTypesMethod, Is.Not.Null);
            Assert.That(nodeTypesMethod, Is.Not.Null);

            var contracts = new[]
            {
                typeof(IValueOutput<float>),
                typeof(IValueOutput<int>),
                typeof(IValueOutput<bool>),
                typeof(IValueOutput<Vector2>),
                typeof(IValueOutput<Vector3>),
                typeof(IValueOutput<Vector4>),
                typeof(IValueOutput<Color>),
                typeof(ITriggerOutput),
            };
            var allTypes = (System.Collections.Generic.IEnumerable<Type>)allTypesMethod.Invoke(null, null);
            var outputNodes = allTypes
                .Where(type => typeof(RouteNode).IsAssignableFrom(type)
                    && !type.IsAbstract
                    && contracts.Any(contract => contract.IsAssignableFrom(type)))
                .ToArray();
            Assert.That(outputNodes, Is.Not.Empty, "The output type catalog must be discoverable.");

            foreach (var contract in contracts)
            {
                var offered = (System.Collections.Generic.IReadOnlyList<Type>)nodeTypesMethod.Invoke(null, new object[] { contract });
                foreach (var outputNode in outputNodes)
                {
                    Assert.That(offered.Contains(outputNode), Is.EqualTo(contract.IsAssignableFrom(outputNode)),
                        outputNode.FullName + " must only appear in the picker for " + contract.Name + ".");
                }
            }
        }

        [Test]
        public void MissingSourceIsReportedBeforePlay()
        {
            var route = new FloatRoute();
            route.Configure("source required", null);
            route.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("source required") && issue.Contains("Source is not assigned")), Is.True);
        }

        [Test]
        public void DuplicateStableIdsAndInvalidRangesAreReportedBeforePlay()
        {
            var first = new FloatRoute();
            first.Configure("first route", new ConstantFloatSource());
            first.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(first);

            var second = new FloatRoute();
            var clamp = new ClampFloatModifier();
            SetField(clamp, "minimum", 2f);
            SetField(clamp, "maximum", 1f);
            second.Configure("invalid range route", new ConstantFloatSource());
            second.Modifiers.Add(clamp);
            second.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(second);
            SetField(second, "id", first.Id);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("duplicate route id")), Is.True);
            Assert.That(issues.Exists(issue => issue.Contains("invalid range route") && issue.Contains("minimum must not exceed maximum")), Is.True);
        }

        [Test]
        public void DuplicateRouteIdsAcrossHostsAreReportedBeforePlay()
        {
            var routeA = new FloatRoute();
            routeA.Configure("host A route", new ConstantFloatSource());
            routeA.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(routeA);
            var routeB = new FloatRoute();
            routeB.Configure("host B route", new ConstantFloatSource());
            routeB.Outputs.Add(new DebugFloatOutput());
            hostB.AddRoute(routeB);
            SetField(routeB, "id", routeA.Id);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("duplicate route id") && issue.Contains("host B route")), Is.True);
        }

        [Test]
        public void DuplicateNodeIdsWithinRouteAreReportedBeforePlay()
        {
            var source = new ConstantFloatSource();
            var modifier = new AddFloatModifier();
            var route = new FloatRoute();
            route.Configure("duplicate node ids", source);
            route.Modifiers.Add(modifier);
            route.Outputs.Add(new DebugFloatOutput());
            SetField(modifier, "nodeId", source.NodeId);
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("duplicate node id")), Is.True);
        }

        [Test]
        public void MissingUnityTargetIsReportedWithRouteAndNodeContext()
        {
            var route = new BoolRoute();
            route.Configure("missing renderer target", new ConstantBoolSource());
            route.Outputs.Add(new RendererEnabledBoolOutput());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("missing renderer target") && issue.Contains("required reference 'target' is missing")), Is.True);
        }

        [Test]
        public void MissingAndWrongTypeShaderPropertiesAreReportedBeforePlay()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) Assert.Ignore("The built-in Standard shader is unavailable in this Editor.");
            var material = new Material(shader);
            extraAsset = material;
            var rendererObject = new GameObject("Shader validation target");
            var renderer = rendererObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            try
            {
                var rendererOutputType = FindLoadedType("MondmadeVJ0.RendererFloatOutput");
                var materialOutputType = FindLoadedType("MondmadeVJ0.MaterialFloatOutput");
                if (rendererOutputType == null || materialOutputType == null)
                    Assert.Ignore("The Rendering integration is not installed in this project.");

                var wrongTypeOutput = (IValueOutput<float>)Activator.CreateInstance(rendererOutputType);
                SetField(wrongTypeOutput, "target", renderer);
                SetField(wrongTypeOutput, "propertyName", "_Color");
                var wrongTypeRoute = new FloatRoute();
                wrongTypeRoute.Configure("shader type mismatch", new ConstantFloatSource());
                wrongTypeRoute.Outputs.Add(wrongTypeOutput);
                hostA.AddRoute(wrongTypeRoute);

                var missingOutput = (IValueOutput<float>)Activator.CreateInstance(materialOutputType);
                var materialOutputModeType = FindLoadedType("MondmadeVJ0.MaterialOutputMode");
                if (materialOutputModeType != null)
                    SetField(missingOutput, "outputMode", Enum.Parse(materialOutputModeType, "SharedAsset"));
                SetField(missingOutput, "target", material);
                SetField(missingOutput, "propertyName", "_MondmadeMissingProperty");
                var missingRoute = new FloatRoute();
                missingRoute.Configure("missing shader property", new ConstantFloatSource());
                missingRoute.Outputs.Add(missingOutput);
                hostA.AddRoute(missingRoute);

                var issues = MondmadeVJ0Validation.Validate(hostA);

                Assert.That(issues.Exists(issue => issue.Contains("shader type mismatch") && issue.Contains("expected Float")), Is.True);
                Assert.That(issues.Exists(issue => issue.Contains("missing shader property") && issue.Contains("does not exist")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(rendererObject); }
        }

        [Test]
        public void MissingAndWrongTypeAnimatorParametersAreReportedBeforePlay()
        {
            var outputType = FindLoadedType("MondmadeVJ0.AnimatorFloatOutput");
            var controllerType = FindLoadedType("UnityEditor.Animations.AnimatorController");
            if (outputType == null || controllerType == null)
                Assert.Ignore("The Animator integration or Editor controller API is unavailable.");

            var parameterType = typeof(AnimatorControllerParameterType);
            var addParameter = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "AddParameter" && method.GetParameters().Length == 2
                    && method.GetParameters()[0].ParameterType == typeof(string) && method.GetParameters()[1].ParameterType == parameterType);
            if (addParameter == null) Assert.Ignore("AnimatorController.AddParameter is unavailable in this Editor.");

            extraAssetPath = "Assets/MondmadeVJ0TempA22.controller";
            UnityEditor.AssetDatabase.DeleteAsset(extraAssetPath);
            var createController = controllerType.GetMethod("CreateAnimatorControllerAtPath", BindingFlags.Static | BindingFlags.Public,
                binder: null, types: new[] { typeof(string) }, modifiers: null);
            if (createController == null) Assert.Ignore("AnimatorController.CreateAnimatorControllerAtPath is unavailable in this Editor.");
            extraAsset = createController.Invoke(null, new object[] { extraAssetPath }) as UnityEngine.Object;
            if (extraAsset == null) Assert.Ignore("Unity could not create a temporary AnimatorController asset.");
            addParameter.Invoke(extraAsset, new[] { "ExistingBool", Enum.ToObject(parameterType, (int)AnimatorControllerParameterType.Bool) });
            var targetObject = new GameObject("Animator validation target");
            try
            {
                var animator = targetObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = (RuntimeAnimatorController)extraAsset;
                var output = (IValueOutput<float>)Activator.CreateInstance(outputType);
                SetField(output, "target", animator);
                SetField(output, "parameterName", "ExistingBool");
                var missingOutput = (IValueOutput<float>)Activator.CreateInstance(outputType);
                SetField(missingOutput, "target", animator);
                SetField(missingOutput, "parameterName", "DefinitelyMissing");
                var route = new FloatRoute();
                route.Configure("animator parameter type mismatch", new ConstantFloatSource());
                route.Outputs.Add(output);
                route.Outputs.Add(missingOutput);
                hostA.AddRoute(route);

                var issues = MondmadeVJ0Validation.Validate(hostA);

                Assert.That(issues.Exists(issue => issue.Contains("animator parameter type mismatch")
                    && issue.Contains("ExistingBool") && issue.Contains("not Float")), Is.True);
                Assert.That(issues.Exists(issue => issue.Contains("animator parameter type mismatch")
                    && issue.Contains("DefinitelyMissing") && issue.Contains("missing or is not Float")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(targetObject); }
        }

        [Test]
        public void MissingVfxPropertyIsReportedBeforePlayWhenIntegrationIsInstalled()
        {
            var outputType = FindLoadedType("MondmadeVJ0.VFXFloatOutput");
            if (outputType == null) Assert.Ignore("The VFX Graph integration is not installed in this project.");
            var targetField = FindField(outputType, "target");
            var targetObject = new GameObject("VFX validation target");
            try
            {
                var target = targetObject.AddComponent(targetField.FieldType);
                if (target == null) Assert.Ignore("Unity could not create a VisualEffect component in this Editor.");
                if (target.GetType().GetMethod("HasFloat", new[] { typeof(int) }) == null)
                    Assert.Ignore("VisualEffect.HasFloat(int) is unavailable in this package version.");
                var output = (IValueOutput<float>)Activator.CreateInstance(outputType);
                SetField(output, "target", target);
                SetField(output, "propertyName", "MondmadeDefinitelyMissing");
                var route = new FloatRoute();
                route.Configure("missing VFX property", new ConstantFloatSource());
                route.Outputs.Add(output);
                hostA.AddRoute(route);

                var issues = MondmadeVJ0Validation.Validate(hostA);

                Assert.That(issues.Exists(issue => issue.Contains("missing VFX property") && issue.Contains("VFX property")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(targetObject); }
        }

        [Test]
        public void MissingMidiControlAndIncompatibleControlTypeAreReportedBeforePlay()
        {
            var profileType = FindLoadedType("MondmadeVJ0.MidiSurfaceProfile");
            var surfaceType = FindLoadedType("MondmadeVJ0.MidiSurfaceInstance");
            var hubType = FindLoadedType("MondmadeVJ0.MidiHub");
            var missingControlSourceType = FindLoadedType("MondmadeVJ0.MidiButtonBoolSource");
            var mismatchSourceType = FindLoadedType("MondmadeVJ0.MidiAbsoluteFloatSource");
            var referenceType = FindLoadedType("MondmadeVJ0.MidiControlReference");
            if (profileType == null || surfaceType == null || hubType == null || missingControlSourceType == null
                || mismatchSourceType == null || referenceType == null)
                Assert.Ignore("The MIDI integration is not installed in this project.");

            var createProfile = profileType.GetMethod("CreateApcMiniMk2Profile", BindingFlags.Public | BindingFlags.Static);
            var profile = (UnityEngine.Object)createProfile.Invoke(null, null);
            extraAsset = profile;
            var surface = Activator.CreateInstance(surfaceType);
            var surfaceId = surfaceType.GetProperty("InstanceId").GetValue(surface) as string;
            var backendType = FindLoadedType("MondmadeVJ0.MidiInputBackend");
            var configure = surfaceType.GetMethod("Configure");
            configure.Invoke(surface, new[] { profile, "APC mini mk2", (object)0, Enum.ToObject(backendType, 0), -1, -1 });

            hostAObject.SetActive(false);
            var hub = hostAObject.AddComponent(hubType);
            var surfaces = Array.CreateInstance(surfaceType, 1);
            surfaces.SetValue(surface, 0);
            SetField(hub, "surfaces", surfaces);
            var runtime = hostAObject.AddComponent<MondmadeVJ0Runtime>();
            SetField(runtime, "modules", new MonoBehaviour[] { (MonoBehaviour)hub });
            SetField(runtime, "routeHosts", new[] { hostA });

            AddMidiValidationRoute(missingControlSourceType, referenceType, surfaceId, "missing_control", "missing MIDI control");
            AddMidiValidationRoute(mismatchSourceType, referenceType, surfaceId, "pad_0_0", "MIDI control type mismatch");

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("missing MIDI control") && issue.Contains("does not exist")), Is.True);
            Assert.That(issues.Exists(issue => issue.Contains("MIDI control type mismatch") && issue.Contains("incompatible Control Type")), Is.True);
        }

        [Test]
        public void MissingAndWrongTypeGenericComponentMembersAreReportedBeforePlay()
        {
            var targetObject = new GameObject("Component member validation target");
            try
            {
                var target = targetObject.AddComponent<AudioSource>();
                var missingOutput = new ComponentMemberFloatOutput();
                SetField(missingOutput, "target", target);
                SetField(missingOutput, "memberName", "MondmadeDefinitelyMissing");
                var missingRoute = new FloatRoute();
                missingRoute.Configure("missing component member", new ConstantFloatSource());
                missingRoute.Outputs.Add(missingOutput);
                hostA.AddRoute(missingRoute);

                var wrongTypeOutput = new ComponentMemberIntOutput();
                SetField(wrongTypeOutput, "target", target);
                SetField(wrongTypeOutput, "memberName", "volume");
                var wrongTypeRoute = new IntRoute();
                wrongTypeRoute.Configure("component member type mismatch", new ConstantIntSource());
                wrongTypeRoute.Outputs.Add(wrongTypeOutput);
                hostA.AddRoute(wrongTypeRoute);

                var issues = MondmadeVJ0Validation.Validate(hostA);

                Assert.That(issues.Exists(issue => issue.Contains("missing component member") && issue.Contains("not a public writable Single member")), Is.True);
                Assert.That(issues.Exists(issue => issue.Contains("component member type mismatch") && issue.Contains("not a public writable Int32 member")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(targetObject); }
        }

        private void AddMidiValidationRoute(Type sourceType, Type referenceType, string surfaceId, string controlId, string routeName)
        {
            var source = Activator.CreateInstance(sourceType);
            var reference = Activator.CreateInstance(referenceType, new object[] { surfaceId, controlId });
            SetField(source, "control", reference);
            if (source is IValueSource<bool> boolSource)
            {
                var route = new BoolRoute();
                route.Configure(routeName, boolSource);
                route.Outputs.Add(new ValidationBoolOutput());
                hostA.AddRoute(route);
            }
            else if (source is IValueSource<float> floatSource)
            {
                var route = new FloatRoute();
                route.Configure(routeName, floatSource);
                route.Outputs.Add(new ValidationFloatOutput());
                hostA.AddRoute(route);
            }
        }

        [Test]
        public void NonFiniteModifierConfigurationIsReportedBeforePlay()
        {
            var modifier = new DeadZoneFloatModifier();
            SetField(modifier, "threshold", float.NaN);
            var route = new FloatRoute();
            route.Configure("non-finite modifier", new ConstantFloatSource());
            route.Modifiers.Add(modifier);
            route.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues.Exists(issue => issue.Contains("non-finite modifier") && issue.Contains("'threshold' must be finite")), Is.True);
        }

        [Test]
        public void ManagedReferenceClonePreservesUnityObjectReferences()
        {
            var target = new GameObject("Clone target");
            try
            {
                var renderer = target.AddComponent<MeshRenderer>();
                var output = new RendererEnabledBoolOutput();
                SetField(output, "target", renderer);
                var cloneMethod = typeof(RouteHostEditor).GetMethod("CloneManagedReference", BindingFlags.Static | BindingFlags.NonPublic);
                var clone = (RendererEnabledBoolOutput)cloneMethod.Invoke(null, new object[] { output });
                var copiedTarget = (Renderer)typeof(RendererEnabledBoolOutput).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clone);

                Assert.That(copiedTarget, Is.SameAs(renderer));
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
        }

        [Test]
        public void SmoothDampUnboundedMaxSpeedIsValid()
        {
            var route = new FloatRoute();
            route.Configure("Unbounded SmoothDamp", new ConstantFloatSource());
            route.Modifiers.Add(new SmoothDampFloatModifier());
            route.Outputs.Add(new DebugFloatOutput());
            hostA.AddRoute(route);

            var issues = MondmadeVJ0Validation.Validate(hostA);

            Assert.That(issues, Is.Empty);
        }

        private static FloatRoute CreateSignalRoute(string name, SharedFloatSignal input, SharedFloatSignal outputSignal)
        {
            var route = new FloatRoute();
            route.Configure(name, new SignalFloatSource { Signal = input });
            var output = new SignalFloatOutput();
            SetField(output, "signal", outputSignal);
            route.Outputs.Add(output);
            return route;
        }

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new System.MissingFieldException(target.GetType().FullName, name);
        }

        private static Type FindLoadedType(string fullName)
        {
            var animatorEditorType = Type.GetType(fullName + ", UnityEditor.AnimationModule", throwOnError: false);
            return animatorEditorType ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false)).FirstOrDefault(type => type != null);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }

        private sealed class ValidationFloatOutput : IValueOutput<float>
        {
            public void Initialize(RouteContext context) { }
            public void Write(float value) { }
        }

        private sealed class ValidationBoolOutput : IValueOutput<bool>
        {
            public void Initialize(RouteContext context) { }
            public void Write(bool value) { }
        }
    }
}
