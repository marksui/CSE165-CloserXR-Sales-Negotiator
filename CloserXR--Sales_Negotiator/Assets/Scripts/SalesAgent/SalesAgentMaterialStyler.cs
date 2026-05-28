using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesAgentMaterialStyler : MonoBehaviour
    {
        [SerializeField] private bool applyOnAwake = true;
        [SerializeField] private Color skinColor = new Color(0.78f, 0.58f, 0.44f, 1f);
        [SerializeField] private Color shirtColor = new Color(0.08f, 0.42f, 0.48f, 1f);
        [SerializeField] private Color pantsColor = new Color(0.12f, 0.16f, 0.18f, 1f);
        [SerializeField] private Color sneakersColor = new Color(0.88f, 0.89f, 0.84f, 1f);
        [SerializeField] private Color eyelashColor = new Color(0.08f, 0.07f, 0.06f, 1f);

        private readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (applyOnAwake)
            {
                ApplyNow();
            }
        }

        public void ApplyNow()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material material = GetMaterialForRenderer(renderer);
                if (material == null)
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                if (materials == null || materials.Length == 0)
                {
                    materials = new[] { material };
                }

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    materials[materialIndex] = material;
                }

                renderer.materials = materials;
            }
        }

        private Material GetMaterialForRenderer(Renderer renderer)
        {
            string objectName = renderer.gameObject.name;

            if (objectName.IndexOf("Eyelashes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GetOrCreateMaterial("Eyelashes", eyelashColor, 0.2f);
            }

            if (objectName.IndexOf("Shirt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GetOrCreateMaterial("Shirt", shirtColor, 0.48f);
            }

            if (objectName.IndexOf("Pants", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GetOrCreateMaterial("Pants", pantsColor, 0.35f);
            }

            if (objectName.IndexOf("Sneakers", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GetOrCreateMaterial("Sneakers", sneakersColor, 0.55f);
            }

            if (objectName.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GetOrCreateMaterial("Skin", skinColor, 0.42f);
            }

            return null;
        }

        private Material GetOrCreateMaterial(string materialName, Color color, float smoothness)
        {
            if (materialCache.TryGetValue(materialName, out Material material) && material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }

            if (shader == null)
            {
                return null;
            }

            material = new Material(shader)
            {
                name = "CloserXR " + materialName,
                color = color,
                hideFlags = HideFlags.DontSave
            };

            SetColor(material, "_BaseColor", color);
            SetColor(material, "_Color", color);
            SetColor(material, "_EmissionColor", color);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_Glossiness", smoothness);

            materialCache[materialName] = material;
            return material;
        }

        private static void SetColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}
