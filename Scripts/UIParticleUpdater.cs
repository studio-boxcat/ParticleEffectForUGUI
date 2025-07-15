#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;

namespace Coffee.UIExtensions
{
    internal static class UIParticleUpdater
    {
        private static readonly List<UIParticle> _particles = new();
        private static CombineInstance[] _cis = new CombineInstance[2]; // temporary buffer for CombineMeshes.

        public static void Register(UIParticle particle)
        {
            Assert.IsFalse(_particles.ContainsRef(particle), $"UIParticle {particle.SafeName()} is already registered.");

            _particles.Add(particle);
            if (_particles.Count is 1)
            {
                L.I("[UIParticle] Registering UIParticleUpdater.");
                Canvas.willRenderCanvases += (_refresh ??= Refresh);
            }
        }

        public static void Unregister(UIParticle particle)
        {
            Assert.IsTrue(_particles.ContainsRef(particle), $"UIParticle {particle.SafeName()} is not registered.");
            Assert.IsNotNull(_refresh, "UIParticleUpdater is not initialized properly. Please call Register first.");

            _particles.Remove(particle);
            if (_particles.Count is 0)
            {
                L.I("[UIParticle] Unregistering UIParticleUpdater.");
                Canvas.willRenderCanvases -= _refresh;
            }
        }

        private static int _frameCount;
        private static Canvas.WillRenderCanvases? _refresh;

        private static void Refresh()
        {
#if UNITY_EDITOR
            if (_skipRefresh)
            {
                // #4  0x00000101d78ec4 in ParticleSystemRenderer::BakeMesh(PPtr<Mesh>, PPtr<Camera>, ParticleSystemBakeMeshOptions)
                // ...
                // #7  0x000004c70df968 in  Coffee.UIExtensions.UIParticleUpdater:BakeMesh (Coffee.UIExtensions.UIParticle,UnityEngine.Mesh) [{0x352dbded0} + 0x630] [/Users/jameskim/Develop/meow-tower/Packages/com.coffee.ui-particle/Scripts/UIParticleUpdater.cs :: 142u] (0x4c70df338 0x4c70e0238) [0x12f602a80 - Unity Child Domain]
                // #8  0x000004c70dee30 in  Coffee.UIExtensions.UIParticleUpdater:Refresh () [{0x34d5de4b8} + 0x380] [/Users/jameskim/Develop/meow-tower/Packages/com.coffee.ui-particle/Scripts/UIParticleUpdater.cs :: 66u] (0x4c70deab0 0x4c70df058) [0x12f602a80 - Unity Child Domain]
                // ...
                // #25 0x000001021fd808 in PlayerLoopController::ExitPlayMode()
                // #26 0x000001021f4260 in PlayerLoopController::SetIsPlaying(bool)
                L.I("[UIParticle] Refresh is skipped to prevent Unity crash.");
                return;
            }
#endif

            // Do not allow it to be called in the same frame.
            if (_frameCount == Time.frameCount) return;
            _frameCount = Time.frameCount;

            Profiler.BeginSample("[UIParticle] Refresh");

            var mesh = MeshPool.Rent();

            foreach (var particle in _particles)
            {
                mesh.Clear(keepVertexLayout: true);

                try
                {
                    Profiler.BeginSample("[UIParticle] Bake mesh");
                    BakeMesh(particle, mesh);
                    Profiler.EndSample();

                    Profiler.BeginSample("[UIParticle] Set mesh to CanvasRenderer");
                    particle.canvasRenderer.SetMesh(mesh); // this will copy mesh data into CanvasRenderer.
                    Profiler.EndSample();
                }
                catch (Exception e) // just in case.
                {
                    Debug.LogException(e);
                }
            }

            MeshPool.Return(mesh);

            Profiler.EndSample();
        }

        private static Matrix4x4 GetScaledMatrix(ParticleSystem particle)
        {
            var t = particle.transform;

            var main = particle.main;
            var space = main.simulationSpace;
            if (space == ParticleSystemSimulationSpace.Custom && !main.customSimulationSpace)
                space = ParticleSystemSimulationSpace.Local;

            return space switch
            {
                ParticleSystemSimulationSpace.Local => Matrix4x4.Rotate(t.rotation).inverse * Matrix4x4.Scale(t.lossyScale).inverse,
                ParticleSystemSimulationSpace.World => t.worldToLocalMatrix,
                // #78: Support custom simulation space.
                ParticleSystemSimulationSpace.Custom => t.worldToLocalMatrix * Matrix4x4.Translate(main.customSimulationSpace.position),
                _ => Matrix4x4.identity
            };
        }

        private static void BakeMesh(UIParticle particle, Mesh mesh)
        {
            var ps = particle.Source;
            var pr = particle.SourceRenderer;
            var t = particle.transform;


            if (
                // No particle to render.
                (!ps.IsAlive() || ps.particleCount == 0)
                // #102: Do not bake particle system to mesh when the alpha is zero.
                || Mathf.Approximately(particle.canvasRenderer.GetInheritedAlpha(), 0))
            {
                particle.SetSubMeshCount(0);
                return;
            }

            // Get camera for baking mesh.
            // var cam = BakingCamera.GetCamera(particle.canvas);
            var cam = ResolveCamera(particle);
            if (!cam)
            {
                L.E($"UIParticle {particle.SafeName()} requires a camera to bake mesh.");
                return;
            }

            // Calc matrix.
            Profiler.BeginSample("[UIParticle] Bake Mesh > Calc matrix");
            var matrix = GetScaledMatrix(ps);
            Profiler.EndSample();

            // Bake main particles.
            var subMeshCount = 1;
            {
                Profiler.BeginSample("[UIParticle] Bake Mesh > Bake Main Particles");
                ref var ci = ref _cis[0];
                ci.transform = matrix;
                var subMesh = (ci.mesh ??= MeshPool.CreateDynamicMesh());
                subMesh.Clear(); // clean mesh first.
                pr.BakeMesh(subMesh, cam, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                Profiler.EndSample();
            }

            // Bake trails particles.
            if (ps.trails.enabled)
            {
                Profiler.BeginSample("[UIParticle] Bake Mesh > Bake Trails Particles");

                ref var ci = ref _cis[1];
                ci.transform = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local && ps.trails.worldSpace
                    ? matrix * Matrix4x4.Translate(-t.position)
                    : matrix;

                var subMesh = (ci.mesh ??= MeshPool.CreateDynamicMesh());
                subMesh.Clear(); // clean mesh first.
                try
                {
                    pr.BakeTrailsMesh(subMesh, cam, ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                    subMeshCount++;
                }
                catch (Exception e)
                {
                    L.E(e);
                }

                Profiler.EndSample();
            }

            // Set active indices.
            Profiler.BeginSample("[UIParticle] Bake Mesh > Set active indices");
            particle.SetSubMeshCount(subMeshCount);
            Profiler.EndSample();

            // Combine
            Profiler.BeginSample("[UIParticle] Bake Mesh > CombineMesh");
            if (subMeshCount is 1) mesh.CombineMeshes(_cis[0].mesh, _cis[0].transform);
            else mesh.CombineMeshes(_cis, mergeSubMeshes: false, useMatrices: true);
            mesh.RecalculateBounds();
            Profiler.EndSample();
            return;

            static Camera? ResolveCamera(UIParticle particle)
            {
                var cam = particle.canvas.worldCamera; // use camera directly.
#if UNITY_EDITOR
                if (!cam && Editing.Yes(particle)) cam = Camera.current;
#endif
                return cam;
            }
        }

#if UNITY_EDITOR
        private static bool _skipRefresh;

        static UIParticleUpdater() => UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state is UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _skipRefresh = true;
            }
            else if (state is UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                _skipRefresh = false;
            }
        }
#endif
    }
}