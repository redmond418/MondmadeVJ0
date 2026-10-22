using System;
using UnityEngine;

namespace MondmadeVJ0
{
    [Serializable]
    public abstract class AnimatorParameterOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] protected Animator target;
        [SerializeField] protected string parameterName;
        [NonSerialized] protected int parameterHash;
        protected abstract AnimatorControllerParameterType ParameterType { get; }
        protected void Validate(Animator animator, string name, AnimatorControllerParameterType expected)
        {
            if (animator == null) throw new InvalidOperationException("Animator target is not assigned.");
            if (animator.runtimeAnimatorController == null) throw new InvalidOperationException("Animator has no RuntimeAnimatorController.");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Animator parameter name is empty.");
            var hash = Animator.StringToHash(name);
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash && parameter.type == expected) { parameterHash = hash; return; }
            throw new InvalidOperationException($"Animator parameter '{name}' is missing or is not {expected}.");
        }
        public void Initialize(RouteContext context) => Validate(target, parameterName, ParameterType);
        public abstract void Write(T value);
    }

    [Serializable]
    public sealed class AnimatorFloatOutput : AnimatorParameterOutput<float>
    {
        protected override AnimatorControllerParameterType ParameterType => AnimatorControllerParameterType.Float;
        public override void Write(float value) => target.SetFloat(parameterHash, value);
    }

    [Serializable]
    public sealed class AnimatorIntOutput : AnimatorParameterOutput<int>
    {
        protected override AnimatorControllerParameterType ParameterType => AnimatorControllerParameterType.Int;
        public override void Write(int value) => target.SetInteger(parameterHash, value);
    }

    [Serializable]
    public sealed class AnimatorBoolOutput : AnimatorParameterOutput<bool>
    {
        protected override AnimatorControllerParameterType ParameterType => AnimatorControllerParameterType.Bool;
        public override void Write(bool value) => target.SetBool(parameterHash, value);
    }

    [Serializable]
    public sealed class AnimatorTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private Animator target;
        [SerializeField] private string parameterName;
        [NonSerialized] private int parameterHash;
        public void Initialize(RouteContext context)
        {
            if (target == null || target.runtimeAnimatorController == null) throw new InvalidOperationException("Animator target/controller is not assigned.");
            if (string.IsNullOrWhiteSpace(parameterName)) throw new InvalidOperationException("Animator trigger name is empty.");
            parameterHash = Animator.StringToHash(parameterName);
            foreach (var parameter in target.parameters)
                if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Trigger) return;
            throw new InvalidOperationException($"Animator Trigger '{parameterName}' is missing.");
        }
        public void Invoke() => target.SetTrigger(parameterHash);
    }

    [Serializable]
    public sealed class AnimatorLayerWeightFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private Animator target;
        [SerializeField] private string layerName;
        [SerializeField] private int layerIndex;
        [NonSerialized] private int resolvedLayer;
        public void Initialize(RouteContext context)
        {
            if (target == null || target.runtimeAnimatorController == null) throw new InvalidOperationException("Animator target/controller is not assigned.");
            resolvedLayer = string.IsNullOrEmpty(layerName) ? layerIndex : target.GetLayerIndex(layerName);
            if (resolvedLayer < 0 || resolvedLayer >= target.layerCount) throw new InvalidOperationException($"Animator layer '{layerName}' does not exist.");
        }
        public void Write(float value) => target.SetLayerWeight(resolvedLayer, Mathf.Clamp01(value));
    }

    [Serializable]
    public sealed class AnimatorStateTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private Animator target;
        [SerializeField] private string stateName;
        [SerializeField] private string layerName;
        [SerializeField] private int layerIndex;
        [SerializeField] private bool crossFade = true;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.1f;
        [SerializeField, Range(0f, 1f)] private float normalizedTime;
        [NonSerialized] private int stateHash;
        [NonSerialized] private int resolvedLayer;
        public void Initialize(RouteContext context)
        {
            if (target == null || target.runtimeAnimatorController == null) throw new InvalidOperationException("Animator target/controller is not assigned.");
            if (string.IsNullOrWhiteSpace(stateName)) throw new InvalidOperationException("Animator state name is empty.");
            if (crossFadeSeconds < 0f || float.IsNaN(crossFadeSeconds) || float.IsInfinity(crossFadeSeconds)) throw new InvalidOperationException("Crossfade duration must be finite and non-negative.");
            resolvedLayer = string.IsNullOrEmpty(layerName) ? layerIndex : target.GetLayerIndex(layerName);
            if (resolvedLayer < 0 || resolvedLayer >= target.layerCount) throw new InvalidOperationException($"Animator layer '{layerName}' does not exist.");
            stateHash = Animator.StringToHash(stateName);
        }
        public void Invoke()
        {
            if (crossFade) target.CrossFade(stateHash, crossFadeSeconds, resolvedLayer, normalizedTime);
            else target.Play(stateHash, resolvedLayer, normalizedTime);
        }
    }
}
