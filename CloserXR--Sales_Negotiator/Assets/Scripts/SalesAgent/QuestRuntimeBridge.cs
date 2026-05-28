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

        public static Camera EnsureProject3HeadTrackedView(Camera fallbackCamera)
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                return fallbackCamera;
            }

            ovrCameraRigType = ovrCameraRigType ?? FindType("OVRCameraRig");
            if (ovrCameraRigType == null)
            {
                if (!warnedMissingCameraRig)
                {
                    Debug.LogWarning("OVRCameraRig was not found, so CloserXR kept the default Main Camera.");
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
            return headCamera;
        }

        public static bool EnsurePassthrough(GameObject target)
        {
            target = target != null ? target : new GameObject("CloserXR Camera Runtime");

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
                manager = target.GetComponent(ovrManagerType);
            }

            if (manager == null)
            {
                manager = target.AddComponent(ovrManagerType);
            }

            SetMember(manager, "isInsightPassthroughEnabled", true);
            SetEnumMember(manager, "trackingOriginType", "FloorLevel");
            SetMember(manager, "launchSimultaneousHandsControllersOnStartup", true);
            SetMember(manager, "SimultaneousHandsAndControllersEnabled", true);
            SetMember(manager, "shouldBoundaryVisibilityBeSuppressed", true);

            Component passthroughLayer = UnityEngine.Object.FindObjectOfType(ovrPassthroughLayerType) as Component;
            if (passthroughLayer == null)
            {
                passthroughLayer = target.AddComponent(ovrPassthroughLayerType);
            }

            SetMember(passthroughLayer, "hidden", false);
            SetMember(passthroughLayer, "textureOpacity", 1f);
            SetMember(passthroughLayer, "edgeRenderingEnabled", false);
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
            headCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
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

        private static bool InvokeOvrInputEnumMethod(string methodName, string enumTypeName, string enumValueName)
        {
            if (!TryGetOvrInputEnumValue(enumTypeName, enumValueName, out Type enumType, out object enumValue))
            {
                return false;
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
                    return parameters.Length == 1 && parameters[0].ParameterType == enumType;
                });

            if (method == null)
            {
                return false;
            }

            object result = method.Invoke(null, new[] { enumValue });
            return result is bool pressed && pressed;
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
