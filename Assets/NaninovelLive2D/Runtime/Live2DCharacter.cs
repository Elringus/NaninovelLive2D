using System;
using System.Threading.Tasks;
using Naninovel.Commands;
using Naninovel.FX;
using UniRx.Async;
using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// A <see cref="ICharacterActor"/> implementation using a <see cref="Live2DController"/> to represent an actor.
    /// </summary>
    /// <remarks>
    /// Live2D character prefab should have a <see cref="Controller"/> components attached to the root object.
    /// </remarks>
    [ActorResources(typeof(Live2DController), false)]
    public class Live2DCharacter : MonoBehaviourActor<CharacterMetadata>, ICharacterActor, LipSync.IReceiver, Blur.IBlurable
    {
        public override string Appearance { get => appearance; set => SetAppearance(value); }
        public override bool Visible { get => visible; set => SetVisibility(value); }
        public virtual CharacterLookDirection LookDirection { get => lookDirection; set => SetLookDirection(value); }

        protected virtual TransitionalRenderer Renderer { get; private set; }
        protected virtual Live2DController Controller { get; private set; }
        protected virtual Live2DDrawer Drawer { get; private set; }
        protected virtual CharacterLipSyncer LipSyncer { get; private set; }

        private LocalizableResourceLoader<GameObject> prefabLoader;
        private string appearance;
        private bool visible;
        private CharacterLookDirection lookDirection;

        public Live2DCharacter (string id, CharacterMetadata metadata)
            : base(id, metadata) { }

        public override async UniTask InitializeAsync ()
        {
            await base.InitializeAsync();

            prefabLoader = InitializeLoader(ActorMetadata);
            Controller = await InitializeControllerAsync(prefabLoader, Id, Transform);
            Renderer = TransitionalRenderer.CreateFor(ActorMetadata, GameObject);
            Drawer = new Live2DDrawer(Controller);
            LipSyncer = new CharacterLipSyncer(Id, Controller.SetIsSpeaking);

            SetVisibility(false);

            Engine.Behaviour.OnBehaviourUpdate += DrawLive2D;
        }

        public override void Dispose ()
        {
            if (Engine.Behaviour != null)
                Engine.Behaviour.OnBehaviourUpdate -= DrawLive2D;

            LipSyncer?.Dispose();
            Drawer.Dispose();

            base.Dispose();

            prefabLoader?.UnloadAll();
        }

        public UniTask BlurAsync (float duration, float intensity, EasingType easingType = default, CancellationToken cancellationToken = default)
        {
            return Renderer.BlurAsync(duration, intensity, easingType, cancellationToken);
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

            await Renderer.FadeToAsync(visible ? TintColor.a : 0, duration, easingType, cancellationToken);
        }

        public UniTask ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration, EasingType easingType = default,
            CancellationToken cancellationToken = default)
        {
            SetLookDirection(lookDirection);
            return UniTask.CompletedTask;
        }

        public void AllowLipSync (bool active) => LipSyncer.SyncAllowed = active;

        protected virtual void SetAppearance (string appearance)
        {
            this.appearance = appearance;

            if (string.IsNullOrEmpty(appearance)) return;

            if (Controller)
                Controller.SetAppearance(appearance);
        }

        protected virtual void SetVisibility (bool visible) => ChangeVisibilityAsync(visible, 0).Forget();

        protected override Color GetBehaviourTintColor () => Renderer.TintColor;

        protected override void SetBehaviourTintColor (Color tintColor)
        {
            if (!Visible) // Handle visibility-controlled alpha of the tint color.
                tintColor.a = Renderer.TintColor.a;
            Renderer.TintColor = tintColor;
        }

        protected virtual void SetLookDirection (CharacterLookDirection lookDirection)
        {
            this.lookDirection = lookDirection;

            if (Controller)
                Controller.SetLookDirection(lookDirection);
        }

        protected virtual void DrawLive2D () => Drawer.DrawTo(Renderer, ActorMetadata.PixelsPerUnit);

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
            controller.gameObject.name = actorId;
            controller.transform.SetParent(transform);
            return controller;
        }
    }
}
