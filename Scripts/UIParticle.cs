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
        [TextureTypeDefault, OnValueChanged("OnInspectorTextureChanged")]
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

        // Canvas.willRenderCanvases fires much later in the same frame, just before Unity starts drawing the UI.
        // https://docs.unity3d.com/6000.1/Documentation/Manual/execution-order.html
        // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Canvas-willRenderCanvases.html
        private void Update()
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

            if (!Source.isPlaying) return;

            // For particle, we don't need layout, mesh modification or so.
            var m = MeshPool.Rent();
            UIParticleBaker.BakeMesh(this, m, out var subMeshCount);
            canvasRenderer.SetMesh(m);
            MeshPool.Return(m);

            if (_subMeshCount != subMeshCount)
            {
                _subMeshCount = subMeshCount;
                UpdateMaterial(); // update material immediately.
            }
        }

        protected override void UpdateGeometry()
        {
            // vertex is directly updated in Update().
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