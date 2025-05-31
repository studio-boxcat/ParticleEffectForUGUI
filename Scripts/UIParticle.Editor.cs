#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Coffee.UIParticleExtensions;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Coffee.UIExtensions
{
    [GraphicPropertyHide(GraphicPropertyFlag.Color | GraphicPropertyFlag.Material | GraphicPropertyFlag.Raycast)]
    public partial class UIParticle : ISelfValidator
    {
        [ShowInInspector, TableList(HideToolbar = true, IsReadOnly = true, AlwaysExpanded = true)]
        [CustomContextMenu("Collect", nameof(Editor_CollectParticles))]
        ParticleSystemEditorControl[] _particles
        {
            get => m_Particles.Select(x => new ParticleSystemEditorControl(x)).ToArray();
            set => throw new NotSupportedException("Use Collect button to collect particles.");
        }

        protected override void OnValidate()
        {
            SetLayoutDirty();
            SetVerticesDirty();
            m_ShouldRecalculateStencil = true;
            RecalculateClipping();
        }

        protected override void Reset()
        {
            base.Reset();
            Editor_CollectParticles();
        }

        void Editor_CollectParticles()
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
            if (raycastTarget)
                result.AddError("Raycast Target should be disabled.");

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
                if (tsa is { mode: ParticleSystemAnimationMode.Sprites, uvChannelMask: 0 })
                {
                    result.AddError($"The uvChannelMask of TextureSheetAnimationModule is not set to UV0. ({ps.name})");
                    return;
                }

                if (Mathf.Approximately(ps.transform.lossyScale.z, 0))
                {
                    result.AddError("The zero lossyScale.z will not render particles.");
                    return;
                }
            }
        }

        private class ParticleSystemEditorControl
        {
            public ParticleSystemEditorControl(ParticleSystem particle)
            {
                Particle = particle;
            }

            [ShowInInspector]
            public ParticleSystem Particle;

            [ShowInInspector]
            public Material Material
            {
                get => Particle.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                set
                {
                    Particle.GetComponent<ParticleSystemRenderer>().sharedMaterial = value;
                    Particle.GetComponent<UIParticle>().SetMaterialDirty();
                }
            }
        }
    }
}
#endif