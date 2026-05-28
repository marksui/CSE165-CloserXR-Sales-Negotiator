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
        private static Type ovrManagerType;
        private static Type ovrPassthroughLayerType;
        private static Type ovrSpatialAnchorType;
        private static bool warnedMissingOvrInput;
        private static bool warnedMissingPassthrough;

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

        private static Type FindType(string typeName)
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(type => type != null);
        }
    }
}
