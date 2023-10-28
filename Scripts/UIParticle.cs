using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Coffee.UIParticleExtensions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Sirenix.OdinInspector;

[assembly: InternalsVisibleTo("Coffee.UIParticle.Editor")]

namespace Coffee.UIExtensions
{
    /// <summary>
    /// Render maskable and sortable particle effect ,without Camera, RenderTexture or Canvas.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIParticle : MaskableGraphic
#if UNITY_EDITOR
        , ISelfValidator
#endif
    {
        [Tooltip("Particles")] [SerializeField]
        [ListDrawerSettings(IsReadOnly = true), CustomContextMenu("Collect", "Editor_CollectParticles")]
        private List<ParticleSystem> m_Particles = new List<ParticleSystem>();

        private DrivenRectTransformTracker _tracker;
        private Mesh _bakedMesh;
        private readonly List<Material> _modifiedMaterials = new List<Material>();
        private readonly List<Material> _maskMaterials = new List<Material>();
        private readonly List<bool> _activeMeshIndices = new List<bool>();
        private static readonly List<Material> s_TempMaterials = new List<Material>(2);
        private static readonly List<Material> s_PrevMaskMaterials = new List<Material>();
        private static readonly List<Material> s_PrevModifiedMaterials = new List<Material>();
        private static readonly List<IMaterialModifier> s_Components = new List<IMaterialModifier>();
        private static readonly List<ParticleSystem> s_ParticleSystems = new List<ParticleSystem>();


        /// <summary>
        /// Should this graphic be considered a target for raycasting?
        /// </summary>
        public override bool raycastTarget
        {
            get => false;
            set { }
        }

        internal Mesh bakedMesh => _bakedMesh;

        public List<ParticleSystem> particles => m_Particles;

        public IEnumerable<Material> materials => _modifiedMaterials;

        public override Material materialForRendering => canvasRenderer.GetMaterial(0);

        public List<bool> activeMeshIndices
        {
            get => _activeMeshIndices;
            set
            {
                if (_activeMeshIndices.SequenceEqualFast(value)) return;
                _activeMeshIndices.Clear();
                _activeMeshIndices.AddRange(value);
                UpdateMaterial();
            }
        }

        internal Vector3 cachedPosition { get; set; }

        public void Play() => particles.Exec(p => p.Play());
        public void Pause() => particles.Exec(p => p.Pause());
        public void Stop() => particles.Exec(p => p.Stop());
        public void Clear() => particles.Exec(p => p.Clear());

        protected override void UpdateMaterial()
        {
            // Clear mask materials.
            s_PrevMaskMaterials.AddRange(_maskMaterials);
            _maskMaterials.Clear();

            // Clear modified materials.
            s_PrevModifiedMaterials.AddRange(_modifiedMaterials);
            _modifiedMaterials.Clear();

            // Recalculate stencil value.
            if (m_ShouldRecalculateStencil)
            {
                var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
                m_StencilValue = maskable ? MaskUtilities.GetStencilDepth(transform, rootCanvas) : 0;
                m_ShouldRecalculateStencil = false;
            }

            // No mesh to render.
            var count = activeMeshIndices.CountFast();
            if (count == 0 || !isActiveAndEnabled || particles.Count == 0)
            {
                canvasRenderer.Clear();
                ClearPreviousMaterials();
                return;
            }

            //
            GetComponents(s_Components);
            var materialCount = Mathf.Min(8, count);
            canvasRenderer.materialCount = materialCount;
            var j = 0;
            for (var i = 0; i < particles.Count; i++)
            {
                if (materialCount <= j) break;
                var ps = particles[i];
                if (!ps) continue;

                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.GetSharedMaterials(s_TempMaterials);

                // Main
                var index = i * 2;
                if (activeMeshIndices.Count <= index) break;
                if (activeMeshIndices[index] && 0 < s_TempMaterials.Count)
                {
                    var mat = GetModifiedMaterial(s_TempMaterials[0], ps.GetTextureForSprite());
                    for (var k = 1; k < s_Components.Count; k++)
                        mat = s_Components[k].GetModifiedMaterial(mat);
                    canvasRenderer.SetMaterial(mat, j);
                    j++;
                }

                // Trails
                index++;
                if (activeMeshIndices.Count <= index || materialCount <= j) break;
                if (activeMeshIndices[index] && 1 < s_TempMaterials.Count)
                {
                    var mat = GetModifiedMaterial(s_TempMaterials[1], null);
                    for (var k = 1; k < s_Components.Count; k++)
                        mat = s_Components[k].GetModifiedMaterial(mat);
                    canvasRenderer.SetMaterial(mat, j++);
                }
            }

            ClearPreviousMaterials();
        }

        private void ClearMaterials()
        {
            // Clear mask materials.
            s_PrevMaskMaterials.AddRange(_maskMaterials);
            _maskMaterials.Clear();

            // Clear modified materials.
            s_PrevModifiedMaterials.AddRange(_modifiedMaterials);
            _modifiedMaterials.Clear();

            canvasRenderer.Clear();
            ClearPreviousMaterials();
        }

        private void ClearPreviousMaterials()
        {
            foreach (var m in s_PrevMaskMaterials)
                StencilMaterial.Remove(m);
            s_PrevMaskMaterials.Clear();

            foreach (var m in s_PrevModifiedMaterials)
                ModifiedMaterial.Remove(m);
            s_PrevModifiedMaterials.Clear();
        }

        private Material GetModifiedMaterial(Material baseMaterial, Texture2D texture)
        {
            if (0 < m_StencilValue)
            {
                baseMaterial = StencilMaterial.Add(baseMaterial, (1 << m_StencilValue) - 1, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, (1 << m_StencilValue) - 1, 0);
                _maskMaterials.Add(baseMaterial);
            }

            if (texture == null) return baseMaterial;

            baseMaterial = ModifiedMaterial.Add(baseMaterial, texture, 0);
            _modifiedMaterials.Add(baseMaterial);

            return baseMaterial;
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            activeMeshIndices.Clear();

            UIParticleUpdater.Register(this);

            // Create objects.
            _bakedMesh = MeshPool.Rent();

            base.OnEnable();
        }

        private new IEnumerator Start()
        {
            // #147: ParticleSystem creates Particles in wrong position during prewarm
            // #148: Particle Sub Emitter not showing when start game
            var delayToPlay = particles.AnyFast(ps =>
            {
                ps.GetComponentsInChildren(false, s_ParticleSystems);
                return s_ParticleSystems.AnyFast(p => p.isPlaying && (p.subEmitters.enabled || p.main.prewarm));
            });
            s_ParticleSystems.Clear();
            if (!delayToPlay) yield break;

            Stop();
            Clear();
            yield return null;

            Play();
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        protected override void OnDisable()
        {
            UIParticleUpdater.Unregister(this);
            _tracker.Clear();

            // Destroy object.
            MeshPool.Return(_bakedMesh);
            _bakedMesh = null;

            base.OnDisable();
            ClearMaterials();
        }

        /// <summary>
        /// Call to update the geometry of the Graphic onto the CanvasRenderer.
        /// </summary>
        protected override void UpdateGeometry()
        {
        }

        /// <summary>
        /// Callback for when properties have been changed by animation.
        /// </summary>
        protected override void OnDidApplyAnimationProperties()
        {
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            SetLayoutDirty();
            SetVerticesDirty();
            m_ShouldRecalculateStencil = true;
            RecalculateClipping();
        }

        public void Editor_CollectParticles()
        {
            CollectParticles(this, particles);
        }

        static void CollectParticles(UIParticle target, List<ParticleSystem> buffer)
        {
            target.GetComponentsInChildren(true, buffer);
            buffer.RemoveAll(x => x.GetComponentInParent<UIParticle>(true) != target);
            buffer.SortForRendering(target.transform);
        }

        static readonly List<ParticleSystem> _particleSystemBuf = new();

        void ISelfValidator.Validate(SelfValidationResult result)
        {
            CollectParticles(this, _particleSystemBuf);

            if (_particleSystemBuf.Count != particles.Count)
            {
                result.AddError($"The number of particles is different. ({particles.Count} != {_particleSystemBuf.Count})");
                return;
            }

            for (var i = 0; i < particles.Count; i++)
            {
                if (particles[i] == _particleSystemBuf[i]) continue;
                result.AddError($"The particle is different. ({particles[i].name} != {_particleSystemBuf[i].name})");
                return;
            }

            foreach (var ps in particles)
            {
                if (ps.TryGetComponent<ParticleSystemRenderer>(out var renderer) && renderer.enabled)
                {
                    result.AddError($"The ParticleSystemRenderer of {ps.name} is enabled.");
                    return;
                }

                var tsa = ps.textureSheetAnimation;
                if (tsa.mode == ParticleSystemAnimationMode.Sprites && tsa.uvChannelMask == (UVChannelFlags) 0)
                {
                    result.AddError($"The uvChannelMask of TextureSheetAnimationModule is not set to UV0. ({ps.name})");
                    return;
                }
            }
        }

        protected override bool m_Material_ShowIf() => false;
        protected override bool m_Color_ShowIf() => false;
        protected override bool m_RaycastTarget_ShowIf() => false;
        protected override bool m_RaycastPadding_ShowIf() => false;
#endif
    }
}