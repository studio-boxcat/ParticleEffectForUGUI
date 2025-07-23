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

        [SerializeField, Required, AssetsOnly]
        [OnValueChanged("OnInspectorTextureChanged")]
        private Texture2D _texture = null!;
        public override Texture mainTexture => _texture;

        [NonSerialized]
        private ParticleSystemRenderer? _sourceRenderer;
        internal ParticleSystemRenderer SourceRenderer => _sourceRenderer ??= Source.GetComponent<ParticleSystemRenderer>();

        private int _subMeshCount;

        private static readonly List<ParticleSystem> _psBuf = new();

        private IEnumerator Start()
        {
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
                target.GetComponentsInChildren(includeInactive: false, _psBuf); // mostly no children in the UIParticle.

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

        private void Update() => SetVerticesDirty(); // no good way to detect particle system update, so just always mark as dirty.

        protected override void UpdateGeometry()
        {
#if UNITY_EDITOR
            if (_skipUpdate)
            {
                // #4  0x00000101d78ec4 in ParticleSystemRenderer::BakeMesh(PPtr<Mesh>, PPtr<Camera>, ParticleSystemBakeMeshOptions)
                // ...
                // #7  0x000004c70df968 in  Coffee.UIExtensions.UIParticleUpdater:BakeMesh (Coffee.UIExtensions.UIParticle,UnityEngine.Mesh) [{0x352dbded0} + 0x630] [/Users/jameskim/Develop/meow-tower/Packages/com.coffee.ui-particle/Scripts/UIParticleUpdater.cs :: 142u] (0x4c70df338 0x4c70e0238) [0x12f602a80 - Unity Child Domain]
                // #8  0x000004c70dee30 in  Coffee.UIExtensions.UIParticleUpdater:Refresh () [{0x34d5de4b8} + 0x380] [/Users/jameskim/Develop/meow-tower/Packages/com.coffee.ui-particle/Scripts/UIParticleUpdater.cs :: 66u] (0x4c70deab0 0x4c70df058) [0x12f602a80 - Unity Child Domain]
                // ...
                // #25 0x000001021fd808 in PlayerLoopController::ExitPlayMode()
                // #26 0x000001021f4260 in PlayerLoopController::SetIsPlaying(bool)
                L.I("[UIParticle] Update() is skipped to prevent Unity crash.");
                return;
            }
#endif

            var ps = Source;
            var cr = canvasRenderer;
            if ((!ps.IsAlive() && !ps.isPlaying) // not playing. for timeline, isPlaying is always false but IsAlive() returns true only when the ParticleSystem needs to be updated.
                || ps.particleCount == 0 // no particles to render.
                || Mathf.Approximately(cr.GetInheritedAlpha(), 0)) // #102: Do not bake particle system to mesh when the alpha is zero.
            {
                // L.I($"[UIParticle] ParticleSystem is not alive or not playing or no particles: " +
                //     $"isAlive={ps.IsAlive()}, isPlaying={ps.isPlaying}, particleCount={ps.particleCount}, inheritedAlpha={cr.GetInheritedAlpha()}");
                cr.SetMesh(MeshPool.Empty);
                return;
            }

            // L.I($"[UIParticle] Update() is called. Baking mesh: ps={ps.name}, alive={ps.IsAlive()}, playing={ps.isPlaying}, " +
            //     $"particleCount={ps.particleCount}, inheritedAlpha={cr.GetInheritedAlpha()}");

            // Get camera for baking mesh.
            var cam = CanvasUtils.ResolveWorldCamera(this)!;
            if (!cam) // is this necessary?
            {
                L.W($"[UIParticle] No camera found: {name}");
                return; // should I keep the previous mesh?
            }


            // For particle, we don't need layout, mesh modification or so.
            var m = MeshPool.Rent();
            UIParticleBaker.BakeMesh(ps, SourceRenderer, m, cam!, out var subMeshCount);
            cr.SetMesh(m);
            MeshPool.Return(m);

            if (_subMeshCount != subMeshCount)
            {
                _subMeshCount = subMeshCount;
                // XXX: avoid SetMaterialDirty() enqueue this graphic again to the CanvasUpdateRegistry.
                // UpdateGeometry() is called by Graphic.Rebuild() which is called by CanvasUpdateRegistry.PerformUpdate().
                // changing subMeshCount is super rare case anyway.
                UpdateMaterial();
            }
        }

        public void SetTexture(Texture2D value)
        {
            if (_texture.RefEq(value)) return;
            _texture = value;
            SetMaterialDirty();
        }

        protected override void UpdateMaterial()
        {
            // call base.UpdateMaterial() to ensure the main material is set.
            base.UpdateMaterial();

            // process the trail material. (need to be tested)
            // TODO: must remove the old stencil material.
            if (IsActive() && _subMeshCount is 2)
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

        protected override void OnDidApplyAnimationProperties()
        {
            // do nothing. ParticleSystem itself handles animation properties.
        }

#if UNITY_EDITOR
        private static bool _skipUpdate;

        static UIParticle() => UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state is UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _skipUpdate = true;
            }
            else if (state is UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                _skipUpdate = false;
            }
        }
#endif
    }
}