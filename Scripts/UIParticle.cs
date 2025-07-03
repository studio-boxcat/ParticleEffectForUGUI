#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sirenix.OdinInspector;
using UnityEngine;
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
    [RequireComponent(typeof(ParticleSystem))]
    [RequireComponent(typeof(ParticleSystemRenderer))]
    public partial class UIParticle : MaskableGraphic
    {
        [SerializeField, Required, HideInInspector, ReadOnly]
        internal ParticleSystem Source = null!;

        [SerializeField, Required, AssetsOnly, OnValueChanged("OnInspectorTextureChanged")]
        private Texture2D _texture = null!;
        public override Texture mainTexture => _texture;

        [NonSerialized]
        private ParticleSystemRenderer? _sourceRenderer = null;
        internal ParticleSystemRenderer SourceRenderer => _sourceRenderer ??= Source.GetComponent<ParticleSystemRenderer>();

        private MaterialPropertyBlock? _mpb;
        private int _subMeshCount;

        public void SetTexture(Texture2D value)
        {
            if (_texture.RefEq(value))
                return;

            _texture = value;

            if (_mpb is not null)
            {
                _mpb.SetMainTex(value);
                SourceRenderer.SetPropertyBlock(_mpb);
            }

            SetMaterialDirty();
        }

        public override Material material
        {
            get => base.material;
            set
            {
                var changed = m_Material.RefEq(value);
                if (!changed) return;
                base.material = value;
                SourceRenderer.sharedMaterial = value;
            }
        }


        internal void SetSubMeshCount(int value)
        {
            if (_subMeshCount == value) return;
            _subMeshCount = value;
            UpdateMaterial();
        }

        protected override void UpdateMaterial()
        {
            // No mesh to render.
            if (_subMeshCount is 0 || !isActiveAndEnabled) return;

            // call base.UpdateMaterial() to ensure the main material is set.
            base.UpdateMaterial();

            // process the trail material.
            if (_subMeshCount is 2)
            {
                var r = SourceRenderer;
                var mat = r.trailMaterial;
                // depth is already set by base class. (UpdateMaterial() -> MaterialModifierUtils.ResolveMaterialForRendering() -> GetModifiedMaterial())
                var d = m_StencilDepth!.Value;
                if (d is not 0) mat = StencilMaterial.AddMaskable(r.trailMaterial, d); // make maskable.
                mat = MaterialModifierUtils.ResolveMaterialForRenderingExceptSelf(r, mat); // skip self, since it just for enabling stencil.

                var cr = canvasRenderer;
                cr.materialCount = 2;
                cr.SetMaterial(mat, 1);
            }
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            _subMeshCount = 0;

            base.OnEnable();

            UIParticleUpdater.Register(this);
        }

        private static readonly List<ParticleSystem> _psBuf = new();

        private IEnumerator Start()
        {
            if (_mpb is null)
            {
                _mpb = GraphicsUtils.CreateMaterialPropertyBlock(mainTexture);
                SourceRenderer.SetPropertyBlock(_mpb);
            }

            // #147: ParticleSystem creates Particles in wrong position during prewarm
            // #148: Particle Sub Emitter not showing when start game
            if (!NeedDelayToPlay(this))
                yield break;

            Source.Stop();
            Source.Clear();
            yield return null;

            Source.Play();
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

            base.OnDisable();
            canvasRenderer.Clear();
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