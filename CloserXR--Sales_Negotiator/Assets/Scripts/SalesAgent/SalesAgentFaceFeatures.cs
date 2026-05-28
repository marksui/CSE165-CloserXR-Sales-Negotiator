using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class SalesAgentFaceFeatures : MonoBehaviour
    {
        [SerializeField] private bool showFeatures = true;
        [SerializeField] private float featureScale = 1f;
        [SerializeField] private Color eyeWhiteColor = new Color(0.96f, 0.95f, 0.9f, 1f);
        [SerializeField] private Color pupilColor = new Color(0.055f, 0.04f, 0.03f, 1f);
        [SerializeField] private Color eyebrowColor = new Color(0.22f, 0.15f, 0.1f, 1f);
        [SerializeField] private Color mouthColor = new Color(0.46f, 0.2f, 0.18f, 1f);
        [SerializeField] private Color noseColor = new Color(0.68f, 0.46f, 0.36f, 1f);

        private const string FaceRootName = "CloserXR Face Features";
        private readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        private void OnEnable()
        {
            EnsureFace();
        }

        private void LateUpdate()
        {
            EnsureFace();
        }

        public void EnsureFace()
        {
            Transform head = FindHead();
            if (head == null)
            {
                return;
            }

            Transform faceRoot = GetOrCreateFaceRoot(head);
            faceRoot.gameObject.SetActive(showFeatures);
            faceRoot.localPosition = Vector3.zero;
            faceRoot.localRotation = Quaternion.identity;
            faceRoot.localScale = Vector3.one * Mathf.Max(0.001f, featureScale);

            if (!showFeatures)
            {
                return;
            }

            RemoveFeature(faceRoot, "Left Brow");
            RemoveFeature(faceRoot, "Right Brow");
            RemoveFeature(faceRoot, "Mouth");

            CreateOrUpdateFeature(faceRoot, "Left Eye White", PrimitiveType.Sphere, new Vector3(-0.033f, 0.093f, 0.108f), new Vector3(0.021f, 0.011f, 0.004f), eyeWhiteColor);
            CreateOrUpdateFeature(faceRoot, "Right Eye White", PrimitiveType.Sphere, new Vector3(0.033f, 0.093f, 0.108f), new Vector3(0.021f, 0.011f, 0.004f), eyeWhiteColor);
            CreateOrUpdateFeature(faceRoot, "Left Pupil", PrimitiveType.Sphere, new Vector3(-0.033f, 0.093f, 0.113f), new Vector3(0.006f, 0.006f, 0.0025f), pupilColor);
            CreateOrUpdateFeature(faceRoot, "Right Pupil", PrimitiveType.Sphere, new Vector3(0.033f, 0.093f, 0.113f), new Vector3(0.006f, 0.006f, 0.0025f), pupilColor);

            CreateOrUpdateFeature(faceRoot, "Left Brow Soft", PrimitiveType.Sphere, new Vector3(-0.033f, 0.111f, 0.111f), new Vector3(0.018f, 0.0025f, 0.0025f), eyebrowColor);
            CreateOrUpdateFeature(faceRoot, "Right Brow Soft", PrimitiveType.Sphere, new Vector3(0.033f, 0.111f, 0.111f), new Vector3(0.018f, 0.0025f, 0.0025f), eyebrowColor);
            CreateOrUpdateFeature(faceRoot, "Nose", PrimitiveType.Sphere, new Vector3(0f, 0.072f, 0.118f), new Vector3(0.012f, 0.019f, 0.01f), noseColor);
            CreateOrUpdateFeature(faceRoot, "Mouth Soft", PrimitiveType.Sphere, new Vector3(0f, 0.043f, 0.116f), new Vector3(0.032f, 0.003f, 0.0025f), mouthColor);
        }

        private Transform FindHead()
        {
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                string transformName = transforms[i].name;
                if (transformName.IndexOf(":Head", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    transformName.IndexOf("HeadTop", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private Transform GetOrCreateFaceRoot(Transform head)
        {
            Transform existing = head.Find(FaceRootName);
            if (existing != null)
            {
                return existing;
            }

            GameObject faceObject = new GameObject(FaceRootName);
            faceObject.transform.SetParent(head, false);
            return faceObject.transform;
        }

        private void CreateOrUpdateFeature(
            Transform parent,
            string featureName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            Transform feature = parent.Find(featureName);
            if (feature == null)
            {
                GameObject featureObject = GameObject.CreatePrimitive(primitiveType);
                featureObject.name = featureName;
                featureObject.transform.SetParent(parent, false);
                RemoveCollider(featureObject);
                feature = featureObject.transform;
            }

            feature.localPosition = localPosition;
            feature.localRotation = Quaternion.identity;
            feature.localScale = localScale;

            Renderer renderer = feature.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetOrCreateMaterial(featureName, color);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static void RemoveFeature(Transform parent, string featureName)
        {
            Transform feature = parent.Find(featureName);
            if (feature == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(feature.gameObject);
            }
            else
            {
                DestroyImmediate(feature.gameObject);
            }
        }

        private Material GetOrCreateMaterial(string materialName, Color color)
        {
            if (materialCache.TryGetValue(materialName, out Material material) && material != null)
            {
                material.color = color;
                SetColor(material, "_BaseColor", color);
                SetColor(material, "_Color", color);
                return material;
            }

            Shader shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Diffuse");

            material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            material.name = "CloserXR Face " + materialName;
            material.color = color;
            material.hideFlags = HideFlags.DontSave;
            SetColor(material, "_BaseColor", color);
            SetColor(material, "_Color", color);
            SetColor(material, "_EmissionColor", color * 0.08f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.18f);
            SetFloat(material, "_Glossiness", 0.18f);

            materialCache[materialName] = material;
            return material;
        }

        private static void RemoveCollider(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        private static void SetColor(Material material, string propertyName, Color value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}
