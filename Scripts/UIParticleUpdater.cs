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
        private static int _frameCount;

        static UIParticleUpdater() => Canvas.willRenderCanvases += Refresh;

        public static void Register(UIParticle particle)
        {
            Assert.IsFalse(_particles.Contains(particle), $"UIParticle {particle.SafeName()} is already registered.");
            _particles.Add(particle);
        }

        public static void Unregister(UIParticle particle)
        {
            Assert.IsTrue(_particles.Contains(particle), $"UIParticle {particle.SafeName()} is not registered.");
            _particles.Remove(particle);
        }

        private static void Refresh()
        {
            // Do not allow it to be called in the same frame.
            if (_frameCount == Time.frameCount) return;
            _frameCount = Time.frameCount;

            Profiler.BeginSample("[UIParticle] Refresh");
            for (var i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];

                try
                {
                    Profiler.BeginSample("[UIParticle] Bake mesh");
                    BakeMesh(particle);
                    Profiler.EndSample();

                    Profiler.BeginSample("[UIParticle] Set mesh to CanvasRenderer");
                    particle.canvasRenderer.SetMesh(particle.BakedMesh);
                    Profiler.EndSample();
                }
                catch (Exception e) // just in case.
                {
                    Debug.LogException(e);
                }
            }

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

        private static void BakeMesh(UIParticle particle)
        {
            var m = particle.BakedMesh!; // BakeMesh() is called by Refresh() which only handles enabled UIParticle.
            m.Clear(false); // clear mesh first.

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

            // Calc matrix.
            Profiler.BeginSample("[UIParticle] Bake Mesh > Calc matrix");
            var matrix = GetScaledMatrix(ps);
            Profiler.EndSample();

            // Get camera for baking mesh.
            // var cam = BakingCamera.GetCamera(particle.canvas);
            var cam = particle.canvas.worldCamera; // use camera directly.

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
            if (subMeshCount is 1) m.CombineMeshes(_cis[0].mesh, _cis[0].transform);
            else m.CombineMeshes(_cis, mergeSubMeshes: false, useMatrices: true);
            m.RecalculateBounds();
            Profiler.EndSample();
        }
    }
}