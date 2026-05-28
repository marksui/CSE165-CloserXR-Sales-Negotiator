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
        [SerializeField] private Color pupilColor = new Color(0.045f, 0.04f, 0.035f, 1f);
        [SerializeField] private Color eyebrowColor = new Color(0.08f, 0.07f, 0.06f, 1f);
        [SerializeField] private Color mouthColor = new Color(0.5f, 0.18f, 0.16f, 1f);
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

            CreateOrUpdateFeature(faceRoot, "Left Eye White", PrimitiveType.Sphere, new Vector3(-0.034f, 0.096f, 0.106f), new Vector3(0.028f, 0.016f, 0.007f), eyeWhiteColor);
            CreateOrUpdateFeature(faceRoot, "Right Eye White", PrimitiveType.Sphere, new Vector3(0.034f, 0.096f, 0.106f), new Vector3(0.028f, 0.016f, 0.007f), eyeWhiteColor);
            CreateOrUpdateFeature(faceRoot, "Left Pupil", PrimitiveType.Sphere, new Vector3(-0.034f, 0.096f, 0.113f), new Vector3(0.01f, 0.01f, 0.004f), pupilColor);
            CreateOrUpdateFeature(faceRoot, "Right Pupil", PrimitiveType.Sphere, new Vector3(0.034f, 0.096f, 0.113f), new Vector3(0.01f, 0.01f, 0.004f), pupilColor);

            CreateOrUpdateFeature(faceRoot, "Left Brow", PrimitiveType.Cube, new Vector3(-0.034f, 0.12f, 0.109f), new Vector3(0.038f, 0.005f, 0.006f), eyebrowColor);
            CreateOrUpdateFeature(faceRoot, "Right Brow", PrimitiveType.Cube, new Vector3(0.034f, 0.12f, 0.109f), new Vector3(0.038f, 0.005f, 0.006f), eyebrowColor);
            CreateOrUpdateFeature(faceRoot, "Nose", PrimitiveType.Sphere, new Vector3(0f, 0.073f, 0.119f), new Vector3(0.015f, 0.023f, 0.013f), noseColor);
            CreateOrUpdateFeature(faceRoot, "Mouth", PrimitiveType.Cube, new Vector3(0f, 0.044f, 0.116f), new Vector3(0.062f, 0.007f, 0.006f), mouthColor);
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
