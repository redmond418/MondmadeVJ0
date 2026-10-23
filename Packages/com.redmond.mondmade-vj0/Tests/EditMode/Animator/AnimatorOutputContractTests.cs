using System;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MondmadeVJ0.Tests.Animator
{
    public sealed class AnimatorOutputContractTests
    {
        private static readonly object[] ParameterWrongTypeCases =
        {
            new object[] { typeof(AnimatorFloatOutput), "mode" },
            new object[] { typeof(AnimatorIntOutput), "speed" },
            new object[] { typeof(AnimatorBoolOutput), "fire" },
            new object[] { typeof(AnimatorTriggerOutput), "enabled" },
        };

        private GameObject targetObject;
        private UnityEngine.Animator animator;
        private AnimatorController controller;
        private string controllerPath;

        [SetUp]
        public void SetUp()
        {
            targetObject = new GameObject("Animator Output Contract Target");
            animator = targetObject.AddComponent<UnityEngine.Animator>();
            controllerPath = AssetDatabase.GenerateUniqueAssetPath("Assets/MondmadeVJ0.AnimatorOutputTests.controller");
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("mode", AnimatorControllerParameterType.Int);
            controller.AddParameter("enabled", AnimatorControllerParameterType.Bool);
            controller.AddParameter("fire", AnimatorControllerParameterType.Trigger);
            animator.runtimeAnimatorController = controller;
        }

        [TearDown]
        public void TearDown()
        {
            if (targetObject != null) UnityEngine.Object.DestroyImmediate(targetObject);
            if (!string.IsNullOrEmpty(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);
        }

        [Test]
        public void ValueOutputsWriteFloatIntAndBoolParameters()
        {
            var floatOutput = new AnimatorFloatOutput();
            SetField(floatOutput, "target", animator);
            SetField(floatOutput, "parameterName", "speed");
            floatOutput.Initialize(null);
            floatOutput.Write(0.75f);

            var intOutput = new AnimatorIntOutput();
            SetField(intOutput, "target", animator);
            SetField(intOutput, "parameterName", "mode");
            intOutput.Initialize(null);
            intOutput.Write(3);

            var boolOutput = new AnimatorBoolOutput();
            SetField(boolOutput, "target", animator);
            SetField(boolOutput, "parameterName", "enabled");
            boolOutput.Initialize(null);
            boolOutput.Write(true);

            Assert.That(animator.GetFloat(UnityEngine.Animator.StringToHash("speed")), Is.EqualTo(0.75f));
            Assert.That(animator.GetInteger(UnityEngine.Animator.StringToHash("mode")), Is.EqualTo(3));
            Assert.That(animator.GetBool(UnityEngine.Animator.StringToHash("enabled")), Is.True);
        }

        [Test]
        public void ValueOutputRejectsMissingAndWrongTypeParameters()
        {
            var missing = new AnimatorFloatOutput();
            SetField(missing, "target", animator);
            SetField(missing, "parameterName", "missing");
            Assert.Throws<InvalidOperationException>(() => missing.Initialize(null));

            var wrongType = new AnimatorFloatOutput();
            SetField(wrongType, "target", animator);
            SetField(wrongType, "parameterName", "enabled");
            Assert.Throws<InvalidOperationException>(() => wrongType.Initialize(null));
        }

        [TestCaseSource(nameof(ParameterWrongTypeCases))]
        public void AnimatorParameterOutputsRejectMissingTargetsAndWrongParameterTypes(Type outputType, string wrongParameter)
        {
            var output = Activator.CreateInstance(outputType);
            var missingTarget = InitializeError(output);
            Assert.That(missingTarget.Message, Does.Contain("not assigned"), outputType.Name);

            SetField(output, "target", animator);
            SetField(output, "parameterName", wrongParameter);
            var wrongType = InitializeError(output);
            Assert.That(wrongType.Message, Does.Contain("missing").Or.Contain("is not"), outputType.Name);
        }

        [Test]
        public void AnimatorLayerAndStateOutputsRejectMissingTargets()
        {
            Assert.Throws<InvalidOperationException>(() => new AnimatorLayerWeightFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new AnimatorStateTriggerOutput().Initialize(null));
        }

        [Test]
        public void TriggerOutputTransitionsToConfiguredState()
        {
            var layer = controller.layers[0];
            var idle = layer.stateMachine.AddState("Idle");
            var fired = layer.stateMachine.AddState("Fired");
            layer.stateMachine.defaultState = idle;
            var transition = idle.AddTransition(fired);
            transition.AddCondition(AnimatorConditionMode.If, 0f, "fire");
            transition.duration = 0f;
            controller.layers = new[] { layer };
            animator.Rebind();
            animator.Update(0f);

            var output = new AnimatorTriggerOutput();
            SetField(output, "target", animator);
            SetField(output, "parameterName", "fire");
            output.Initialize(null);
            output.Invoke();
            animator.Update(0.1f);

            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(UnityEngine.Animator.StringToHash("Fired")));
        }

        [Test]
        public void TriggerOutputRejectsNonTriggerParameter()
        {
            var output = new AnimatorTriggerOutput();
            SetField(output, "target", animator);
            SetField(output, "parameterName", "speed");
            Assert.Throws<InvalidOperationException>(() => output.Initialize(null));
        }

        [Test]
        public void LayerWeightOutputResolvesNameAndClampsValue()
        {
            var output = new AnimatorLayerWeightFloatOutput();
            SetField(output, "target", animator);
            var baseLayer = controller.layers[0].name;
            SetField(output, "layerName", baseLayer);
            output.Initialize(null);
            output.Write(1.5f);

            var layerIndex = animator.GetLayerIndex(baseLayer);
            Assert.That(layerIndex, Is.EqualTo(0));
            Assert.That(animator.GetLayerWeight(layerIndex), Is.EqualTo(1f));
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

        private static InvalidOperationException InitializeError(object output)
        {
            var initialize = output.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(initialize, Is.Not.Null, output.GetType().Name + " must expose Initialize(RouteContext).");
            var exception = Assert.Throws<TargetInvocationException>(() => initialize.Invoke(output, new object[] { null }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>(), output.GetType().Name);
            return (InvalidOperationException)exception.InnerException;
        }
    }
}
