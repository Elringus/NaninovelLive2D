﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private Vector2 canvasSize;
        private Vector2 canvasOffset;
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

            TransitionalRenderer = InitializeRenderer(ActorMetadata, GameObject);
            prefabLoader = InitializeLoader(ActorMetadata);
            Live2DController = await InitializeControllerAsync(prefabLoader, Id, Transform);
            InitializeDrawables(drawables, Live2DController);
            (canvasSize, canvasOffset) = InitializeCanvas(Live2DController);
            
            // Align underlying model game object with the render texture position.
            Live2DController.transform.localPosition += new Vector3(0, canvasSize.y / 2);
            
            SetVisibility(false);

            Engine.Behaviour.OnBehaviourUpdate += RenderLive2D;
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

        public override UniTask ChangeAppearanceAsync (string appearance, float duration, EasingType easingType = default,
            Transition? transition = default, CancellationToken cancellationToken = default)
        {
            SetAppearance(appearance);
            return UniTask.CompletedTask;
        }

        public override async UniTask ChangeVisibilityAsync (bool visible, float duration, EasingType easingType = default, 
            CancellationToken cancellationToken = default)
        {
            this.visible = visible;

            await TransitionalRenderer.FadeToAsync(visible ? TintColor.a : 0, duration, easingType, cancellationToken);
        }

        public UniTask ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration, EasingType easingType = default, 
            CancellationToken cancellationToken = default)
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

        protected virtual void RenderLive2D ()
        {
            if (drawables.Count == 0) return;

            var renderDimensions = canvasSize * ActorMetadata.PixelsPerUnit;
            var renderPosition = Live2DController.transform.position + (Vector3)canvasOffset;
            var orthoMin = Vector3.Scale(-renderDimensions / 2f, Transform.localScale) + renderPosition * ActorMetadata.PixelsPerUnit;
            var orthoMax = Vector3.Scale(renderDimensions / 2f, Transform.localScale) + renderPosition * ActorMetadata.PixelsPerUnit;
            var orthoMatrix = Matrix4x4.Ortho(orthoMin.x, orthoMax.x, orthoMin.y, orthoMax.y, float.MinValue, float.MaxValue);
            var rotationMatrix = Matrix4x4.Rotate(Quaternion.Inverse(Transform.localRotation));

            PrepareRenderTexture(renderDimensions);
            PrepareCommandBuffer(orthoMatrix);
            SortDrawables();
            foreach (var drawable in drawables)
                RenderDrawable(drawable, rotationMatrix);
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }

        private static TransitionalRenderer InitializeRenderer (OrthoActorMetadata actorMetadata, GameObject gameObject)
        {
            TransitionalRenderer renderer;
            
            if (actorMetadata.RenderTexture)
            {
                var textureRenderer = gameObject.AddComponent<TransitionalTextureRenderer>();
                textureRenderer.Initialize(actorMetadata.CustomShader);
                textureRenderer.RenderTexture = actorMetadata.RenderTexture;
                textureRenderer.CorrectAspect = actorMetadata.CorrectRenderAspect;
                renderer = textureRenderer;
            }
            else
            {
                var spriteRenderer = gameObject.AddComponent<TransitionalSpriteRenderer>();
                spriteRenderer.Initialize(actorMetadata.Pivot, actorMetadata.PixelsPerUnit, actorMetadata.CustomShader);
                renderer = spriteRenderer;
            }

            renderer.DepthPassEnabled = actorMetadata.EnableDepthPass;
            renderer.DepthAlphaCutoff = actorMetadata.DepthAlphaCutoff;

            return renderer;
        }

        private static LocalizableResourceLoader<GameObject> InitializeLoader (ActorMetadata actorMetadata)
        {
            var providerManager = Engine.GetService<IResourceProviderManager>();
            var localizationManager = Engine.GetService<ILocalizationManager>();
            return actorMetadata.Loader.CreateLocalizableFor<GameObject>(providerManager, localizationManager);
        }

        private static async Task<Live2DController> InitializeControllerAsync (LocalizableResourceLoader<GameObject> loader, string actorId, Transform transform)
        {
            var prefabResource = await loader.LoadAsync(actorId);
            if (!prefabResource.Valid) 
                throw new Exception($"Failed to load Live2D model prefab for `{actorId}` character. Make sure the resource is set up correctly in the character configuration.");
            var controller = Engine.Instantiate(prefabResource.Object).GetComponent<Live2DController>();
            controller.gameObject.name = "Live2DModel";
            controller.transform.SetParent(transform);
            return controller;
        }

        private static void InitializeDrawables (List<Live2DDrawable> drawables, Live2DController controller)
        {
            controller.CubismModel.ForceUpdateNow(); // Required to build meshes.
            drawables.Clear();
            drawables.AddRange(controller.RenderController.Renderers
                .Select(cd => new Live2DDrawable(cd))
                .OrderBy(d => d.MeshRenderer.sortingOrder)
                .ThenByDescending(d => d.Transform.position.z));
        }

        private static (Vector2 size, Vector2 offset) InitializeCanvas (Live2DController controller)
        {
            if (controller.TryGetComponent<RenderCanvas>(out var renderCanvas))
                return (renderCanvas.Size, renderCanvas.Offset);
            else
            {
                var bounds = controller.RenderController.Renderers.GetMeshRendererBounds();
                var size = new Vector2(bounds.size.x, bounds.size.y);
                return (size, Vector2.zero);
            }
        }

        private void PrepareRenderTexture (Vector2 renderDimensions)
        {
            var requiredSize = new Vector2Int(Mathf.RoundToInt(renderDimensions.x), Mathf.RoundToInt(renderDimensions.y));
            if (CurrentTextureValid()) return;
            
            if (renderTexture)
                RenderTexture.ReleaseTemporary(renderTexture);
            
            renderTexture = RenderTexture.GetTemporary(requiredSize.x, requiredSize.y);
            TransitionalRenderer.MainTexture = renderTexture;

            bool CurrentTextureValid () => renderTexture && renderTexture.width == requiredSize.x && renderTexture.height == requiredSize.y;
        }

        private void PrepareCommandBuffer (Matrix4x4 orthoMatrix)
        {
            commandBuffer.Clear();
            commandBuffer.SetRenderTarget(renderTexture);
            commandBuffer.ClearRenderTarget(true, true, Color.clear);
            commandBuffer.SetProjectionMatrix(orthoMatrix);
        }

        private void SortDrawables ()
        {
            var sortMode = Live2DController.RenderController.SortingMode;
            if (sortMode == CubismSortingMode.BackToFrontOrder || sortMode == CubismSortingMode.BackToFrontZ)
                drawables.Sort((x, y) => y.Transform.position.z.CompareTo(x.Transform.position.z));
            else drawables.Sort((x, y) => x.Transform.position.z.CompareTo(y.Transform.position.z));
        }

        private void RenderDrawable (Live2DDrawable drawable, Matrix4x4 rotationMatrix)
        {
            if (!drawable.MeshRenderer.enabled) return;
            
            var position = Live2DController.transform.TransformPoint(rotationMatrix // Compensate actor (parent game object) rotation.
                .MultiplyPoint3x4(Live2DController.transform.InverseTransformPoint(drawable.Position)));
            var transform = Matrix4x4.TRS(position * ActorMetadata.PixelsPerUnit, drawable.Rotation, drawable.Scale * ActorMetadata.PixelsPerUnit);
            drawable.MeshRenderer.GetPropertyBlock(propertyBlock);
            commandBuffer.DrawMesh(drawable.Mesh, transform, drawable.RenderMaterial, 0, -1, propertyBlock);
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
