using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    internal static class QuestRuntimeBridge
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        private static readonly Dictionary<string, Type> NestedEnumTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, object> EnumValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MethodInfo> OvrInputMethods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

        private static Type ovrInputType;
        private static Type ovrCameraRigType;
        private static Type ovrManagerType;
        private static Type ovrPassthroughLayerType;
        private static Type ovrSpatialAnchorType;
        private static bool warnedMissingOvrInput;
        private static bool warnedMissingPassthrough;
        private static bool warnedMissingCameraRig;

        public static bool GetPrimaryIndexTriggerDown()
        {
            return GetRawButtonDown("LIndexTrigger")
                || GetRawButtonDown("RIndexTrigger")
                || GetButtonDown("PrimaryIndexTrigger");
        }

        public static bool GetPrimaryIndexTriggerUp()
        {
            return GetRawButtonUp("LIndexTrigger")
                || GetRawButtonUp("RIndexTrigger")
                || GetButtonUp("PrimaryIndexTrigger");
        }

        public static bool GetLeftGripDown()
        {
            return GetRawButtonDown("LHandTrigger", "LTouch")
                || GetButtonDown("PrimaryHandTrigger", "LTouch");
        }

        public static bool GetLeftIndexTriggerDown()
        {
            return GetRawButtonDown("LIndexTrigger", "LTouch")
                || GetButtonDown("PrimaryIndexTrigger", "LTouch");
        }

        public static bool GetRightIndexTriggerDown()
        {
            return GetRawButtonDown("RIndexTrigger", "RTouch")
                || GetButtonDown("PrimaryIndexTrigger", "RTouch")
                || GetButtonDown("SecondaryIndexTrigger", "RTouch");
        }

        public static bool GetRightIndexTriggerUp()
        {
            return GetRawButtonUp("RIndexTrigger", "RTouch")
                || GetButtonUp("PrimaryIndexTrigger", "RTouch")
                || GetButtonUp("SecondaryIndexTrigger", "RTouch");
        }

        public static bool GetRawButtonDown(string rawButtonName)
        {
            return InvokeOvrInputEnumMethod("GetDown", "RawButton", rawButtonName);
        }

        public static bool GetRawButtonUp(string rawButtonName)
        {
            return InvokeOvrInputEnumMethod("GetUp", "RawButton", rawButtonName);
        }

        public static bool GetButtonDown(string buttonName)
        {
            return InvokeOvrInputEnumMethod("GetDown", "Button", buttonName);
        }

        public static bool GetButtonUp(string buttonName)
        {
            return InvokeOvrInputEnumMethod("GetUp", "Button", buttonName);
        }

        private static bool GetRawButtonDown(string rawButtonName, string controllerName)
        {
            return InvokeOvrInputEnumMethod("GetDown", "RawButton", rawButtonName, controllerName);
        }

        private static bool GetRawButtonUp(string rawButtonName, string controllerName)
        {
            return InvokeOvrInputEnumMethod("GetUp", "RawButton", rawButtonName, controllerName);
        }

        private static bool GetButtonDown(string buttonName, string controllerName)
        {
            return InvokeOvrInputEnumMethod("GetDown", "Button", buttonName, controllerName);
        }

        private static bool GetButtonUp(string buttonName, string controllerName)
        {
            return InvokeOvrInputEnumMethod("GetUp", "Button", buttonName, controllerName);
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
            SetMember(manager, "launchSimultaneousHandsControllersOnStartup", true);
            SetMember(manager, "SimultaneousHandsAndControllersEnabled", true);
            SetMember(manager, "shouldBoundaryVisibilityBeSuppressed", true);

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

        private static bool InvokeOvrInputEnumMethod(
            string methodName,
            string enumTypeName,
            string enumValueName,
            string controllerName = "Active")
        {
            if (!TryGetOvrInputEnumValue(enumTypeName, enumValueName, out Type enumType, out object enumValue))
            {
                return false;
            }

            MethodInfo method = GetOvrInputMethod(methodName, enumType);
            if (method == null)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            object[] arguments = parameters.Length == 1
                ? new[] { enumValue }
                : new[] { enumValue, GetOvrInputControllerArgument(parameters[1], controllerName) };

            object result = method.Invoke(null, arguments);
            return result is bool pressed && pressed;
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
