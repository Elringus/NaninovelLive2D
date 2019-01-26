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
        public CharacterLookDirection LookDirection { get => lookDirection; set => SetLookDirection(value); }

        protected LocalizableResourceLoader<Live2DController> PrefabLoader { get; private set; }
        protected Live2DController Live2DController { get; private set; }
        protected RenderTexture RenderTexture { get; private set; }
        protected Camera RenderCamera { get; private set; }
        protected NovelSpriteRenderer SpriteRenderer { get; }

        private static readonly Vector3 prefabOffset = new Vector3(0, 0, -999);
        private static float distributeXOffset = -999;

        private string appearance;
        private bool isVisible;
        private CharacterLookDirection lookDirection;

        public Live2DCharacter (string name, OrthoActorMetadata metadata) 
            : base(name)
        {
            // Only project provider is supported.
            metadata.LoaderConfiguration.ProviderTypes = new List<ResourceProviderType> { ResourceProviderType.Project };

            var providerMngr = Engine.GetService<ResourceProviderManager>();
            var localeMngr = Engine.GetService<LocalizationManager>();
            PrefabLoader = new LocalizableResourceLoader<Live2DController>(metadata.LoaderConfiguration, providerMngr, localeMngr);

            SpriteRenderer = GameObject.AddComponent<NovelSpriteRenderer>();
            SpriteRenderer.Pivot = metadata.Pivot;
            SpriteRenderer.PixelsPerUnit = metadata.PixelsPerUnit;

            SetVisibility(false);
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

            DisposeResources();
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

            var config = Live2DConfiguration.LoadFromResources();
            var refCamera = Engine.GetService<OrthoCamera>();

            Live2DController = Engine.Instantiate(live2DPrefab, $"{Name} Live2D Renderer")?.GetComponent<Live2DController>();
            Debug.Assert(Live2DController, $"Failed to initialize Live2D controller: {live2DPrefab.name} prefab is invalid or doesn't have {nameof(Naninovel.Live2DController)} component attached to the root object.");
            Live2DController.transform.localPosition = Vector3.zero + prefabOffset;
            Live2DController.transform.AddPosX(distributeXOffset); // Distribute concurrently used Live2D prefabs.
            distributeXOffset += refCamera.ReferenceSize.x + config.CameraOffset.x;
            Live2DController.gameObject.ForEachDescendant(g => g.layer = config.RenderLayer);

            var descriptor = new RenderTextureDescriptor((int)refCamera.ReferenceResolution.x, (int)refCamera.ReferenceResolution.y, RenderTextureFormat.Default);
            RenderTexture = new RenderTexture(descriptor);

            SpriteRenderer.MainTexture = RenderTexture;

            RenderCamera = Engine.CreateObject<Camera>("RenderCamera");
            RenderCamera.transform.SetParent(Live2DController.transform, false);
            RenderCamera.transform.localPosition = Vector3.zero + config.CameraOffset;
            RenderCamera.targetTexture = RenderTexture;
            RenderCamera.cullingMask = 1 << config.RenderLayer;
            RenderCamera.orthographic = true;
            RenderCamera.orthographicSize = config.OrthoSize;

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
    }
}
