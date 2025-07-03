#if UNITY_EDITOR
#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Coffee.UIExtensions
{
    [GraphicPropertyHide(GraphicPropertyFlag.Color | GraphicPropertyFlag.Material | GraphicPropertyFlag.Raycast)]
    public partial class UIParticle : ISelfValidator
    {
        [ShowInInspector] // only for editor inspector.
        private Material _material
        {
            get => SourceRenderer.sharedMaterial;
            set
            {
                SourceRenderer.sharedMaterial = value;
                SetMaterialDirty();
            }
        }

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
            if (tsa is { mode: ParticleSystemAnimationMode.Sprites, uvChannelMask: 0 })
                result.AddError($"The uvChannelMask of TextureSheetAnimationModule is not set to UV0. ({ps.name})");

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
    }
}
#endif