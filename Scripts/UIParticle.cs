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
            if (_skipUpdatePlayModeChanged)
            {
                L.I("[UIParticle] Update() is skipped to prevent Unity crash on exiting play mode.");
                return;
            }

            if (_skipUpdateFocusChanged)
            {
                _skipUpdateFocusChanged = false; // reset the flag.
                L.I("[UIParticle] Update() is skipped once to prevent Unity crash on editor focus.");
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
            UIParticleBaker.BakeMesh(ps, SourceRenderer, m, cam, out var subMeshCount);
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
        private static bool _skipUpdatePlayModeChanged;
        private static bool _skipUpdateFocusChanged;

        static UIParticle()
        {
            /*
               #4  0x00000101d78ec4 in ParticleSystemRenderer::BakeMesh(PPtr<Mesh>, PPtr<Camera>, ParticleSystemBakeMeshOptions)
               ...
               #7  0x000004c70df968 in  Coffee.UIExtensions.UIParticleUpdater:BakeMesh (Coffee.UIExtensions.UIParticle,UnityEngine.Mesh) [{0x352dbded0} + 0x630] [/Users/jameskim/Develop/meow-tower/Packages/com.coffee.ui-particle/Scripts/UIParticleUpdater.cs :: 142u] (0x4c70df338 0x4c70e0238) [0x12f602a80 - Unity Child Domain]
               #8  0x000004c70dee30 in  Coffee.UIExtensions.UIParticleUpdater:Refresh () [{0x34d5de4b8} + 0x380] [/Users/jameskim/Develop/meow-tower/Packages/com.coffee.ui-particle/Scripts/UIParticleUpdater.cs :: 66u] (0x4c70deab0 0x4c70df058) [0x12f602a80 - Unity Child Domain]
               ...
               #25 0x000001021fd808 in PlayerLoopController::ExitPlayMode()
               #26 0x000001021f4260 in PlayerLoopController::SetIsPlaying(bool)
             */
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state is UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    _skipUpdatePlayModeChanged = true;
                }
                else if (state is UnityEditor.PlayModeStateChange.EnteredEditMode)
                {
                    _skipUpdatePlayModeChanged = false;
                }
            };

            /*
               0x349742fb8 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx/libmonobdwgc-2.0.dylib : mono_dump_native_crash_info
               0x34970531c - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx/libmonobdwgc-2.0.dylib : mono_handle_native_crash
               0x34968da78 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx/libmonobdwgc-2.0.dylib : mono_sigsegv_signal_handler_debug
               0x19cce96a4 - /usr/lib/system/libsystem_platform.dylib : _sigtramp
               0x1032f8498 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN14StackAllocatorIL13AllocatorMode0EE13TryDeallocateEPv
               0x1032e9de0 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN13MemoryManager10DeallocateEPvRK10MemLabelIdPKci
               0x1032e81c8 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z19free_alloc_internalPvRK10MemLabelIdPKci
               0x102e7c3d4 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN4core6vectorIhLm0EED2Ev
               0x1041c0ec4 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN22ParticleSystemRenderer8BakeMeshE4PPtrI4MeshES0_I6CameraE29ParticleSystemBakeMeshOptions
               0x10302dcac - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z38ParticleSystemRenderer_CUSTOM_BakeMeshP37ScriptingBackendNativeObjectPtrOpaqueS0_S0_29ParticleSystemBakeMeshOptions
               0x4bb3babc4 - Unknown
               ...
               0x4c7f996fc - Unknown
               0x349690d38 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx/libmonobdwgc-2.0.dylib : mono_jit_runtime_invoke
               0x34981721c - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx/libmonobdwgc-2.0.dylib : do_runtime_invoke
               0x34981713c - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx/libmonobdwgc-2.0.dylib : mono_runtime_invoke
               0x103a8bd54 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z23scripting_method_invoke18ScriptingMethodPtr18ScriptingObjectPtrR18ScriptingArgumentsP21ScriptingExceptionPtrb
               0x103a6658c - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN19ScriptingInvocation6InvokeEP21ScriptingExceptionPtrb
               0x103ff5290 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN2UI13CanvasManager18WillRenderCanvasesEv
               0x103ff779c - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZZN2UI23InitializeCanvasManagerEvEN37UIEventsWillRenderCanvasesRegistrator7ForwardEv
               0x1036b8e8c - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZZ23InitPlayerLoopCallbacksvEN45PostLateUpdatePlayerUpdateCanvasesRegistrator7ForwardEv
               0x1036a04ac - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z17ExecutePlayerLoopP22NativePlayerLoopSystem
               0x1036a04e0 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z17ExecutePlayerLoopP22NativePlayerLoopSystem
               0x1036a08dc - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z10PlayerLoopv
               0x1046441ac - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN16EditorPlayerLoop7ExecuteEv
               0x104644998 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN20PlayerLoopController19InternalUpdateSceneEbb
               0x10463ccd0 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN20PlayerLoopController31UpdateSceneIfNeededFromMainLoopEv
               0x10463a140 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _ZN11Application9TickTimerEv
               0x1058f1098 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : -[EditorApplication TickTimer]
               0x19e3a2fcc - /System/Library/Frameworks/Foundation.framework/Versions/C/Foundation : __NSFireTimer
               0x19cdb3c50 - /System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation : __CFRUNLOOP_IS_CALLING_OUT_TO_A_TIMER_CALLBACK_FUNCTION__
               0x19cdb3910 - /System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation : __CFRunLoopDoTimer
               0x19cdb344c - /System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation : __CFRunLoopDoTimers
               0x19cd99858 - /System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation : __CFRunLoopRun
               0x19cd98a98 - /System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation : CFRunLoopRunSpecific
               0x1a883b27c - /System/Library/Frameworks/Carbon.framework/Versions/A/Frameworks/HIToolbox.framework/Versions/A/HIToolbox : RunCurrentEventLoopInMode
               0x1a883e31c - /System/Library/Frameworks/Carbon.framework/Versions/A/Frameworks/HIToolbox.framework/Versions/A/HIToolbox : ReceiveNextEventCommon
               0x1a89c9484 - /System/Library/Frameworks/Carbon.framework/Versions/A/Frameworks/HIToolbox.framework/Versions/A/HIToolbox : _BlockUntilNextEventMatchingListInModeWithFilter
               0x1a0cbda34 - /System/Library/Frameworks/AppKit.framework/Versions/C/AppKit : _DPSNextEvent
               0x1a165c940 - /System/Library/Frameworks/AppKit.framework/Versions/C/AppKit : -[NSApplication(NSEventRouting) _nextEventMatchingEventMask:untilDate:inMode:dequeue:]
               0x1a0cb0be4 - /System/Library/Frameworks/AppKit.framework/Versions/C/AppKit : -[NSApplication run]
               0x1a0c872dc - /System/Library/Frameworks/AppKit.framework/Versions/C/AppKit : NSApplicationMain
               0x105908ed0 - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : _Z10EditorMainiPPKc
               0x1059091fc - /Applications/Unity/Hub/Editor/2022.3.60f1/Unity.app/Contents/MacOS/Unity : main
             */
            UnityEditor.EditorApplication.focusChanged += hasFocus =>
            {
                if (hasFocus)
                {
                    _skipUpdateFocusChanged = true;
                }
            };
        }
#endif
    }
}