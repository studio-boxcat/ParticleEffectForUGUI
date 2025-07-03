#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Coffee.UIParticleExtensions;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[assembly: InternalsVisibleTo("Coffee.UIParticle.Editor")]

namespace Coffee.UIExtensions
{
    /// <summary>
    /// Render maskable and sortable particle effect ,without Camera, RenderTexture or Canvas.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public partial class UIParticle : MaskableGraphic
    {
        [SerializeField, Required, HideInInspector, RequiredListLength(1)]
        private ParticleSystem[] m_Particles = null!;

        private Mesh? _bakedMesh;
        private readonly List<Material> _maskMats = new();
        private readonly List<Material> _modMats = new();
        private int _subMeshCount;

        private static readonly List<Material> _garbageMaskMats = new();
        private static readonly List<Material> _garbageModMats = new();
        private static readonly List<IMaterialModifier> _matModBuf = new();
        private static readonly List<ParticleSystem> _psBuf = new();

        public ParticleSystem particle => m_Particles[0];
        internal Mesh? bakedMesh => _bakedMesh;
        public override Material materialForRendering => canvasRenderer.GetMaterial(0);

        internal int subMeshCount
        {
            get => _subMeshCount;
            set
            {
                if (_subMeshCount == value) return;
                _subMeshCount = value;
                UpdateMaterial();
            }
        }

        protected override void UpdateMaterial()
        {
            var cr = canvasRenderer; // cache for performance.

            // Clear old materials.
            TransferToGarbage(_maskMats, _modMats);

            // Recalculate stencil value.
            if (m_StencilDepthDirty)
            {
                m_StencilDepth = maskable ? MaskUtilities.GetStencilDepth(transform) : 0;
                m_StencilDepthDirty = false;
            }

            // No mesh to render.
            if (_subMeshCount is 0 || !isActiveAndEnabled)
            {
                cr.Clear();
                PurgeGarbage();
                return;
            }

            //
            cr.materialCount = _subMeshCount; // max 2
            GetComponents(_matModBuf);
            {
                var ps = particle;
                var r = ps.GetComponent<ParticleSystemRenderer>();

                // Main - always enabled.
                {
                    var mat = GetModifiedMaterial(r.sharedMaterial, ps.GetTextureForSprite());
                    for (var k = 1; k < _matModBuf.Count; k++) // skip 0, because it's itself.
                        mat = _matModBuf[k].GetModifiedMaterial(mat);
                    cr.SetMaterial(mat, 0);
                }

                // Trail - trail enabled only if main is enabled
                if (_subMeshCount > 1)
                {
                    var mat = GetModifiedMaterial(r.trailMaterial, null);
                    for (var k = 1; k < _matModBuf.Count; k++) // skip 0, because it's itself.
                        mat = _matModBuf[k].GetModifiedMaterial(mat);
                    cr.SetMaterial(mat, 1);
                }
            }

            PurgeGarbage();
        }

        private void ClearMaterials()
        {
            canvasRenderer.Clear();
            TransferToGarbage(_maskMats, _modMats);
            PurgeGarbage();
        }

        private static void TransferToGarbage(List<Material> maskMats, List<Material> modMats)
        {
            _garbageMaskMats.AddRange(maskMats);
            maskMats.Clear();
            _garbageModMats.AddRange(modMats);
            modMats.Clear();
        }

        private static void PurgeGarbage()
        {
            foreach (var m in _garbageMaskMats)
                StencilMaterial.Remove(m);
            _garbageMaskMats.Clear();

            foreach (var m in _garbageModMats)
                ModifiedMaterial.Remove(m);
            _garbageModMats.Clear();
        }

        private Material GetModifiedMaterial(Material baseMaterial, Texture2D? texture)
        {
            // enable stencil first.
            if (0 < m_StencilDepth)
            {
                baseMaterial = StencilMaterial.Add(baseMaterial, (1 << m_StencilDepth) - 1, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, (1 << m_StencilDepth) - 1, 0);
                _maskMats.Add(baseMaterial);
            }

            // if there's texture, modify material to use it.
            if (texture)
            {
                baseMaterial = ModifiedMaterial.Add(baseMaterial, texture, 0);
                _modMats.Add(baseMaterial);
            }

            return baseMaterial;
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            _subMeshCount = 0;

            UIParticleUpdater.Register(this);

            // Create objects.
            _bakedMesh = MeshPool.Rent();

            base.OnEnable();
        }

        private IEnumerator Start()
        {
            // #147: ParticleSystem creates Particles in wrong position during prewarm
            // #148: Particle Sub Emitter not showing when start game
            if (!NeedDelayToPlay(this))
                yield break;

            particle.Stop();
            particle.Clear();
            yield return null;

            particle.Play();
            yield break;

            static bool NeedDelayToPlay(Component target)
            {
                target.GetComponentsInChildren(false, _psBuf);

                for (var i = 0; i < _psBuf.Count; ++i)
                {
                    var p = _psBuf[i];
                    if (p.isPlaying && (p.subEmitters.enabled || p.main.prewarm))
                    {
                        _psBuf.Clear();
                        return true;
                    }
                }

                _psBuf.Clear();
                return false;
            }
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        protected override void OnDisable()
        {
            UIParticleUpdater.Unregister(this);

            // Destroy object.
            MeshPool.Return(_bakedMesh!); // Rented from OnEnable().
            _bakedMesh = null;

            base.OnDisable();
            ClearMaterials();
        }

        /// <summary>
        /// Call to update the geometry of the Graphic onto the CanvasRenderer.
        /// </summary>
        protected override void UpdateGeometry()
        {
            // UIParticleUpdater.cs will invoke CanvasRenderer.SetMesh(bakedMesh);
        }

        /// <summary>
        /// Callback for when properties have been changed by animation.
        /// </summary>
        protected override void OnDidApplyAnimationProperties() { }
    }
}