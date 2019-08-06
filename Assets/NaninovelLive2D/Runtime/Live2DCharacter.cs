using System.Threading;
using System.Threading.Tasks;
using UnityCommon;
using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// A <see cref="ICharacterActor"/> implementation using a <see cref="Naninovel.Live2DController"/> to represent an actor.
    /// </summary>
    /// <remarks>
    /// Live2D character prefab should have a <see cref="Naninovel.Live2DController"/> components attached to the root object.
    /// </remarks>
    public class Live2DCharacter : MonoBehaviourActor, ICharacterActor
    {
        public override string Appearance { get => appearance; set => SetAppearance(value); }
        public override bool IsVisible { get => isVisible; set => SetVisibility(value); }
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

        private readonly TextPrinterManager textPrinterManager;
        private readonly CharacterManager characterManager;
        private readonly CameraManager cameraManager;
        private readonly Tweener<VectorTween> positionTweener, scaleTweener;
        private Resource<GameObject> live2DPrefabResource;
        private string appearance;
        private bool isVisible;
        private Vector3 scale = Vector3.one;
        private Vector3 position = Vector3.zero;
        private CharacterLookDirection lookDirection;

        public Live2DCharacter (string id, CharacterMetadata metadata) 
            : base(id, metadata)
        {
            if (!config) config = Configuration.LoadOrDefault<Live2DConfiguration>();

            cameraManager = Engine.GetService<CameraManager>();
            characterManager = Engine.GetService<CharacterManager>();
            textPrinterManager = Engine.GetService<TextPrinterManager>();
            positionTweener = new Tweener<VectorTween>(ActorBehaviour);
            scaleTweener = new Tweener<VectorTween>(ActorBehaviour);

            cameraManager.OnAspectChanged += UpdateRenderOrthoSize;
            textPrinterManager.OnPrintTextStarted += HandlePrintTextStarted;
            textPrinterManager.OnPrintTextFinished += HandlePrintTextFinished;

            var providerMngr = Engine.GetService<ResourceProviderManager>();
            var localeMngr = Engine.GetService<LocalizationManager>();
            PrefabLoader = new LocalizableResourceLoader<GameObject>(
                providerMngr.GetProviderList(ResourceProviderType.Project), 
                localeMngr, metadata.LoaderConfiguration.PathPrefix);

            SpriteRenderer = GameObject.AddComponent<TransitionalSpriteRenderer>();
            SpriteRenderer.Pivot = metadata.Pivot;
            SpriteRenderer.PixelsPerUnit = metadata.PixelsPerUnit;

            SetVisibility(false);
        }

        public override async Task InitializeAsync ()
        {
            await base.InitializeAsync();

            var live2DPrefab = await PrefabLoader.LoadAsync(Id);
            InitializeController(live2DPrefab);
        }

        public override async Task HoldResourcesAsync (object holder, string appearance)
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

        public override async Task ChangePositionAsync (Vector3 position, float duration, 
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

        public override async Task ChangeScaleAsync (Vector3 scale, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            CompleteScaleTween();
            this.scale = scale;

            var tween = new VectorTween(Live2DController.ModelScale, scale, duration, SetBehaviourScale, false, easingType);
            await scaleTweener.RunAsync(tween, cancellationToken);
        }

        public override Task ChangeAppearanceAsync (string appearance, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            SetAppearance(appearance);
            return Task.CompletedTask;
        }

        public override async Task ChangeVisibilityAsync (bool isVisible, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            this.isVisible = isVisible;

            await SpriteRenderer.FadeToAsync(isVisible ? 1 : 0, duration, easingType, cancellationToken);
        }

        public Task ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration, 
            EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            SetLookDirection(lookDirection);
            return Task.CompletedTask;
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

        protected override void SetBehaviourPosition (Vector3 position)
        {
            this.position = position;

            Transform.position = new Vector3(position.x, characterManager.GlobalSceneOrigin.y, position.z);
            var localY = Live2DController.transform.InverseTransformPoint((Vector2)position - characterManager.GlobalSceneOrigin).y;
            RenderCamera.transform.localPosition = new Vector3(config.CameraOffset.x, config.CameraOffset.y - localY, config.CameraOffset.z);
        }

        protected override void SetBehaviourScale (Vector3 scale)
        {
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
            this.isVisible = isVisible;

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
            if (!IsVisible) // Handle visibility-controlled alpha of the tint color.
                tintColor.a = SpriteRenderer.TintColor.a;
            SpriteRenderer.TintColor = tintColor;
        }

        protected virtual void InitializeController (GameObject live2DPrefab)
        {
            if (Live2DController) return;

            Live2DController = Engine.Instantiate(live2DPrefab, $"{Id} Live2D Renderer")?.GetComponent<Live2DController>();
            Debug.Assert(Live2DController, $"Failed to initialize Live2D controller: {live2DPrefab.name} prefab is invalid or doesn't have {nameof(Naninovel.Live2DController)} component attached to the root object.");
            Live2DController.transform.localPosition = Vector3.zero + prefabOffset;
            Live2DController.transform.AddPosX(distributeXOffset); // Distribute concurrently used Live2D prefabs.
            distributeXOffset += cameraManager.ReferenceSize.x + config.CameraOffset.x;
            Live2DController.gameObject.ForEachDescendant(g => g.layer = config.RenderLayer);

            var descriptor = new RenderTextureDescriptor((int)cameraManager.ReferenceResolution.x, (int)cameraManager.ReferenceResolution.y, RenderTextureFormat.Default);
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
            if (positionTweener.IsRunning)
                positionTweener.CompleteInstantly();
        }

        private void CompleteScaleTween ()
        {
            if (scaleTweener.IsRunning)
                scaleTweener.CompleteInstantly();
        }

        private void UpdateRenderOrthoSize (float aspect)
        {
            RenderCamera.orthographicSize = cameraManager.Camera.orthographicSize;
        }

        private void HandlePrintTextStarted (PrintTextArgs args)
        {
            if (args.AuthorId == Id)
                Live2DController.SetIsSpeaking(true);
        }

        private void HandlePrintTextFinished (PrintTextArgs args)
        {
            if (args.AuthorId == Id)
                Live2DController.SetIsSpeaking(false);
        }
    }
}
