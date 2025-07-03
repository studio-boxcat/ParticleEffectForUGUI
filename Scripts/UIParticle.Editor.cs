#if UNITY_EDITOR
#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Coffee.UIExtensions
{
    [GraphicPropertyHide(GraphicPropertyFlag.Color | GraphicPropertyFlag.Raycast)]
    public partial class UIParticle : ISelfValidator
    {
        protected override void Reset()
        {
            base.Reset();
            Source = GetComponent<ParticleSystem>();
        }

        void ISelfValidator.Validate(SelfValidationResult result)
        {
            if (raycastTarget)
                result.AddError("Raycast Target should be disabled.");

            using var _ = CompBuf.GetComponents(this, typeof(ParticleSystem), out var particles);
            if (particles.Count > 1)
                result.AddError("Multiple ParticleSystems are not supported. Please use only one ParticleSystem.");

            var ps = (ParticleSystem) particles[0];
            if (ps.RefNeq(Source))
                result.AddError("The ParticleSystem component is not the same as the one in m_Particles.");
            if (Mathf.Approximately(ps.transform.lossyScale.z, 0))
                result.AddError("The zero lossyScale.z will not render particles.");

            var pr = SourceRenderer;
            if (!pr)
                result.AddError("The ParticleSystemRenderer component is missing.");
            if (pr.enabled)
                result.AddError($"The ParticleSystemRenderer of {ps.name} is enabled.");
            if (!pr.sharedMaterial)
                result.AddError($"The ParticleSystemRenderer's sharedMaterial is not set. ({ps.name})");
            if (pr.sharedMaterial.RefNeq(m_Material))
                result.AddError($"The ParticleSystemRenderer's sharedMaterial is not the same as the one in m_Material. ({ps.name})");
            if (pr.sharedMaterial.mainTexture)
                result.AddError($"The ParticleSystemRenderer's sharedMaterial's mainTexture is not null. ({pr.sharedMaterial.name})");
            // #69: Editor crashes when mesh is set to null when `ParticleSystem.RenderMode = Mesh`
            if (pr.renderMode == ParticleSystemRenderMode.Mesh && !pr.mesh)
                result.AddError("The ParticleSystemRenderer's mesh is null. Please assign a mesh.");
            // #61: When `ParticleSystem.RenderMode = None`, an error occurs
            if (pr.renderMode == ParticleSystemRenderMode.None)
                result.AddError("The ParticleSystemRenderer's renderMode is None. Please set it to Billboard, Mesh, or Stretched Billboard.");

            // shape module
            var shapeType = ps.shape.shapeType;
            if (shapeType is ParticleSystemShapeType.Cone)
            {
                if (!IsValidConeShape(ps, out var detail))
                    result.AddError("The ParticleSystem with Cone shape is not setup properly: " + detail);
            }

            // texture sheet animation module
            var tsa = ps.textureSheetAnimation;
            if (tsa.enabled)
            {
                if (tsa.spriteCount is 0)
                    result.AddError($"The ParticleSystem's TextureSheetAnimationModule is enabled but spriteCount is 0. ({ps.name})");
                if (tsa.GetSprite(0).texture.RefNeq(_texture))
                    result.AddError($"The ParticleSystem's TextureSheetAnimationModule's first sprite's texture is not the same as the one in m_Texture. ({ps.name})");
                // Why?
                if (tsa is { mode: ParticleSystemAnimationMode.Sprites, uvChannelMask: 0 })
                    result.AddError($"The uvChannelMask of TextureSheetAnimationModule is not set to UV0. ({ps.name})");
            }

            // trail module
            if (ps.trails.enabled)
            {
                if (!pr.trailMaterial)
                    result.AddError($"The ParticleSystemRenderer's trailMaterial is not set. ({ps.name})");
            }

            return;

            static bool IsValidConeShape(ParticleSystem ps, out string? detail)
            {
                detail = null;

                var t = ps.transform;
                var shape = ps.shape;
                var rot = t.rotation.eulerAngles;
                var sr = shape.rotation; // shape rotation
                var ss = shape.scale; // shape scale

                // #1: heading up or down + scale.y == 0
                if ((((rot.x % 180f) is 90f or -90f) && rot is { y: 0, z: 0 })
                    && sr is { x: 0, z: 0 }
                    && ss == new Vector3(1, 0, 1))
                {
                    return true;
                }

                // #2: rotated around Z-axis + scale.x == 0
                if (rot == new Vector3(0, 0, 0)
                    && sr is { y: 90, z: 0 }
                    && ss is { x : 0 })
                {
                    return true;
                }

                detail = $"Rotation: {rot}, Shape Rotation: {sr}, Shape Scale: {ss}";
                return false;
            }
        }

        protected override void OnValidate()
        {
            base.OnValidate();

            // ParticleSystemRenderer -> UIParticle
            var mat = SourceRenderer.sharedMaterial;
            if (mat)
            {
                if (!m_Material) m_Material = mat;
                if (!_texture) _texture = (Texture2D) mat.mainTexture;
            }
        }

        private void OnInspectorTextureChanged()
        {
            SourceRenderer.SetPropertyBlock(GraphicsUtils.CreateMaterialPropertyBlock(mainTexture));
            Source.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Source.Play(true);
        }

        protected override void OnInspectorMaterialChanged()
        {
            SourceRenderer.sharedMaterial = m_Material;
            Source.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Source.Play(true);
        }
    }
}
#endif