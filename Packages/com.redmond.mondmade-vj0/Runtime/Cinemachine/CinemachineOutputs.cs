using System;
using Unity.Cinemachine;
using UnityEngine;

namespace MondmadeVJ0
{
    public enum CinemachineSwitchMode { Priority, GameObjectActive }

    [Serializable]
    public sealed class CinemachineCameraIndexOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private CinemachineCamera[] cameras = Array.Empty<CinemachineCamera>();
        [SerializeField] private CinemachineSwitchMode switchMode = CinemachineSwitchMode.Priority;
        [SerializeField] private int activePriority = 100;
        [SerializeField] private int inactivePriority;
        public void Initialize(RouteContext context)
        {
            if (cameras == null || cameras.Length == 0) throw new InvalidOperationException("Cinemachine camera list is empty.");
            for (var i = 0; i < cameras.Length; i++) if (cameras[i] == null) throw new InvalidOperationException($"Cinemachine camera {i} is not assigned.");
        }
        public void Write(int value)
        {
            var index = Mathf.Clamp(value, 0, cameras.Length - 1);
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (switchMode == CinemachineSwitchMode.GameObjectActive) camera.gameObject.SetActive(i == index);
                else camera.Priority = i == index ? activePriority : inactivePriority;
            }
        }
    }

    [Serializable]
    public sealed class CinemachineFovFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private CinemachineCamera target;
        public void Initialize(RouteContext context) { if (target == null) throw new InvalidOperationException("CinemachineCamera target is not assigned."); }
        public void Write(float value) { var lens = target.Lens; lens.FieldOfView = Mathf.Clamp(value, 0.01f, 179f); target.Lens = lens; }
    }

    [Serializable]
    public sealed class CinemachineDutchFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private CinemachineCamera target;
        public void Initialize(RouteContext context) { if (target == null) throw new InvalidOperationException("CinemachineCamera target is not assigned."); }
        public void Write(float value) { var lens = target.Lens; lens.Dutch = value; target.Lens = lens; }
    }

    [Serializable]
    public sealed class CinemachinePriorityIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private CinemachineVirtualCameraBase target;
        public void Initialize(RouteContext context) { if (target == null) throw new InvalidOperationException("Cinemachine camera target is not assigned."); }
        public void Write(int value) => target.Priority = value;
    }
}
