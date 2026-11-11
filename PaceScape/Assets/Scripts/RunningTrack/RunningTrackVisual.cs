// Builds the oval markings and shares generated mesh and material resources.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaceScape.RunningTrack
{
    // Creates floor markings under the same visibility root as the marker object.
    [DisallowMultipleComponent]
    public sealed class RunningTrackVisual : MonoBehaviour
    {
        private const int Segments = 1024;
        private const float HalfLineWidth = 0.025f;
        private const float HeightAboveFloor = 0.01f;
        private static Mesh _sharedMesh;
        private static Material _sharedMaterial;
        private static int _resourceUsers;
        private bool _hasResources;

        // Builds an eight-lane oval aligned with the parent's floor pose, without changing its scale.
        public static RunningTrackVisual Create(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var track = new GameObject("Running track - 8 lane oval");
            track.layer = parent.gameObject.layer;
            track.transform.SetParent(parent, false);
            var visual = track.AddComponent<RunningTrackVisual>();
            try
            {
                AcquireResources();
                visual._hasResources = true;
                track.AddComponent<MeshFilter>().sharedMesh = _sharedMesh;
                var renderer = track.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _sharedMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return visual;
            }
            catch
            {
                visual.ReleaseGeneratedResources();
                Release(track);
                throw;
            }
        }

        // Shares one generated mesh and material across active track owners.
        private static void AcquireResources()
        {
            if (_resourceUsers == 0)
            {
                Shader shader = Resources.Load<Shader>("PaceScapeTrack");
                if (shader == null || !shader.isSupported)
                {
                    throw new InvalidOperationException("The bundled PaceScapeTrack shader is missing or unsupported.");
                }

                try
                {
                    _sharedMaterial = new Material(shader) { name = "Running track markings" };
                    _sharedMesh = BuildMesh();
                }
                catch
                {
                    ReleaseSharedResources();
                    throw;
                }
            }

            _resourceUsers++;
        }

        // Generates lane markings as a single mesh for rendering.
        private static Mesh BuildMesh()
        {
            int quadCount = Segments * (RunningTrackGeometry.LaneCount + 1) + 81;
            var vertices = new List<Vector3>(quadCount * 4);
            var colors = new List<Color>(quadCount * 4);
            var triangles = new List<int>(quadCount * 6);
            for (int segment = 0; segment < Segments; segment++)
            {
                double start = segment * RunningTrackGeometry.LapLength / Segments;
                double end = (segment + 1) * RunningTrackGeometry.LapLength / Segments;
                for (int lane = 0; lane <= RunningTrackGeometry.LaneCount; lane++)
                {
                    float offset = lane * RunningTrackGeometry.LaneWidth - (float)RunningTrackGeometry.MeasurementOffset;
                    AddStrip(start, end, offset - HalfLineWidth, offset + HalfLineWidth, Color.white);
                }
            }

            float inner = -(float)RunningTrackGeometry.MeasurementOffset;
            float outer = inner + RunningTrackGeometry.LaneCount * RunningTrackGeometry.LaneWidth;
            AddStrip(0, 0.12, inner, outer, Color.white);
            for (int tick = 1; tick < 40; tick++)
            {
                double distance = tick * RunningTrackGeometry.LapLength / 40;
                AddStrip(distance, distance + 0.12, inner, inner + 0.26f, Color.yellow);
                AddStrip(distance, distance + 0.12, outer - 0.26f, outer, Color.yellow);
            }

            AddQuad(new Vector3(-0.04f, 0f, 0.5f), new Vector3(-0.04f, 0f, 2.5f),
                new Vector3(0.04f, 0f, 2.5f), new Vector3(0.04f, 0f, 0.5f), Color.yellow);
            AddQuad(new Vector3(-0.35f, 0f, 2.2f), new Vector3(0f, 0f, 2.8f),
                new Vector3(0.35f, 0f, 2.2f), new Vector3(0f, 0f, 2.4f), Color.yellow);

            var mesh = new Mesh { name = "MarkerML2 oval markings" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // The unlit shader needs no normals, and the immutable mesh needs no CPU-side copy.
            mesh.UploadMeshData(true);
            return mesh;

            void AddStrip(double start, double end, float innerOffset, float outerOffset, Color color)
            {
                AddQuad(RunningTrackGeometry.Evaluate(start, innerOffset), RunningTrackGeometry.Evaluate(end, innerOffset),
                    RunningTrackGeometry.Evaluate(end, outerOffset), RunningTrackGeometry.Evaluate(start, outerOffset), color);
            }

            void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int first = vertices.Count;
                Vector3 elevation = Vector3.up * HeightAboveFloor;
                vertices.Add(a + elevation);
                vertices.Add(b + elevation);
                vertices.Add(c + elevation);
                vertices.Add(d + elevation);
                for (int corner = 0; corner < 4; corner++)
                {
                    colors.Add(color);
                }

                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }
        }

        private void OnDestroy()
        {
            ReleaseGeneratedResources();
        }

        // Releases generated assets, including when content was never active before removal.
        public void ReleaseGeneratedResources()
        {
            if (!_hasResources)
            {
                return;
            }

            _hasResources = false;
            _resourceUsers--;
            if (_resourceUsers == 0)
            {
                ReleaseSharedResources();
            }
        }

        private static void ReleaseSharedResources()
        {
            Release(_sharedMesh);
            Release(_sharedMaterial);
            _sharedMesh = null;
            _sharedMaterial = null;
        }

        private static void Release(UnityEngine.Object resource)
        {
            if (resource == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(resource);
            }
            else
            {
                DestroyImmediate(resource);
            }
        }
    }
}
