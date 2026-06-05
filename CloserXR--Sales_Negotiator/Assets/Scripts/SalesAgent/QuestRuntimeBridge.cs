using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace CloserXR.SalesNegotiator
{
    internal static class QuestRuntimeBridge
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private const float AnalogPressThreshold = 0.45f;
        private const float ThumbstickDirectionThreshold = 0.55f;

        private static readonly Dictionary<string, Type> NestedEnumTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, object> EnumValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MethodInfo> OvrInputMethods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ButtonEdgeState> ButtonEdgeStates = new Dictionary<string, ButtonEdgeState>(StringComparer.OrdinalIgnoreCase);

        private static Type ovrInputType;
        private static Type ovrCameraRigType;
        private static Type ovrManagerType;
        private static Type ovrPassthroughLayerType;
        private static Type ovrSpatialAnchorType;
        private static bool warnedMissingOvrInput;
        private static bool warnedMissingPassthrough;
        private static bool warnedMissingCameraRig;
        private static string lastDiagnosticInput = "";
        private static float lastDiagnosticInputLogTime;

        private enum ButtonEdge
        {
            Down,
            Up
        }

        private enum StickDirection
        {
            Up,
            Down,
            Left,
            Right
        }

        private sealed class ButtonEdgeState
        {
            public int Frame = -1;
            public bool Previous;
            public bool Current;
        }

        public static bool GetPrimaryIndexTriggerDown()
        {
            return GetButtonEdge("PrimaryIndexTrigger", IsPrimaryIndexTriggerPressed(), ButtonEdge.Down);
        }

        public static bool GetPrimaryIndexTriggerUp()
        {
            return GetButtonEdge("PrimaryIndexTrigger", IsPrimaryIndexTriggerPressed(), ButtonEdge.Up);
        }

        public static bool GetLeftGripDown()
        {
            return GetButtonEdge("LeftGrip", IsLeftGripPressed(), ButtonEdge.Down);
        }

        public static bool GetLeftMenuToggleDown()
        {
            return GetButtonEdge("LeftMenuToggle", IsLeftMenuTogglePressed(), ButtonEdge.Down);
        }

        public static bool GetLeftIndexTriggerDown()
        {
            return GetButtonEdge("LeftIndexTrigger", IsLeftIndexTriggerPressed(), ButtonEdge.Down);
        }

        public static bool GetRightIndexTriggerDown()
        {
            return GetButtonEdge("RightIndexTrigger", IsRightIndexTriggerPressed(), ButtonEdge.Down);
        }

        public static bool GetRightIndexTriggerUp()
        {
            return GetButtonEdge("RightIndexTrigger", IsRightIndexTriggerPressed(), ButtonEdge.Up);
        }

        public static bool GetRawButtonDown(string rawButtonName)
        {
            return GetButtonEdge("RawButton." + rawButtonName, IsRawButtonCurrentlyPressed(rawButtonName), ButtonEdge.Down);
        }

        public static bool GetRawButtonUp(string rawButtonName)
        {
            return GetButtonEdge("RawButton." + rawButtonName, IsRawButtonCurrentlyPressed(rawButtonName), ButtonEdge.Up);
        }

        public static bool GetButtonDown(string buttonName)
        {
            return GetButtonEdge("Button." + buttonName, IsButtonCurrentlyPressed(buttonName), ButtonEdge.Down);
        }

        public static bool GetButtonUp(string buttonName)
        {
            return GetButtonEdge("Button." + buttonName, IsButtonCurrentlyPressed(buttonName), ButtonEdge.Up);
        }

        public static string GetControllerDiagnostics()
        {
            InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            List<string> pressed = new List<string>();

            if (IsLeftGripPressed())
            {
                pressed.Add("L grip");
            }

            if (IsLeftIndexTriggerPressed())
            {
                pressed.Add("L trigger");
            }

            if (IsRightIndexTriggerPressed())
            {
                pressed.Add("R trigger");
            }

            if (IsRawButtonCurrentlyPressed("A"))
            {
                pressed.Add("A");
            }

            if (IsRawButtonCurrentlyPressed("B"))
            {
                pressed.Add("B");
            }

            if (IsRawButtonCurrentlyPressed("X"))
            {
                pressed.Add("X");
            }

            if (IsRawButtonCurrentlyPressed("Y"))
            {
                pressed.Add("Y");
            }

            Vector2 rightStick = GetOvrAxis2D("RawAxis2D", "RThumbstick", "RTouch");
            if (rightStick.sqrMagnitude <= 0.001f)
            {
                rightStick = GetXrPrimary2DAxis(XRNode.RightHand);
            }

            if (rightStick.magnitude >= ThumbstickDirectionThreshold)
            {
                pressed.Add("R stick " + FormatAxis(rightStick));
            }

            string pressedText = pressed.Count > 0 ? string.Join(", ", pressed) : "none";
            if (pressedText != "none")
            {
                LogDiagnosticInput(pressedText);
            }

            return "L:" + (leftDevice.isValid ? "valid" : "missing")
                + " R:" + (rightDevice.isValid ? "valid" : "missing")
                + " Press:" + pressedText;
        }

        private static bool GetRawButtonDown(string rawButtonName, string controllerName)
        {
            return GetButtonEdge("RawButton." + controllerName + "." + rawButtonName, IsRawButtonCurrentlyPressed(rawButtonName, controllerName), ButtonEdge.Down);
        }

        private static bool GetRawButtonUp(string rawButtonName, string controllerName)
        {
            return GetButtonEdge("RawButton." + controllerName + "." + rawButtonName, IsRawButtonCurrentlyPressed(rawButtonName, controllerName), ButtonEdge.Up);
        }

        private static bool GetButtonDown(string buttonName, string controllerName)
        {
            return GetButtonEdge("Button." + controllerName + "." + buttonName, IsButtonCurrentlyPressed(buttonName, controllerName), ButtonEdge.Down);
        }

        private static bool GetButtonUp(string buttonName, string controllerName)
        {
            return GetButtonEdge("Button." + controllerName + "." + buttonName, IsButtonCurrentlyPressed(buttonName, controllerName), ButtonEdge.Up);
        }

        private static bool IsPrimaryIndexTriggerPressed()
        {
            return IsLeftIndexTriggerPressed() || IsRightIndexTriggerPressed();
        }

        private static bool IsLeftGripPressed()
        {
            return IsRawButtonCurrentlyPressed("LHandTrigger", "LTouch")
                || IsButtonCurrentlyPressed("PrimaryHandTrigger", "LTouch")
                || GetOvrAxis1D("RawAxis1D", "LHandTrigger", "LTouch") >= AnalogPressThreshold
                || GetOvrAxis1D("Axis1D", "PrimaryHandTrigger", "LTouch") >= AnalogPressThreshold
                || GetXrAnalogButtonPressed(XRNode.LeftHand, CommonUsages.gripButton, CommonUsages.grip);
        }

        private static bool IsLeftMenuTogglePressed()
        {
            return IsLeftGripPressed()
                || IsRawButtonCurrentlyPressed("LThumbstick", "LTouch")
                || IsButtonCurrentlyPressed("PrimaryThumbstick", "LTouch");
        }

        private static bool IsLeftIndexTriggerPressed()
        {
            return IsRawButtonCurrentlyPressed("LIndexTrigger", "LTouch")
                || IsButtonCurrentlyPressed("PrimaryIndexTrigger", "LTouch")
                || GetOvrAxis1D("RawAxis1D", "LIndexTrigger", "LTouch") >= AnalogPressThreshold
                || GetOvrAxis1D("Axis1D", "PrimaryIndexTrigger", "LTouch") >= AnalogPressThreshold
                || GetXrAnalogButtonPressed(XRNode.LeftHand, CommonUsages.triggerButton, CommonUsages.trigger);
        }

        private static bool IsRightIndexTriggerPressed()
        {
            return IsRawButtonCurrentlyPressed("RIndexTrigger", "RTouch")
                || IsButtonCurrentlyPressed("PrimaryIndexTrigger", "RTouch")
                || IsButtonCurrentlyPressed("SecondaryIndexTrigger", "RTouch")
                || GetOvrAxis1D("RawAxis1D", "RIndexTrigger", "RTouch") >= AnalogPressThreshold
                || GetOvrAxis1D("Axis1D", "PrimaryIndexTrigger", "RTouch") >= AnalogPressThreshold
                || GetOvrAxis1D("Axis1D", "SecondaryIndexTrigger", "RTouch") >= AnalogPressThreshold
                || GetXrAnalogButtonPressed(XRNode.RightHand, CommonUsages.triggerButton, CommonUsages.trigger);
        }

        private static bool IsRawButtonCurrentlyPressed(string rawButtonName, string controllerName = "Active")
        {
            return InvokeOvrInputBoolMethod("RawButton", rawButtonName, controllerName)
                || GetXrRawButtonPressed(rawButtonName);
        }

        private static bool IsButtonCurrentlyPressed(string buttonName, string controllerName = "Active")
        {
            return InvokeOvrInputBoolMethod("Button", buttonName, controllerName)
                || GetXrVirtualButtonPressed(buttonName, controllerName);
        }

        private static bool GetButtonEdge(string key, bool isPressed, ButtonEdge edge)
        {
            if (!ButtonEdgeStates.TryGetValue(key, out ButtonEdgeState state))
            {
                state = new ButtonEdgeState();
                ButtonEdgeStates[key] = state;
            }

            int frame = Time.frameCount;
            if (state.Frame != frame)
            {
                state.Previous = state.Frame < 0 ? false : state.Current;
                state.Current = isPressed;
                state.Frame = frame;
            }

            return edge == ButtonEdge.Down
                ? state.Current && !state.Previous
                : !state.Current && state.Previous;
        }

        private static bool GetXrRawButtonPressed(string rawButtonName)
        {
            switch (rawButtonName)
            {
                case "A":
                    return GetXrButtonPressed(XRNode.RightHand, CommonUsages.primaryButton);
                case "B":
                    return GetXrButtonPressed(XRNode.RightHand, CommonUsages.secondaryButton);
                case "X":
                    return GetXrButtonPressed(XRNode.LeftHand, CommonUsages.primaryButton);
                case "Y":
                    return GetXrButtonPressed(XRNode.LeftHand, CommonUsages.secondaryButton);
                case "LIndexTrigger":
                    return GetXrAnalogButtonPressed(XRNode.LeftHand, CommonUsages.triggerButton, CommonUsages.trigger);
                case "RIndexTrigger":
                    return GetXrAnalogButtonPressed(XRNode.RightHand, CommonUsages.triggerButton, CommonUsages.trigger);
                case "LHandTrigger":
                    return GetXrAnalogButtonPressed(XRNode.LeftHand, CommonUsages.gripButton, CommonUsages.grip);
                case "RHandTrigger":
                    return GetXrAnalogButtonPressed(XRNode.RightHand, CommonUsages.gripButton, CommonUsages.grip);
                case "LThumbstick":
                    return GetXrButtonPressed(XRNode.LeftHand, CommonUsages.primary2DAxisClick);
                case "RThumbstick":
                    return GetXrButtonPressed(XRNode.RightHand, CommonUsages.primary2DAxisClick);
                case "LThumbstickUp":
                    return GetThumbstickDirectionPressed(XRNode.LeftHand, "LTouch", "LThumbstick", StickDirection.Up);
                case "LThumbstickDown":
                    return GetThumbstickDirectionPressed(XRNode.LeftHand, "LTouch", "LThumbstick", StickDirection.Down);
                case "LThumbstickLeft":
                    return GetThumbstickDirectionPressed(XRNode.LeftHand, "LTouch", "LThumbstick", StickDirection.Left);
                case "LThumbstickRight":
                    return GetThumbstickDirectionPressed(XRNode.LeftHand, "LTouch", "LThumbstick", StickDirection.Right);
                case "RThumbstickUp":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Up);
                case "RThumbstickDown":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Down);
                case "RThumbstickLeft":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Left);
                case "RThumbstickRight":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Right);
                default:
                    return false;
            }
        }

        private static bool GetXrVirtualButtonPressed(string buttonName, string controllerName)
        {
            XRNode node = string.Equals(controllerName, "RTouch", StringComparison.OrdinalIgnoreCase)
                ? XRNode.RightHand
                : XRNode.LeftHand;

            switch (buttonName)
            {
                case "One":
                    return GetXrButtonPressed(XRNode.RightHand, CommonUsages.primaryButton);
                case "Two":
                    return GetXrButtonPressed(XRNode.RightHand, CommonUsages.secondaryButton);
                case "Three":
                    return GetXrButtonPressed(XRNode.LeftHand, CommonUsages.primaryButton);
                case "Four":
                    return GetXrButtonPressed(XRNode.LeftHand, CommonUsages.secondaryButton);
                case "PrimaryIndexTrigger":
                    return GetXrAnalogButtonPressed(node, CommonUsages.triggerButton, CommonUsages.trigger);
                case "PrimaryHandTrigger":
                    return GetXrAnalogButtonPressed(node, CommonUsages.gripButton, CommonUsages.grip);
                case "PrimaryThumbstick":
                    return GetXrButtonPressed(node, CommonUsages.primary2DAxisClick);
                case "PrimaryThumbstickUp":
                    return GetThumbstickDirectionPressed(node, controllerName, GetRawThumbstickAxisName(node), StickDirection.Up);
                case "PrimaryThumbstickDown":
                    return GetThumbstickDirectionPressed(node, controllerName, GetRawThumbstickAxisName(node), StickDirection.Down);
                case "PrimaryThumbstickLeft":
                    return GetThumbstickDirectionPressed(node, controllerName, GetRawThumbstickAxisName(node), StickDirection.Left);
                case "PrimaryThumbstickRight":
                    return GetThumbstickDirectionPressed(node, controllerName, GetRawThumbstickAxisName(node), StickDirection.Right);
                case "SecondaryIndexTrigger":
                    return GetXrAnalogButtonPressed(XRNode.RightHand, CommonUsages.triggerButton, CommonUsages.trigger);
                case "SecondaryHandTrigger":
                    return GetXrAnalogButtonPressed(XRNode.RightHand, CommonUsages.gripButton, CommonUsages.grip);
                case "SecondaryThumbstick":
                    return GetXrButtonPressed(XRNode.RightHand, CommonUsages.primary2DAxisClick);
                case "SecondaryThumbstickUp":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Up);
                case "SecondaryThumbstickDown":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Down);
                case "SecondaryThumbstickLeft":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Left);
                case "SecondaryThumbstickRight":
                    return GetThumbstickDirectionPressed(XRNode.RightHand, "RTouch", "RThumbstick", StickDirection.Right);
                default:
                    return false;
            }
        }

        private static string GetRawThumbstickAxisName(XRNode node)
        {
            return node == XRNode.RightHand ? "RThumbstick" : "LThumbstick";
        }

        private static bool GetThumbstickDirectionPressed(XRNode node, string controllerName, string rawAxisName, StickDirection direction)
        {
            Vector2 axis = GetOvrAxis2D("RawAxis2D", rawAxisName, controllerName);
            if (axis.sqrMagnitude <= 0.001f)
            {
                axis = GetXrPrimary2DAxis(node);
            }

            switch (direction)
            {
                case StickDirection.Up:
                    return axis.y >= ThumbstickDirectionThreshold && Mathf.Abs(axis.y) >= Mathf.Abs(axis.x);
                case StickDirection.Down:
                    return axis.y <= -ThumbstickDirectionThreshold && Mathf.Abs(axis.y) >= Mathf.Abs(axis.x);
                case StickDirection.Left:
                    return axis.x <= -ThumbstickDirectionThreshold && Mathf.Abs(axis.x) >= Mathf.Abs(axis.y);
                case StickDirection.Right:
                    return axis.x >= ThumbstickDirectionThreshold && Mathf.Abs(axis.x) >= Mathf.Abs(axis.y);
                default:
                    return false;
            }
        }

        private static bool GetXrButtonPressed(XRNode node, InputFeatureUsage<bool> usage)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool pressed) && pressed;
        }

        private static string FormatAxis(Vector2 axis)
        {
            return axis.x.ToString("0.0") + "," + axis.y.ToString("0.0");
        }

        private static void LogDiagnosticInput(string pressedText)
        {
            if (pressedText == lastDiagnosticInput && Time.unscaledTime - lastDiagnosticInputLogTime < 0.5f)
            {
                return;
            }

            lastDiagnosticInput = pressedText;
            lastDiagnosticInputLogTime = Time.unscaledTime;
            Debug.Log("[CloserXR Controller] Input detected: " + pressedText);
        }

        private static bool GetXrAnalogButtonPressed(XRNode node, InputFeatureUsage<bool> buttonUsage, InputFeatureUsage<float> axisUsage)
        {
            if (GetXrButtonPressed(node, buttonUsage))
            {
                return true;
            }

            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid
                && device.TryGetFeatureValue(axisUsage, out float value)
                && value >= AnalogPressThreshold;
        }

        private static Vector2 GetXrPrimary2DAxis(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)
                ? axis
                : Vector2.zero;
        }

        public static bool TryGetLeftControllerRay(out Ray ray)
        {
            if (TryGetControllerAnchorRay("leftControllerAnchor", "leftHandAnchor", out ray))
            {
                return true;
            }

            if (TryGetLocalControllerRay("LTouch", out ray))
            {
                return true;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                Transform cameraTransform = camera.transform;
                ray = new Ray(cameraTransform.TransformPoint(new Vector3(-0.22f, -0.24f, 0.05f)), cameraTransform.forward);
                return false;
            }

            ray = new Ray(Vector3.zero, Vector3.forward);
            return false;
        }

        public static Camera EnsureProject3HeadTrackedView(Camera fallbackCamera)
        {
            ovrCameraRigType = ovrCameraRigType ?? FindType("OVRCameraRig");
            if (ovrCameraRigType == null)
            {
                if (!warnedMissingCameraRig)
                {
                    Debug.LogWarning("OVRCameraRig was not found. CloserXR could not create the required OVR camera rig.");
                    warnedMissingCameraRig = true;
                }

                return fallbackCamera;
            }

            Component cameraRig = UnityEngine.Object.FindObjectOfType(ovrCameraRigType) as Component;
            GameObject rigObject;
            if (cameraRig != null)
            {
                rigObject = cameraRig.gameObject;
            }
            else
            {
                rigObject = new GameObject("OVRCameraRig");
                rigObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                EnsureOvrManagerAndPassthrough(rigObject);
                cameraRig = rigObject.AddComponent(ovrCameraRigType);
            }

            rigObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            EnsureOvrManagerAndPassthrough(rigObject);
            InvokePublicInstance(cameraRig, "EnsureGameObjectIntegrity");

            Transform centerEye = GetObjectMember(cameraRig, "centerEyeAnchor") as Transform
                ?? FindDeepChild(rigObject.transform, "CenterEyeAnchor")
                ?? rigObject.transform;
            Camera headCamera = centerEye.GetComponent<Camera>() ?? centerEye.gameObject.AddComponent<Camera>();
            ConfigureProject3CenterEyeCamera(headCamera, fallbackCamera);
            DisableFallbackCamera(fallbackCamera, headCamera);
            DisableNonOvrSceneCameras(headCamera);
            return headCamera;
        }

        public static bool EnsurePassthrough(GameObject target)
        {
            target = target != null ? target : new GameObject("CloserXR Camera Runtime");
            GameObject passthroughHost = FindOvrCameraRigObject() ?? target;

            ovrManagerType = ovrManagerType ?? FindType("OVRManager");
            ovrPassthroughLayerType = ovrPassthroughLayerType ?? FindType("OVRPassthroughLayer");

            if (ovrManagerType == null || ovrPassthroughLayerType == null)
            {
                if (!warnedMissingPassthrough)
                {
                    Debug.LogWarning("Meta/Oculus SDK classes were not found, so Quest passthrough setup was skipped.");
                    warnedMissingPassthrough = true;
                }

                return false;
            }

            Component manager = GetOvrManagerInstance() as Component;
            if (manager == null)
            {
                manager = passthroughHost.GetComponent(ovrManagerType);
            }

            if (manager == null)
            {
                manager = UnityEngine.Object.FindObjectOfType(ovrManagerType) as Component;
            }

            if (manager == null)
            {
                manager = passthroughHost.AddComponent(ovrManagerType);
            }

            SetMember(manager, "isInsightPassthroughEnabled", true);
            SetEnumMember(manager, "trackingOriginType", "FloorLevel");
            SetEnumMember(manager, "controllerDrivenHandPosesType", "None");
            SetMember(manager, "launchSimultaneousHandsControllersOnStartup", false);
            SetMember(manager, "SimultaneousHandsAndControllersEnabled", false);
            DisableSimultaneousHandsAndControllers();

            Component passthroughLayer = UnityEngine.Object.FindObjectOfType(ovrPassthroughLayerType) as Component;
            if (passthroughLayer == null)
            {
                passthroughLayer = passthroughHost.AddComponent(ovrPassthroughLayerType);
            }

            SetMember(passthroughLayer, "enabled", true);
            SetEnumMember(passthroughLayer, "overlayType", "Underlay");
            SetEnumMember(passthroughLayer, "projectionSurfaceType", "Reconstructed");
            SetMember(passthroughLayer, "compositionDepth", 0);
            SetMember(passthroughLayer, "hidden", false);
            SetMember(passthroughLayer, "textureOpacity", 1f);
            SetMember(passthroughLayer, "edgeRenderingEnabled", false);
            EnsureTransparentCameraBackground(target);
            return true;
        }

        private static void EnsureOvrManagerAndPassthrough(GameObject target)
        {
            EnsurePassthrough(target);
        }

        private static void DisableSimultaneousHandsAndControllers()
        {
            ovrInputType = ovrInputType ?? FindType("OVRInput");
            MethodInfo disableMethod = ovrInputType?.GetMethod("DisableSimultaneousHandsAndControllers", PublicStatic);
            if (disableMethod == null)
            {
                return;
            }

            try
            {
                disableMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not force controller-only input mode: " + e.Message);
            }
        }

        private static void ConfigureProject3CenterEyeCamera(Camera headCamera, Camera fallbackCamera)
        {
            headCamera.tag = "MainCamera";
            headCamera.clearFlags = CameraClearFlags.SolidColor;
            headCamera.backgroundColor = Color.clear;
            headCamera.stereoTargetEye = StereoTargetEyeMask.Both;

            if (fallbackCamera != null)
            {
                headCamera.nearClipPlane = fallbackCamera.nearClipPlane;
                headCamera.farClipPlane = fallbackCamera.farClipPlane;
                headCamera.fieldOfView = fallbackCamera.fieldOfView;
                headCamera.cullingMask = fallbackCamera.cullingMask;
            }

            AudioListener listener = headCamera.GetComponent<AudioListener>();
            if (listener == null)
            {
                listener = headCamera.gameObject.AddComponent<AudioListener>();
            }

            listener.enabled = true;
        }

        private static void DisableFallbackCamera(Camera fallbackCamera, Camera headCamera)
        {
            if (fallbackCamera == null || fallbackCamera == headCamera)
            {
                return;
            }

            fallbackCamera.tag = "Untagged";
            fallbackCamera.enabled = false;

            AudioListener fallbackListener = fallbackCamera.GetComponent<AudioListener>();
            if (fallbackListener != null)
            {
                fallbackListener.enabled = false;
            }

            RoomCameraController editorController = fallbackCamera.GetComponent<RoomCameraController>();
            if (editorController != null)
            {
                editorController.enabled = false;
            }
        }

        private static void DisableNonOvrSceneCameras(Camera headCamera)
        {
            foreach (Camera camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                if (camera == null || camera == headCamera)
                {
                    continue;
                }

                if (camera.transform == headCamera.transform || camera.transform.IsChildOf(headCamera.transform.root))
                {
                    continue;
                }

                camera.tag = "Untagged";
                camera.enabled = false;

                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }

        public static bool EnsureSpatialAnchor(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            ovrSpatialAnchorType = ovrSpatialAnchorType ?? FindType("OVRSpatialAnchor");
            if (ovrSpatialAnchorType == null)
            {
                return false;
            }

            if (target.GetComponent(ovrSpatialAnchorType) == null)
            {
                target.AddComponent(ovrSpatialAnchorType);
            }

            return true;
        }

        private static bool InvokeOvrInputBoolMethod(string enumTypeName, string enumValueName, string controllerName = "Active")
        {
            object result = InvokeOvrInputEnumMethod("Get", enumTypeName, enumValueName, controllerName);
            return result is bool pressed && pressed;
        }

        private static float GetOvrAxis1D(string enumTypeName, string enumValueName, string controllerName = "Active")
        {
            object result = InvokeOvrInputEnumMethod("Get", enumTypeName, enumValueName, controllerName);
            return result is float value ? value : 0f;
        }

        private static Vector2 GetOvrAxis2D(string enumTypeName, string enumValueName, string controllerName = "Active")
        {
            object result = InvokeOvrInputEnumMethod("Get", enumTypeName, enumValueName, controllerName);
            return result is Vector2 value ? value : Vector2.zero;
        }

        private static object InvokeOvrInputEnumMethod(
            string methodName,
            string enumTypeName,
            string enumValueName,
            string controllerName = "Active")
        {
            if (!TryGetOvrInputEnumValue(enumTypeName, enumValueName, out Type enumType, out object enumValue))
            {
                return null;
            }

            MethodInfo method = GetOvrInputMethod(methodName, enumType);
            if (method == null)
            {
                return null;
            }

            ParameterInfo[] parameters = method.GetParameters();
            object[] arguments = parameters.Length == 1
                ? new[] { enumValue }
                : new[] { enumValue, GetOvrInputControllerArgument(parameters[1], controllerName) };

            return method.Invoke(null, arguments);
        }

        private static MethodInfo GetOvrInputMethod(string methodName, Type enumType)
        {
            string cacheKey = methodName + "." + enumType.FullName;
            if (OvrInputMethods.TryGetValue(cacheKey, out MethodInfo cachedMethod))
            {
                return cachedMethod;
            }

            MethodInfo method = ovrInputType
                .GetMethods(PublicStatic)
                .FirstOrDefault(candidate =>
                {
                    if (candidate.Name != methodName)
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length < 1 || parameters.Length > 2 || parameters[0].ParameterType != enumType)
                    {
                        return false;
                    }

                    return parameters.Length == 1 || IsOvrInputControllerParameter(parameters[1]);
                });

            if (method != null)
            {
                OvrInputMethods[cacheKey] = method;
            }

            return method;
        }

        private static bool IsOvrInputControllerParameter(ParameterInfo parameter)
        {
            Type controllerType = ovrInputType.GetNestedType("Controller", BindingFlags.Public);
            return controllerType != null && parameter.ParameterType == controllerType;
        }

        private static object GetOvrInputControllerArgument(ParameterInfo parameter, string controllerName)
        {
            string resolvedControllerName = string.IsNullOrWhiteSpace(controllerName) ? "Active" : controllerName;
            return TryGetOvrInputEnumValue("Controller", resolvedControllerName, out _, out object controller)
                ? controller
                : parameter.DefaultValue;
        }

        private static bool TryGetOvrInputEnumValue(string enumTypeName, string enumValueName, out Type enumType, out object enumValue)
        {
            enumType = null;
            enumValue = null;

            ovrInputType = ovrInputType ?? FindType("OVRInput");
            if (ovrInputType == null)
            {
                if (!warnedMissingOvrInput)
                {
                    Debug.LogWarning("OVRInput was not found. Quest controller buttons are disabled, but keyboard and UI controls still work.");
                    warnedMissingOvrInput = true;
                }

                return false;
            }

            if (!NestedEnumTypes.TryGetValue(enumTypeName, out enumType))
            {
                enumType = ovrInputType.GetNestedType(enumTypeName, BindingFlags.Public);
                if (enumType != null)
                {
                    NestedEnumTypes[enumTypeName] = enumType;
                }
            }

            if (enumType == null)
            {
                return false;
            }

            string cacheKey = enumTypeName + "." + enumValueName;
            if (!EnumValues.TryGetValue(cacheKey, out enumValue))
            {
                try
                {
                    enumValue = Enum.Parse(enumType, enumValueName);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                EnumValues[cacheKey] = enumValue;
            }

            return enumValue != null;
        }

        private static object GetOvrManagerInstance()
        {
            PropertyInfo property = ovrManagerType.GetProperty("instance", PublicStatic);
            return property != null ? property.GetValue(null) : null;
        }

        private static GameObject FindOvrCameraRigObject()
        {
            Component cameraRig = FindOvrCameraRigComponent();
            return cameraRig != null ? cameraRig.gameObject : null;
        }

        private static Component FindOvrCameraRigComponent()
        {
            ovrCameraRigType = ovrCameraRigType ?? FindType("OVRCameraRig");
            if (ovrCameraRigType == null)
            {
                return null;
            }

            return UnityEngine.Object.FindObjectOfType(ovrCameraRigType) as Component;
        }

        private static bool TryGetControllerAnchorRay(string controllerAnchorName, string handAnchorName, out Ray ray)
        {
            Component cameraRig = FindOvrCameraRigComponent();
            Transform controllerAnchor = GetObjectMember(cameraRig, controllerAnchorName) as Transform
                ?? GetObjectMember(cameraRig, handAnchorName) as Transform;

            if (controllerAnchor != null && controllerAnchor.gameObject.activeInHierarchy)
            {
                Vector3 direction = controllerAnchor.forward;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    ray = new Ray(controllerAnchor.position, direction.normalized);
                    return true;
                }
            }

            ray = default;
            return false;
        }

        private static bool TryGetLocalControllerRay(string controllerName, out Ray ray)
        {
            ray = default;

            if (!TryGetOvrInputEnumValue("Controller", controllerName, out Type controllerType, out object controllerValue))
            {
                return false;
            }

            MethodInfo positionMethod = ovrInputType.GetMethod(
                "GetLocalControllerPosition",
                PublicStatic,
                null,
                new[] { controllerType },
                null);
            MethodInfo rotationMethod = ovrInputType.GetMethod(
                "GetLocalControllerRotation",
                PublicStatic,
                null,
                new[] { controllerType },
                null);

            if (positionMethod == null || rotationMethod == null)
            {
                return false;
            }

            object positionResult = positionMethod.Invoke(null, new[] { controllerValue });
            object rotationResult = rotationMethod.Invoke(null, new[] { controllerValue });
            if (!(positionResult is Vector3 localPosition) || !(rotationResult is Quaternion localRotation))
            {
                return false;
            }

            Transform trackingSpace = GetTrackingSpaceTransform();
            Vector3 origin = trackingSpace != null ? trackingSpace.TransformPoint(localPosition) : localPosition;
            Quaternion rotation = trackingSpace != null ? trackingSpace.rotation * localRotation : localRotation;
            Vector3 direction = rotation * Vector3.forward;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            ray = new Ray(origin, direction.normalized);
            return true;
        }

        private static Transform GetTrackingSpaceTransform()
        {
            Component cameraRig = FindOvrCameraRigComponent();
            return GetObjectMember(cameraRig, "trackingSpace") as Transform
                ?? (cameraRig != null ? FindDeepChild(cameraRig.transform, "TrackingSpace") : null);
        }

        private static void EnsureTransparentCameraBackground(GameObject target)
        {
            Camera camera = target != null ? target.GetComponent<Camera>() : null;
            if (camera == null)
            {
                GameObject rigObject = FindOvrCameraRigObject();
                Transform centerEye = rigObject != null ? FindDeepChild(rigObject.transform, "CenterEyeAnchor") : null;
                camera = centerEye != null ? centerEye.GetComponent<Camera>() : Camera.main;
            }

            if (camera == null)
            {
                return;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
        }

        private static void SetMember(object target, string memberName, object value)
        {
            if (target == null)
            {
                return;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, PublicInstance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            FieldInfo field = type.GetField(memberName, PublicInstance);
            field?.SetValue(target, value);
        }

        private static void SetEnumMember(object target, string memberName, string enumValueName)
        {
            if (target == null)
            {
                return;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, PublicInstance);
            if (property != null && property.CanWrite && property.PropertyType.IsEnum)
            {
                property.SetValue(target, Enum.Parse(property.PropertyType, enumValueName));
                return;
            }

            FieldInfo field = type.GetField(memberName, PublicInstance);
            if (field != null && field.FieldType.IsEnum)
            {
                field.SetValue(target, Enum.Parse(field.FieldType, enumValueName));
            }
        }

        private static object GetObjectMember(object target, string memberName)
        {
            if (target == null)
            {
                return null;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, PublicInstance);
            if (property != null && property.CanRead)
            {
                return property.GetValue(target);
            }

            FieldInfo field = type.GetField(memberName, PublicInstance);
            return field != null ? field.GetValue(target) : null;
        }

        private static void InvokePublicInstance(object target, string methodName)
        {
            if (target == null)
            {
                return;
            }

            MethodInfo method = target.GetType().GetMethod(methodName, PublicInstance, null, Type.EmptyTypes, null);
            method?.Invoke(target, null);
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform match = FindDeepChild(root.GetChild(i), childName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Type FindType(string typeName)
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(type => type != null);
        }
    }
}
