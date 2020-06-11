using System.Linq;
using System.Threading;
using UniRx.Async;
using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// A <see cref="ICharacterActor"/> implementation using a <see cref="Naninovel.Live2DController"/> to represent an actor.
    /// </summary>
    /// <remarks>
    /// Live2D character prefab should have a <see cref="Naninovel.Live2DController"/> components attached to the root object.
    /// </remarks>
    public class Live2DCharacter : MonoBehaviourActor, ICharacterActor, Commands.LipSync.IReceiver
    {
        public override string Appearance { get => appearance; set => SetAppearance(value); }
        public override bool Visible { get => visible; set => SetVisibility(value); }
        public override Vector3 Position { get => position; set { CompletePositionTween(); SetBehaviourPosition(value); } }
        public override Vector3 Scale { get => scale; set { CompleteScaleTween(); SetBehaviourScale(value); } }
        public CharacterLookDirection LookDirection { get => lookDirection; set => SetLookDirection(value); }

        protected LocalizableResourceLoader<GameObject> PrefabLoader { get; private set; }
        protected Live2DController Live2DController { get; private set; }
        protected RenderTexture RenderTexture { get; private set; }
        protected Camera RenderCamera { get; private set; }
        protected TransitionalSpriteRenderer SpriteRenderer { get; }

        private const string defaultCameraResource = "Naninovel/RenderCamera";
        private static readonly Vector3 prefabOffset = new Vector3(0, 0, -999);
        private static float distributeXOffset = -999;
        private static Live2DConfiguration config;

        private readonly ITextPrinterManager textPrinterManager;
        private readonly ICharacterManager characterManager;
        private readonly ICameraManager cameraManager;
        private readonly CameraConfiguration cameraConfig;
        private readonly CharactersConfiguration charsConfig;
        private readonly Tweener<VectorTween> positionTweener, scaleTweener;
        private Resource<GameObject> live2DPrefabResource;
        private string appearance;
        private bool visible;
        private Vector3 scale = Vector3.one;
        private Vector3 position = Vector3.zero;
        private CharacterLookDirection lookDirection;
        private bool lipSyncAllowed = true;

        public Live2DCharacter (string id, CharacterMetadata metadata) 
            : base(id, metadata)
        {
            if (!config) config = Engine.GetConfiguration<Live2DConfiguration>();

            cameraManager = Engine.GetService<ICameraManager>();
            characterManager = Engine.GetService<ICharacterManager>();
            textPrinterManager = Engine.GetService<ITextPrinterManager>();
            positionTweener = new Tweener<VectorTween>();
            scaleTweener = new Tweener<VectorTween>();
            cameraConfig = Engine.GetConfiguration<CameraConfiguration>();
            charsConfig = Engine.GetConfiguration<CharactersConfiguration>();

            cameraManager.OnAspectChanged += UpdateRenderOrthoSize;
            textPrinterManager.OnPrintTextStarted += HandlePrintTextStarted;
            textPrinterManager.OnPrintTextFinished += HandlePrintTextFinished;

            var providerMngr = Engine.GetService<IResourceProviderManager>();
            var localeMngr = Engine.GetService<ILocalizationManager>();
            PrefabLoader = metadata.Loader.CreateLocalizableFor<GameObject>(providerMngr, localeMngr);

            SpriteRenderer = GameObject.AddComponent<TransitionalSpriteRenderer>();
            SpriteRenderer.Pivot = metadata.Pivot;
            SpriteRenderer.PixelsPerUnit = metadata.PixelsPerUnit;

            SetVisibility(false);
        }

        public override async UniTask InitializeAsync ()
        {
            await base.InitializeAsync();

            var live2DPrefabs = await PrefabLoader.LoadAllAsync(Id);
            var live2DPrefab = live2DPrefabs.FirstOrDefault();
            InitializeController(live2DPrefab);
        }

        public override async UniTask HoldResourcesAsync (object holder, string appearance)
        {
            await base.HoldResourcesAsync(holder, appearance);

            live2DPrefabResource = await PrefabLoader.LoadAsync(Id);
            if (live2DPrefabResource?.IsValid ?? false)
                live2DPrefabResource.Hold(holder);
        }

        public override void ReleaseResources (object holder, string appearance)
        {
            base.ReleaseResources(holder, appearance);

            live2DPrefabResource?.Release(holder);
        }

        public override async UniTask ChangePositionAsync (Vector3 position, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            CompletePositionTween();
            var curPos = this.position;
            this.position = position;

            var tween = new VectorTween(curPos, position, duration, SetBehaviourPosition, false, easingType);
            await positionTweener.RunAsync(tween, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            SetBehaviourPosition(position);
        }

        public override async UniTask ChangeScaleAsync (Vector3 scale, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            CompleteScaleTween();
            this.scale = scale;

            var tween = new VectorTween(Live2DController.ModelScale, scale, duration, SetBehaviourScale, false, easingType);
            await scaleTweener.RunAsync(tween, cancellationToken);
        }

        public override UniTask ChangeAppearanceAsync (string appearance, float duration, 
            EasingType easingType = EasingType.Linear, Transition? transition = default, CancellationToken cancellationToken = default)
        {
            SetAppearance(appearance);
            return UniTask.CompletedTask;
        }

        public override async UniTask ChangeVisibilityAsync (bool isVisible, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            this.visible = isVisible;

            await SpriteRenderer.FadeToAsync(isVisible ? 1 : 0, duration, easingType, cancellationToken);
        }

        public UniTask ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            SetLookDirection(lookDirection);
            return UniTask.CompletedTask;
        }

        public override void Dispose ()
        {
            base.Dispose();

            if (cameraManager != null)
                cameraManager.OnAspectChanged -= UpdateRenderOrthoSize;

            if (textPrinterManager != null)
            {
                textPrinterManager.OnPrintTextStarted -= HandlePrintTextStarted;
                textPrinterManager.OnPrintTextFinished -= HandlePrintTextFinished;
            }

            DisposeResources();
        }

        public void AllowLipSync (bool active)
        {
            lipSyncAllowed = active;
        }

        protected override void SetBehaviourPosition (Vector3 position)
        {
            if (!Transform || !RenderCamera || !Live2DController) return;

            this.position = position;

            var globalSceneOrigin = cameraConfig.SceneToWorldSpace(charsConfig.SceneOrigin);
            Transform.position = new Vector3(position.x, globalSceneOrigin.y, position.z);
            var localY = Live2DController.transform.InverseTransformPoint(position - globalSceneOrigin).y;
            RenderCamera.transform.localPosition = new Vector3(config.CameraOffset.x, config.CameraOffset.y - localY, config.CameraOffset.z);
        }

        protected override void SetBehaviourScale (Vector3 scale)
        {
            if (!Live2DController) return;

            this.scale = scale;

            Live2DController.ModelScale = scale;
        }

        protected virtual void SetAppearance (string appearance)
        {
            this.appearance = appearance;

            if (string.IsNullOrEmpty(appearance)) return;

            Live2DController.SetAppearance(appearance);
        }

        protected virtual void SetVisibility (bool isVisible)
        {
            this.visible = isVisible;

            SpriteRenderer.Opacity = isVisible ? 1 : 0;
        }

        protected virtual void SetLookDirection (CharacterLookDirection lookDirection)
        {
            this.lookDirection = lookDirection;

            Live2DController.SetLookDirection(lookDirection);
        }

        protected override Color GetBehaviourTintColor () => SpriteRenderer.TintColor;

        protected override void SetBehaviourTintColor (Color tintColor)
        {
            if (!SpriteRenderer) return;

            if (!Visible) // Handle visibility-controlled alpha of the tint color.
                tintColor.a = SpriteRenderer.TintColor.a;
            SpriteRenderer.TintColor = tintColor;
        }

        protected virtual void InitializeController (GameObject live2DPrefab)
        {
            if (Live2DController) return;

            var prefab = Engine.Instantiate(live2DPrefab, $"{Id} Live2D Renderer");
            if (ObjectUtils.IsValid(prefab))
                Live2DController = prefab.GetComponent<Live2DController>();
            Debug.Assert(Live2DController, $"Failed to initialize Live2D controller: {live2DPrefab.name} prefab is invalid or doesn't have {nameof(Naninovel.Live2DController)} component attached to the root object.");
            Live2DController.transform.localPosition = Vector3.zero + prefabOffset;
            Live2DController.transform.AddPosX(distributeXOffset); // Distribute concurrently used Live2D prefabs.
            distributeXOffset += cameraConfig.ReferenceSize.x + config.CameraOffset.x;
            Live2DController.gameObject.ForEachDescendant(g => g.layer = config.RenderLayer);

            var descriptor = new RenderTextureDescriptor(cameraConfig.ReferenceResolution.x, cameraConfig.ReferenceResolution.y, RenderTextureFormat.Default);
            RenderTexture = new RenderTexture(descriptor);

            SpriteRenderer.MainTexture = RenderTexture;

            var cameraPrefab = ObjectUtils.IsValid(config.CustomRenderCamera) ? config.CustomRenderCamera : Resources.Load<Camera>(defaultCameraResource);
            RenderCamera = Engine.Instantiate(cameraPrefab, "RenderCamera");
            RenderCamera.transform.SetParent(Live2DController.transform, false);
            RenderCamera.transform.localPosition = Vector3.zero + config.CameraOffset;
            RenderCamera.targetTexture = RenderTexture;
            RenderCamera.cullingMask = 1 << config.RenderLayer;
            RenderCamera.orthographicSize = cameraManager.Camera.orthographicSize;

            Live2DController.SetRenderCamera(RenderCamera);
        }

        private void DisposeResources ()
        {
            if (RenderTexture)
                Object.Destroy(RenderTexture);
            if (Live2DController)
                Object.Destroy(Live2DController.gameObject);
            Live2DController = null;
        }

        private void CompletePositionTween ()
        {
            if (positionTweener.Running)
                positionTweener.CompleteInstantly();
        }

        private void CompleteScaleTween ()
        {
            if (scaleTweener.Running)
                scaleTweener.CompleteInstantly();
        }

        private void UpdateRenderOrthoSize (float aspect)
        {
            RenderCamera.orthographicSize = cameraManager.Camera.orthographicSize;
        }

        private void HandlePrintTextStarted (PrintTextArgs args)
        {
            if (lipSyncAllowed && args.AuthorId == Id)
                Live2DController.SetIsSpeaking(true);
        }

        private void HandlePrintTextFinished (PrintTextArgs args)
        {
            if (args.AuthorId == Id)
                Live2DController.SetIsSpeaking(false);
        }
    }
}
