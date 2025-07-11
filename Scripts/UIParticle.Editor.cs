#if UNITY_EDITOR
#nullable enable
using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Coffee.UIExtensions
{
    [GraphicPropertyHide(GraphicPropertyFlag.Color | GraphicPropertyFlag.Raycast)]
    public partial class UIParticle : ISelfValidator
    {
        // editor only initializer.
        private void Awake()
        {
            if (!_texture) _texture = AssetDatabaseUtils.LoadTextureWithGUID("0311aa56f4c25498ebd31febe866c3cf")!; // Particle_Bling_Y
            if (!m_Material) m_Material = AssetDatabaseUtils.LoadMaterialWithGUID("d8984a0a3a8bb45d48946817c2152326");

            if (!Source)
            {
                Source = GetComponent<ParticleSystem>();
                var main = Source.main;
                main.startSpeed = 0.3f;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var shape = Source.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                SourceRenderer.sharedMaterial = m_Material;
            }
        }

        [ShowInInspector, FoldoutGroup("Advanced"), PropertyOrder(100)]
        private Material _partialMainMaterial => SourceRenderer.sharedMaterial;
        [ShowInInspector, FoldoutGroup("Advanced"), PropertyOrder(100)]
        private Material _partialTrailMaterial => SourceRenderer.trailMaterial;

        [Button(DirtyOnClick = false), ButtonGroup(order: 1000)]
        private void Restart()
        {
            Source.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Source.Play(true);
        }

        [Button("Emit 10", DirtyOnClick = false), ButtonGroup]
        private void Emit10()
        {
            Source.Emit(10);
        }

        private void OnInspectorTextureChanged()
        {
            UpdateTexture();
            Restart();
        }

        protected override void OnInspectorMaterialChanged()
        {
            SourceRenderer.sharedMaterial = m_Material;
            Restart();
        }

        void ISelfValidator.Validate(SelfValidationResult result)
        {
            if (raycastTarget)
                result.AddError("Raycast Target should be disabled.");

            using var _ = CompBuf.GetComponents(this, typeof(ParticleSystem), out var particles);
            if (particles.Count > 1)
                result.AddError("Multiple ParticleSystems are not supported. Please use only one ParticleSystem.");

            var ps = (ParticleSystem) particles[0];
            if (ps.RefNq(Source))
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
            if (pr.sharedMaterial.RefNq(m_Material))
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
            if (shapeType is (ParticleSystemShapeType.Cone or ParticleSystemShapeType.Box)
                && !IsValid3DShape(ps, out var detail))
            {
                result.AddError("The ParticleSystem with 3D shape is not setup properly: " + detail);
            }

            // texture sheet animation module
            var tsa = ps.textureSheetAnimation;
            if (tsa.enabled)
            {
                if (tsa.mode is not ParticleSystemAnimationMode.Grid)
                    result.AddError($"The ParticleSystem's TextureSheetAnimationModule mode is not set to Grid. ({ps.name})");
            }

            // trail module
            if (ps.trails.enabled)
            {
                if (!pr.trailMaterial)
                    result.AddError($"The ParticleSystemRenderer's trailMaterial is not set. ({ps.name})");
            }

            var noise = ps.noise;
            if (noise.enabled)
            {
                if (!noise.separateAxes || !IsZero(noise.strengthZ))
                    result.AddError($"The ParticleSystem's NoiseModule is not setup properly. Please set separateAxes to true and strengthZ to zero. ({ps.name})");
            }

            return;

            static bool IsValid3DShape(ParticleSystem ps, out string? detail)
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
                if (rot.EE0()
                    && sr is { y: 90, z: 0 }
                    && ss is { x: 0 })
                {
                    return true;
                }

                // #3: rotated around X-axis + scale.y == 0
                if (rot.EE0()
                    && sr is { x: 90, y: 0, z: 0 }
                    && ss is { y: 0 })
                {
                    return true;
                }

                detail = $"Rotation: {rot}, Shape Rotation: {sr}, Shape Scale: {ss}";
                return false;
            }

            static bool IsZero(ParticleSystem.MinMaxCurve value)
            {
                return value.mode switch
                {
                    ParticleSystemCurveMode.Constant => value.constant.EE0(),
                    ParticleSystemCurveMode.Curve => false, // Cannot determine if the curve is zero without evaluating it.
                    ParticleSystemCurveMode.TwoCurves => false, // Cannot determine if the curves are zero without evaluating them.
                    ParticleSystemCurveMode.TwoConstants => value.constantMin.EE0() && value.constantMax.EE0(),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }
    }
}
#endif