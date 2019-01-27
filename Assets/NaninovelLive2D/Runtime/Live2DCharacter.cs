using System.Collections.Generic;
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
        public override Vector3 Position { get => position; set { CompletePositionTween(); SetPosition(value); } }
        public override Vector3 Scale { get => scale; set { CompleteScaleTween(); SetScale(value); } }
        public CharacterLookDirection LookDirection { get => lookDirection; set => SetLookDirection(value); }

        protected LocalizableResourceLoader<Live2DController> PrefabLoader { get; private set; }
        protected Live2DController Live2DController { get; private set; }
        protected RenderTexture RenderTexture { get; private set; }
        protected Camera RenderCamera { get; private set; }
        protected NovelSpriteRenderer SpriteRenderer { get; }

        private static readonly Vector3 prefabOffset = new Vector3(0, 0, -999);
        private static float distributeXOffset = -999;

        private static Live2DConfiguration config;
        private static OrthoCamera refCamera;
        private static CharacterManager charManager;
        private string appearance;
        private bool isVisible;
        private Vector3 scale = Vector3.one;
        private Vector3 position = Vector3.zero;
        private Tweener<VectorTween> positionTweener, scaleTweener;
        private CharacterLookDirection lookDirection;

        public Live2DCharacter (string name, OrthoActorMetadata metadata) 
            : base(name)
        {
            // Only project provider is supported.
            metadata.LoaderConfiguration.ProviderTypes = new List<ResourceProviderType> { ResourceProviderType.Project };

            if (!config) config = Live2DConfiguration.LoadFromResources();
            if (refCamera is null) refCamera = Engine.GetService<OrthoCamera>();
            if (charManager is null) charManager = Engine.GetService<CharacterManager>();
            positionTweener = new Tweener<VectorTween>(ActorBehaviour);
            scaleTweener = new Tweener<VectorTween>(ActorBehaviour);

            refCamera.OnAspectChanged += UpdateRenderOrthoSize;

            var providerMngr = Engine.GetService<ResourceProviderManager>();
            var localeMngr = Engine.GetService<LocalizationManager>();
            PrefabLoader = new LocalizableResourceLoader<Live2DController>(metadata.LoaderConfiguration, providerMngr, localeMngr);

            SpriteRenderer = GameObject.AddComponent<NovelSpriteRenderer>();
            SpriteRenderer.Pivot = metadata.Pivot;
            SpriteRenderer.PixelsPerUnit = metadata.PixelsPerUnit;

            SetVisibility(false);
        }

        public override async Task ChangePositionAsync (Vector3 position, float duration)
        {
            CompletePositionTween();
            var curPos = this.position;
            this.position = position;

            InitializeController();
            //var worldY = Live2DController.transform.TransformPoint(RenderCamera.transform.localPosition - config.CameraOffset).y + charManager.GlobalSceneOrigin.y;
            //var curPos = new Vector3(Transform.position.x, worldY, Transform.position.z);
            var tween = new VectorTween(curPos, position, duration, SetPosition, false, true);
            await positionTweener.RunAsync(tween);
            SetPosition(position);
        }

        public override async Task ChangeScaleAsync (Vector3 scale, float duration)
        {
            CompleteScaleTween();
            this.scale = scale;

            InitializeController();
            var tween = new VectorTween(Live2DController.ModelScale, scale, duration, SetScale, false, true);
            await scaleTweener.RunAsync(tween);
        }

        public override Task ChangeAppearanceAsync (string appearance, float duration)
        {
            SetAppearance(appearance);
            return Task.CompletedTask;
        }

        public override async Task ChangeVisibilityAsync (bool isVisible, float duration)
        {
            this.isVisible = isVisible;

            await SpriteRenderer.FadeToAsync(isVisible ? 1 : 0, duration);
        }

        public Task ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration)
        {
            SetLookDirection(lookDirection);
            return Task.CompletedTask;
        }

        public override async Task PreloadResourcesAsync (string appearance = null)
        {
            if (Live2DController) return;

            var prefab = await PrefabLoader.LoadAsync(Name);
            InitializeController(prefab.gameObject);
        }

        public override Task UnloadResourcesAsync (string appearance = null)
        {
            DisposeResources();
            return Task.CompletedTask;
        }

        public override void Dispose ()
        {
            base.Dispose();

            if (refCamera != null)
                refCamera.OnAspectChanged -= UpdateRenderOrthoSize;

            DisposeResources();
        }

        protected override void SetPosition (Vector3 position)
        {
            this.position = position;

            InitializeController();
            Transform.position = new Vector3(position.x, charManager.GlobalSceneOrigin.y, position.z);
            var localY = Live2DController.transform.InverseTransformPoint((Vector2)position - charManager.GlobalSceneOrigin).y;
            RenderCamera.transform.localPosition = new Vector3(config.CameraOffset.x, config.CameraOffset.y - localY, config.CameraOffset.z);
        }

        protected override void SetScale (Vector3 scale)
        {
            this.scale = scale;

            InitializeController();
            Live2DController.ModelScale = scale;
        }

        protected virtual void SetAppearance (string appearance)
        {
            this.appearance = appearance;

            if (string.IsNullOrEmpty(appearance)) return;

            InitializeController();
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

            InitializeController();
            Live2DController.SetLookDirection(lookDirection);
        }

        private void InitializeController (GameObject live2DPrefab = null)
        {
            if (Live2DController) return;

            if (!live2DPrefab) live2DPrefab = PrefabLoader.Load(Name).gameObject;

            Live2DController = Engine.Instantiate(live2DPrefab, $"{Name} Live2D Renderer")?.GetComponent<Live2DController>();
            Debug.Assert(Live2DController, $"Failed to initialize Live2D controller: {live2DPrefab.name} prefab is invalid or doesn't have {nameof(Naninovel.Live2DController)} component attached to the root object.");
            Live2DController.transform.localPosition = Vector3.zero + prefabOffset;
            Live2DController.transform.AddPosX(distributeXOffset); // Distribute concurrently used Live2D prefabs.
            distributeXOffset += refCamera.ReferenceSize.x + config.CameraOffset.x;
            Live2DController.gameObject.ForEachDescendant(g => g.layer = config.RenderLayer);

            var descriptor = new RenderTextureDescriptor((int)refCamera.ReferenceResolution.x, (int)refCamera.ReferenceResolution.y, RenderTextureFormat.Default);
            RenderTexture = new RenderTexture(descriptor);

            SpriteRenderer.MainTexture = RenderTexture;

            RenderCamera = config.RenderCamera ? Engine.Instantiate(config.RenderCamera, "RenderCamera") : Engine.CreateObject<Camera>("RenderCamera");
            RenderCamera.transform.SetParent(Live2DController.transform, false);
            RenderCamera.transform.localPosition = Vector3.zero + config.CameraOffset;
            RenderCamera.targetTexture = RenderTexture;
            RenderCamera.cullingMask = 1 << config.RenderLayer;
            RenderCamera.orthographicSize = refCamera.Camera.orthographicSize;

            Live2DController.SetRenderCamera(RenderCamera);
        }

        private void DisposeResources ()
        {
            if (RenderTexture)
                Object.Destroy(RenderTexture);
            if (Live2DController)
                Object.Destroy(Live2DController.gameObject);
            Live2DController = null;
            PrefabLoader?.UnloadAllAsync();
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
            RenderCamera.orthographicSize = refCamera.Camera.orthographicSize;
        }
    }
}
