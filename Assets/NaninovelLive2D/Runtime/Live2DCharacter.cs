using System;
using System.Collections.Generic;
using System.Linq;
using Live2D.Cubism.Rendering;
using Naninovel.Commands;
using UniRx.Async;
using UnityEngine;
using UnityEngine.Rendering;

namespace Naninovel
{
    /// <summary>
    /// A <see cref="ICharacterActor"/> implementation using a <see cref="Live2DController"/> to represent an actor.
    /// </summary>
    /// <remarks>
    /// Live2D character prefab should have a <see cref="Live2DController"/> components attached to the root object.
    /// </remarks>
    [ActorResources(typeof(Live2DController), false)]
    public class Live2DCharacter : MonoBehaviourActor<CharacterMetadata>, ICharacterActor, LipSync.IReceiver
    {
        public override string Appearance { get => appearance; set => SetAppearance(value); }
        public override bool Visible { get => visible; set => SetVisibility(value); }
        public CharacterLookDirection LookDirection { get => lookDirection; set => SetLookDirection(value); }

        protected TransitionalRenderer TransitionalRenderer { get; private set; }
        protected Live2DController Live2DController { get; private set; }

        private readonly Dictionary<object, HashSet<string>> heldAppearances = new Dictionary<object, HashSet<string>>();
        private readonly List<Live2DDrawable> drawables = new List<Live2DDrawable>();
        private readonly CommandBuffer commandBuffer = new CommandBuffer();
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        private readonly IAudioManager audioManager;
        private readonly ITextPrinterManager textPrinterManager;
        private LocalizableResourceLoader<GameObject> prefabLoader;
        private RenderTexture renderTexture;
        private Vector2 renderCanvasSize;
        private string appearance;
        private bool visible;
        private CharacterLookDirection lookDirection;
        private bool lipSyncAllowed = true;

        public Live2DCharacter (string id, CharacterMetadata metadata)
            : base(id, metadata)
        {
            commandBuffer.name = $"Naninovel-RenderLive2D-{id}";
            audioManager = Engine.GetService<IAudioManager>();
            textPrinterManager = Engine.GetService<ITextPrinterManager>();
            textPrinterManager.OnPrintTextStarted += HandlePrintTextStarted;
            textPrinterManager.OnPrintTextFinished += HandlePrintTextFinished;
        }

        public override async UniTask InitializeAsync ()
        {
            await base.InitializeAsync();

            if (ActorMetadata.RenderTexture)
            {
                var textureRenderer = GameObject.AddComponent<TransitionalTextureRenderer>();
                textureRenderer.Initialize(ActorMetadata.CustomShader);
                textureRenderer.RenderTexture = ActorMetadata.RenderTexture;
                textureRenderer.CorrectAspect = ActorMetadata.CorrectRenderAspect;
                TransitionalRenderer = textureRenderer;
            }
            else
            {
                var spriteRenderer = GameObject.AddComponent<TransitionalSpriteRenderer>();
                spriteRenderer.Initialize(ActorMetadata.Pivot, ActorMetadata.PixelsPerUnit, ActorMetadata.CustomShader);
                TransitionalRenderer = spriteRenderer;
            }
            TransitionalRenderer.DepthPassEnabled = ActorMetadata.EnableDepthPass;
            TransitionalRenderer.DepthAlphaCutoff = ActorMetadata.DepthAlphaCutoff;

            var providerManager = Engine.GetService<IResourceProviderManager>();
            var localizationManager = Engine.GetService<ILocalizationManager>();
            prefabLoader = ActorMetadata.Loader.CreateLocalizableFor<GameObject>(providerManager, localizationManager);

            var prefabResource = await prefabLoader.LoadAsync(Id);
            if (!prefabResource.Valid) throw new Exception($"Failed to load Live2D model prefab for `{Id}` character. Make sure the resource is set up correctly in the character configuration.");
            Live2DController = Engine.Instantiate(prefabResource.Object).GetComponent<Live2DController>();
            Live2DController.gameObject.name = "Live2DModel";
            Live2DController.transform.SetParent(Transform);

            InitializeDrawables();
            SetVisibility(false);

            Engine.Behaviour.OnBehaviourUpdate += RenderLive2D;
        }

        public override UniTask ChangeAppearanceAsync (string appearance, float duration, EasingType easingType = default,
            Transition? transition = default, CancellationToken cancellationToken = default)
        {
            SetAppearance(appearance);
            return UniTask.CompletedTask;
        }

        public override async UniTask ChangeVisibilityAsync (bool visible, float duration, EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            this.visible = visible;

            await TransitionalRenderer.FadeToAsync(visible ? TintColor.a : 0, duration, easingType, cancellationToken);
        }
        
        public UniTask ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration, EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            SetLookDirection(lookDirection);
            return UniTask.CompletedTask;
        }
        
        public void AllowLipSync (bool active)
        {
            lipSyncAllowed = active;
        }

        public override async UniTask HoldResourcesAsync (string appearance, object holder)
        {
            if (!heldAppearances.ContainsKey(holder))
            {
                await prefabLoader.LoadAndHoldAsync(Id, holder);
                heldAppearances.Add(holder, new HashSet<string>());
            }

            heldAppearances[holder].Add(appearance);
        }

        public override void ReleaseResources (string appearance, object holder)
        {
            if (!heldAppearances.ContainsKey(holder)) return;
            
            heldAppearances[holder].Remove(appearance);
            if (heldAppearances.Count == 0)
            {
                heldAppearances.Remove(holder);
                prefabLoader?.Release(Id, holder);
            }
        }

        public override void Dispose ()
        {
            if (Engine.Behaviour != null)
                Engine.Behaviour.OnBehaviourUpdate -= RenderLive2D;
            
            if (textPrinterManager != null)
            {
                textPrinterManager.OnPrintTextStarted -= HandlePrintTextStarted;
                textPrinterManager.OnPrintTextFinished -= HandlePrintTextFinished;
            }

            if (renderTexture)
                RenderTexture.ReleaseTemporary(renderTexture);

            base.Dispose();

            prefabLoader?.UnloadAll();
        }

        protected virtual void SetAppearance (string appearance)
        {
            this.appearance = appearance;

            if (string.IsNullOrEmpty(appearance)) return;

            if (Live2DController)
                Live2DController.SetAppearance(appearance);
        }

        protected virtual void SetVisibility (bool visible) => ChangeVisibilityAsync(visible, 0).Forget();

        protected override Color GetBehaviourTintColor () => TransitionalRenderer.TintColor;

        protected override void SetBehaviourTintColor (Color tintColor)
        {
            if (!Visible) // Handle visibility-controlled alpha of the tint color.
                tintColor.a = TransitionalRenderer.TintColor.a;
            TransitionalRenderer.TintColor = tintColor;
        }
        
        protected virtual void SetLookDirection (CharacterLookDirection lookDirection)
        {
            this.lookDirection = lookDirection;

            if (Live2DController)
                Live2DController.SetLookDirection(lookDirection);
        }

        protected virtual void InitializeDrawables ()
        {
            Live2DController.CubismModel.ForceUpdateNow(); // Required to build meshes.

            drawables.Clear();
            drawables.AddRange(Live2DController.RenderController.Renderers
                .Select(cd => new Live2DDrawable(cd))
                .OrderBy(d => d.MeshRenderer.sortingOrder)
                .ThenByDescending(d => d.Transform.position.z));
            if (drawables.Count == 0) return;

            Live2DController.TryGetComponent<RenderCanvas>(out var renderCanvas);
            if (renderCanvas) renderCanvasSize = renderCanvas.Size;
            else
            {
                var bounds = Live2DController.RenderController.Renderers.GetMeshRendererBounds();
                renderCanvasSize = new Vector2(bounds.size.x, bounds.size.y);
            }
            
            // Align underlying model game object with the render texture position.
            Live2DController.transform.localPosition += new Vector3(0, renderCanvasSize.y / 2);
        }
        
        protected virtual void RenderLive2D ()
        {
            if (drawables.Count == 0)
            {
                Debug.LogWarning($"Can't render Live2D actor `{Id}`: drawables list is empty. Make sure the Live2D prefab is configured correctly.");
                return;
            }

            var renderDimensions = renderCanvasSize * ActorMetadata.PixelsPerUnit;
            var renderTextureSize = new Vector2Int(Mathf.RoundToInt(renderDimensions.x), Mathf.RoundToInt(renderDimensions.y));

            if (!renderTexture || renderTexture.width != renderTextureSize.x || renderTexture.height != renderTextureSize.y)
            {
                if (renderTexture)
                    RenderTexture.ReleaseTemporary(renderTexture);
                renderTexture = RenderTexture.GetTemporary(renderTextureSize.x, renderTextureSize.y);
                TransitionalRenderer.MainTexture = renderTexture;
            }
            
            var orthoMin = Vector3.Scale(-renderDimensions / 2f, Transform.localScale) + Live2DController.transform.position * ActorMetadata.PixelsPerUnit;
            var orthoMax = Vector3.Scale(renderDimensions / 2f, Transform.localScale) + Live2DController.transform.position * ActorMetadata.PixelsPerUnit;
            var orthoMatrix = Matrix4x4.Ortho(orthoMin.x, orthoMax.x, orthoMin.y, orthoMax.y, float.MinValue, float.MaxValue);
            var rotationMatrix = Matrix4x4.Rotate(Quaternion.Inverse(Transform.localRotation));
            
            commandBuffer.Clear();
            commandBuffer.SetRenderTarget(renderTexture);
            commandBuffer.ClearRenderTarget(true, true, Color.clear);
            commandBuffer.SetProjectionMatrix(orthoMatrix);

            for (int i = 0; i < drawables.Count; i++)
            {
                var drawable = drawables[i];
                if (!drawable.MeshRenderer.enabled) continue;
                var renderPosition = Live2DController.transform.TransformPoint(rotationMatrix // Compensate actor (parent game object) rotation.
                    .MultiplyPoint3x4(Live2DController.transform.InverseTransformPoint(drawable.Position)));
                var renderTransform = Matrix4x4.TRS(renderPosition * ActorMetadata.PixelsPerUnit, drawable.Rotation, drawable.Scale * ActorMetadata.PixelsPerUnit);
                drawable.MeshRenderer.GetPropertyBlock(propertyBlock);
                commandBuffer.DrawMesh(drawable.Mesh, renderTransform, drawable.RenderMaterial, 0, -1, propertyBlock);
            }
            
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }

        private void HandlePrintTextStarted (PrintTextArgs args)
        {
            if (!lipSyncAllowed || args.AuthorId != Id) return;

            if (Live2DController)
                Live2DController.SetIsSpeaking(true);

            var playedVoicePath = audioManager.GetPlayedVoicePath();
            if (!string.IsNullOrEmpty(playedVoicePath))
            {
                var track = audioManager.GetVoiceTrack(playedVoicePath);
                track.OnStop -= HandleVoiceClipStopped;
                track.OnStop += HandleVoiceClipStopped;
            }
            else textPrinterManager.OnPrintTextFinished += HandlePrintTextFinished;
        }

        private void HandlePrintTextFinished (PrintTextArgs args)
        {
            if (args.AuthorId != Id) return;

            if (Live2DController)
                Live2DController.SetIsSpeaking(false);
            textPrinterManager.OnPrintTextFinished -= HandlePrintTextFinished;
        }

        private void HandleVoiceClipStopped ()
        {
            if (Live2DController)
                Live2DController.SetIsSpeaking(false);
        }
    }
}
