using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SpatialRoomMapDemo : MonoBehaviour
    {
        [SerializeField] private bool showRoomOutline = true;
        [SerializeField] private bool preferQuestGuardianBoundary = true;
        [SerializeField] private Vector2 fallbackRoomSize = new Vector2(4f, 5f);
        [SerializeField] private float fallbackForwardOffset = 1.25f;
        [SerializeField] private float wallHeight = 2.4f;
        [SerializeField] private int floorGridDivisions = 4;
        [SerializeField] private float lineWidth = 0.025f;
        [SerializeField] private Color floorColor = new Color(0.05f, 0.95f, 0.8f, 0.7f);
        [SerializeField] private Color wallColor = new Color(0.2f, 0.55f, 1f, 0.55f);

        private readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
        private readonly Vector3[] fallbackFootprint = new Vector3[4];

        private GameObject lineRoot;
        private Material lineMaterial;
        private Transform userHead;
        private Bounds floorBounds;
        private float nextRefreshTime;
        private int lineCursor;

        public bool HasRoomBounds { get; private set; }
        public string BoundarySourceLabel { get; private set; } = "Searching";

        private void Awake()
        {
            userHead = Camera.main != null ? Camera.main.transform : null;
        }

        private void Update()
        {
            if (Time.time < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.time + 0.35f;
            RefreshRoomOutline();
        }

        public Vector3 ClampToRoom(Vector3 position, float padding)
        {
            if (!HasRoomBounds)
            {
                return position;
            }

            Bounds paddedBounds = floorBounds;
            paddedBounds.Expand(new Vector3(-padding * 2f, 0f, -padding * 2f));

            if (paddedBounds.size.x <= 0.1f || paddedBounds.size.z <= 0.1f)
            {
                paddedBounds = floorBounds;
            }

            position.x = Mathf.Clamp(position.x, paddedBounds.min.x, paddedBounds.max.x);
            position.z = Mathf.Clamp(position.z, paddedBounds.min.z, paddedBounds.max.z);
            return position;
        }

        private void RefreshRoomOutline()
        {
            Vector3[] footprint = null;
            bool usingGuardian = preferQuestGuardianBoundary && TryReadQuestGuardianFootprint(out footprint);
            if (usingGuardian)
            {
                BoundarySourceLabel = "Guardian";
            }

            if (footprint == null || footprint.Length < 3)
            {
                footprint = BuildFallbackFootprint();
                BoundarySourceLabel = "Demo boundary";
            }

            UpdateFloorBounds(footprint);

            if (!showRoomOutline)
            {
                SetLineRootVisible(false);
                return;
            }

            SetLineRootVisible(true);
            BeginLines();
            DrawFloor(footprint);
            DrawWallOutlines(footprint);
            EndLines();
        }

        private Vector3[] BuildFallbackFootprint()
        {
            if (userHead == null && Camera.main != null)
            {
                userHead = Camera.main.transform;
            }

            Vector3 center = Vector3.zero;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (userHead != null)
            {
                center = userHead.position;
                forward = Vector3.ProjectOnPlane(userHead.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(userHead.right, Vector3.up).normalized;
            }

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.Cross(Vector3.up, forward).normalized;
            }

            center.y = 0f;
            center += forward * fallbackForwardOffset;

            float halfWidth = Mathf.Max(1f, fallbackRoomSize.x) * 0.5f;
            float halfDepth = Mathf.Max(1f, fallbackRoomSize.y) * 0.5f;

            fallbackFootprint[0] = center - right * halfWidth - forward * halfDepth;
            fallbackFootprint[1] = center + right * halfWidth - forward * halfDepth;
            fallbackFootprint[2] = center + right * halfWidth + forward * halfDepth;
            fallbackFootprint[3] = center - right * halfWidth + forward * halfDepth;
            return fallbackFootprint;
        }

        private bool TryReadQuestGuardianFootprint(out Vector3[] footprint)
        {
            footprint = null;

            try
            {
                Type boundaryType = FindType("OVRBoundary");
                Type managerType = FindType("OVRManager");
                if (boundaryType == null || managerType == null)
                {
                    return false;
                }

                object boundary = GetStaticMemberValue(managerType, "boundary");
                if (boundary == null)
                {
                    return false;
                }

                MethodInfo configuredMethod = boundaryType.GetMethod("GetConfigured", Type.EmptyTypes);
                if (configuredMethod != null && configuredMethod.ReturnType == typeof(bool))
                {
                    bool configured = (bool)configuredMethod.Invoke(boundary, null);
                    if (!configured)
                    {
                        return false;
                    }
                }

                Type boundaryEnum = boundaryType.GetNestedType("BoundaryType");
                if (boundaryEnum == null)
                {
                    return false;
                }

                object playArea = Enum.Parse(boundaryEnum, "PlayArea");
                MethodInfo geometryMethod = boundaryType.GetMethod("GetGeometry", new[] { boundaryEnum });
                if (geometryMethod != null)
                {
                    object geometry = geometryMethod.Invoke(boundary, new[] { playArea });
                    if (geometry is Vector3[] points && points.Length >= 3)
                    {
                        footprint = NormalizeFootprint(points);
                        return true;
                    }
                }

                MethodInfo dimensionsMethod = boundaryType.GetMethod("GetDimensions", new[] { boundaryEnum });
                if (dimensionsMethod != null && dimensionsMethod.ReturnType == typeof(Vector3))
                {
                    Vector3 dimensions = (Vector3)dimensionsMethod.Invoke(boundary, new[] { playArea });
                    if (dimensions.x > 0.1f && dimensions.z > 0.1f)
                    {
                        footprint = BuildCenteredRectangle(dimensions.x, dimensions.z);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Type FindType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static object GetStaticMemberValue(Type type, string memberName)
        {
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                return property.GetValue(null);
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
            return field != null ? field.GetValue(null) : null;
        }

        private static Vector3[] NormalizeFootprint(Vector3[] points)
        {
            Vector3[] footprint = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                footprint[i] = new Vector3(points[i].x, 0f, points[i].z);
            }

            return footprint;
        }

        private static Vector3[] BuildCenteredRectangle(float width, float depth)
        {
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            return new[]
            {
                new Vector3(-halfWidth, 0f, -halfDepth),
                new Vector3(halfWidth, 0f, -halfDepth),
                new Vector3(halfWidth, 0f, halfDepth),
                new Vector3(-halfWidth, 0f, halfDepth)
            };
        }

        private void UpdateFloorBounds(Vector3[] footprint)
        {
            floorBounds = new Bounds(footprint[0], Vector3.zero);
            for (int i = 1; i < footprint.Length; i++)
            {
                floorBounds.Encapsulate(footprint[i]);
            }

            HasRoomBounds = floorBounds.size.x > 0.25f && floorBounds.size.z > 0.25f;
        }

        private void DrawFloor(Vector3[] footprint)
        {
            AddPolyline(footprint, floorColor, true);

            int divisions = Mathf.Clamp(floorGridDivisions, 0, 8);
            if (divisions < 2)
            {
                return;
            }

            for (int i = 1; i < divisions; i++)
            {
                float t = i / (float)divisions;
                Vector3 left = Vector3.Lerp(footprint[0], footprint[3], t);
                Vector3 right = Vector3.Lerp(footprint[1], footprint[2], t);
                Vector3 bottom = Vector3.Lerp(footprint[0], footprint[1], t);
                Vector3 top = Vector3.Lerp(footprint[3], footprint[2], t);
                AddLine(left, right, floorColor);
                AddLine(bottom, top, floorColor);
            }
        }

        private void DrawWallOutlines(Vector3[] footprint)
        {
            Vector3[] topFootprint = new Vector3[footprint.Length];
            for (int i = 0; i < footprint.Length; i++)
            {
                topFootprint[i] = footprint[i] + Vector3.up * wallHeight;
                AddLine(footprint[i], topFootprint[i], wallColor);
            }

            AddPolyline(topFootprint, wallColor, true);
        }

        private void BeginLines()
        {
            EnsureLineRoot();
            lineCursor = 0;
        }

        private void EndLines()
        {
            for (int i = lineCursor; i < lineRenderers.Count; i++)
            {
                lineRenderers[i].enabled = false;
            }
        }

        private void AddLine(Vector3 start, Vector3 end, Color color)
        {
            AddPolyline(new[] { start, end }, color, false);
        }

        private void AddPolyline(IList<Vector3> points, Color color, bool loop)
        {
            LineRenderer line = GetLineRenderer();
            line.enabled = true;
            line.loop = loop;
            line.positionCount = points.Count;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;

            for (int i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i]);
            }
        }

        private LineRenderer GetLineRenderer()
        {
            EnsureLineRoot();

            if (lineCursor >= lineRenderers.Count)
            {
                GameObject lineObject = new GameObject($"Room Outline Line {lineRenderers.Count + 1}");
                lineObject.transform.SetParent(lineRoot.transform, false);

                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.textureMode = LineTextureMode.Stretch;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                if (lineMaterial != null)
                {
                    line.material = lineMaterial;
                }

                lineRenderers.Add(line);
            }

            return lineRenderers[lineCursor++];
        }

        private void EnsureLineRoot()
        {
            if (lineRoot != null)
            {
                return;
            }

            lineRoot = new GameObject("CloserXR Room Outline");
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/Internal-Colored");
            if (shader != null)
            {
                lineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
            }
        }

        private void SetLineRootVisible(bool visible)
        {
            EnsureLineRoot();
            lineRoot.SetActive(visible);
        }

        private void OnDestroy()
        {
            if (lineRoot != null)
            {
                Destroy(lineRoot);
            }

            if (lineMaterial != null)
            {
                Destroy(lineMaterial);
            }
        }
    }
}
