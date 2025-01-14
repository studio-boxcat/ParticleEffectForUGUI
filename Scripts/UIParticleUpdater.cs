using System;
using System.Collections.Generic;
using Coffee.UIParticleExtensions;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace Coffee.UIExtensions
{
    internal static class UIParticleUpdater
    {
        static readonly List<UIParticle> s_ActiveParticles = new();
        static MaterialPropertyBlock s_Mpb;
        private static int frameCount = 0;


        public static void Register(UIParticle particle)
        {
            if (!particle) return;
            s_ActiveParticles.Add(particle);
        }

        public static void Unregister(UIParticle particle)
        {
            if (!particle) return;
            s_ActiveParticles.Remove(particle);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            MeshHelper.Init();

            Canvas.willRenderCanvases -= Refresh;
            Canvas.willRenderCanvases += Refresh;
        }

        private static void Refresh()
        {
            // Do not allow it to be called in the same frame.
            if (frameCount == Time.frameCount) return;
            frameCount = Time.frameCount;

            Profiler.BeginSample("[UIParticle] Refresh");
            for (var i = 0; i < s_ActiveParticles.Count; i++)
            {
                try
                {
                    Refresh(s_ActiveParticles[i]);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            Profiler.EndSample();
        }

        private static void Refresh(UIParticle particle)
        {
            if (!particle || !particle.bakedMesh || !particle.canvas || !particle.canvasRenderer) return;

            Profiler.BeginSample("[UIParticle] Bake mesh");
            BakeMesh(particle);
            Profiler.EndSample();

            // if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            // {
            //     Profiler.BeginSample("[UIParticle] Modify color space to linear");
            //     particle.bakedMesh.ModifyColorSpaceToLinear();
            //     Profiler.EndSample();
            // }

            Profiler.BeginSample("[UIParticle] Set mesh to CanvasRenderer");
            particle.canvasRenderer.SetMesh(particle.bakedMesh);
            Profiler.EndSample();
        }

        private static Matrix4x4 GetScaledMatrix(ParticleSystem particle)
        {
            var transform = particle.transform;
            var main = particle.main;
            var space = main.simulationSpace;
            if (space == ParticleSystemSimulationSpace.Custom && !main.customSimulationSpace)
                space = ParticleSystemSimulationSpace.Local;

            switch (space)
            {
                case ParticleSystemSimulationSpace.Local:
                    return Matrix4x4.Rotate(transform.rotation).inverse
                           * Matrix4x4.Scale(transform.lossyScale).inverse;
                case ParticleSystemSimulationSpace.World:
                    return transform.worldToLocalMatrix;
                case ParticleSystemSimulationSpace.Custom:
                    // #78: Support custom simulation space.
                    return transform.worldToLocalMatrix
                           * Matrix4x4.Translate(main.customSimulationSpace.position);
                default:
                    return Matrix4x4.identity;
            }
        }

        private static void BakeMesh(UIParticle particle)
        {
            // Clear mesh before bake.
            Profiler.BeginSample("[UIParticle] Bake Mesh > Clear mesh before bake");
            MeshHelper.Clear();
            particle.bakedMesh.Clear(false);
            Profiler.EndSample();

            // Get camera for baking mesh.
            var camera = BakingCamera.GetCamera(particle.canvas);
            var root = particle.transform;
            var rootMatrix = Matrix4x4.Rotate(root.rotation).inverse
                             * Matrix4x4.Scale(root.lossyScale).inverse;

            // Cache position
            for (var i = 0; i < particle.particles.Count; i++)
            {
                Profiler.BeginSample("[UIParticle] Bake Mesh > Push index");
                MeshHelper.activeMeshIndices.Add(false);
                MeshHelper.activeMeshIndices.Add(false);
                Profiler.EndSample();

                // No particle to render.
                var currentPs = particle.particles[i];
                if (!currentPs || !currentPs.IsAlive() || currentPs.particleCount == 0) continue;
                var r = currentPs.GetComponent<ParticleSystemRenderer>();
                if (!r.sharedMaterial && !r.trailMaterial) continue;

                // Calc matrix.
                Profiler.BeginSample("[UIParticle] Bake Mesh > Calc matrix");
                var matrix = rootMatrix;
                if (currentPs.transform != root)
                {
                    if (currentPs.main.simulationSpace == ParticleSystemSimulationSpace.Local)
                    {
                        var relativePos = root.InverseTransformPoint(currentPs.transform.position);
                        matrix = Matrix4x4.Translate(relativePos) * matrix;
                    }
                    else
                    {
                        matrix *= Matrix4x4.Translate(-root.position);
                    }
                }
                else
                {
                    matrix = GetScaledMatrix(currentPs);
                }

                Profiler.EndSample();

#if UNITY_2018_3_OR_NEWER
                // #102: Do not bake particle system to mesh when the alpha is zero.
                if (Mathf.Approximately(particle.canvasRenderer.GetInheritedAlpha(), 0))
                    continue;
#endif

                // Bake main particles.
                if (CanBakeMesh(r))
                {
                    Profiler.BeginSample("[UIParticle] Bake Mesh > Bake Main Particles");
                    var hash = currentPs.GetMaterialHash(false);
                    if (hash != 0)
                    {
                        var m = MeshHelper.GetTemporaryMesh();
                        r.BakeMesh(m, camera, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                        MeshHelper.Push(i * 2, hash, m, matrix);
                    }

                    Profiler.EndSample();
                }

                // Bake trails particles.
                if (currentPs.trails.enabled)
                {
                    Profiler.BeginSample("[UIParticle] Bake Mesh > Bake Trails Particles");
                    var hash = currentPs.GetMaterialHash(true);
                    if (hash != 0)
                    {
                        matrix = currentPs.main.simulationSpace == ParticleSystemSimulationSpace.Local && currentPs.trails.worldSpace
                            ? matrix * Matrix4x4.Translate(-currentPs.transform.position)
                            : matrix;

                        var m = MeshHelper.GetTemporaryMesh();
                        try
                        {
                            r.BakeTrailsMesh(m, camera, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                            MeshHelper.Push(i * 2 + 1, hash, m, matrix);
                        }
                        catch
                        {
                            MeshHelper.DiscardTemporaryMesh(m);
                        }
                    }

                    Profiler.EndSample();
                }
            }

            // Set active indices.
            Profiler.BeginSample("[UIParticle] Bake Mesh > Set active indices");
            particle.activeMeshIndices = MeshHelper.activeMeshIndices;
            Profiler.EndSample();

            // Combine
            Profiler.BeginSample("[UIParticle] Bake Mesh > CombineMesh");
            MeshHelper.CombineMesh(particle.bakedMesh);
            MeshHelper.Clear();
            Profiler.EndSample();
        }

        private static bool CanBakeMesh(ParticleSystemRenderer renderer)
        {
            // #69: Editor crashes when mesh is set to null when `ParticleSystem.RenderMode = Mesh`
            if (renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh == null) return false;

            // #61: When `ParticleSystem.RenderMode = None`, an error occurs
            if (renderer.renderMode == ParticleSystemRenderMode.None) return false;

            return true;
        }
    }
}
