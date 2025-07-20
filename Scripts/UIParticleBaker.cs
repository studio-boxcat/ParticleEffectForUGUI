#nullable enable
using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;

namespace Coffee.UIExtensions
{
    internal static class UIParticleBaker
    {
        private static CombineInstance[] _cis = new CombineInstance[2]; // temporary buffer for CombineMeshes.

        public static void BakeMesh(
            ParticleSystem ps, ParticleSystemRenderer pr,
            Mesh mesh, Camera cam, out int subMeshCount)
        {
            Assert.IsTrue(mesh.vertexCount is 0, "UIParticleBaker.BakeMesh() requires an empty mesh to bake particles.");
            Assert.IsTrue(ps.IsAlive() || ps.isPlaying, "UIParticleBaker.BakeMesh() requires a ParticleSystem that is alive to bake particles.");
            Assert.IsTrue(ps.particleCount > 0, "UIParticleBaker.BakeMesh() requires a ParticleSystem that has particles to bake.");

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
                    ? matrix * Matrix4x4.Translate(-pr.transform.position)
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