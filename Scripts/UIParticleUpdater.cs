#nullable enable
using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Coffee.UIExtensions
{
    internal static class UIParticleUpdater
    {
        private static CombineInstance[] _cis = new CombineInstance[2]; // temporary buffer for CombineMeshes.

        public static void BakeMesh(UIParticle particle, Mesh mesh, out int subMeshCount)
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
                subMeshCount = 0;
                return;
            }

            // Get camera for baking mesh.
            // var cam = BakingCamera.GetCamera(particle.canvas);
            var cam = ResolveCamera(particle);
            if (!cam)
            {
                L.E($"UIParticle {particle.SafeName()} requires a camera to bake mesh.");
                subMeshCount = 0;
                return;
            }

            // Calc matrix.
            Profiler.BeginSample("[UIParticle] Bake Mesh > Calc matrix");
            var matrix = GetScaledMatrix(ps);
            Profiler.EndSample();

            // Bake main particles.
            subMeshCount = 1;
            {
                Profiler.BeginSample("[UIParticle] Bake Mesh > Bake Main Particles");
                ref var ci = ref _cis[0];
                ci.transform = matrix;
                var subMesh = (ci.mesh ??= MeshPool.CreateDynamicMesh());
                // XXX: BakeMesh() will overwrite the mesh data.
                // subMesh.Clear(); // clean mesh first.
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
                // XXX: BakeMesh() will overwrite the mesh data.
                // subMesh.Clear(); // clean mesh first.
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
    }
}