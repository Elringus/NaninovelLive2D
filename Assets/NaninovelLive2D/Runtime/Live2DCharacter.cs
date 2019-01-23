using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// A <see cref="ICharacterActor"/> implementation using a <see cref="Live2D.Cubism.Core.CubismModel"/> to represent an actor.
    /// </summary>
    /// <remarks>
    /// Prefab with the actor name should have a <see cref="Live2DAppearanceController"/> components attached to the root object.
    /// All the apperance changes are handled by mapping apperance name to the set of <see cref="Live2D.Cubism.Core.CubismParameter"/> as specified in the <see cref="Live2DAppearanceController"/>.
    /// </remarks>
    public class Live2DCharacter : MonoBehaviourActor, ICharacterActor
    {
        public override string Appearance { get => appearance; set => SetAppearance(value); }
        public override bool IsVisible { get => isVisible; set => SetVisibility(value); }
        public CharacterLookDirection LookDirection { get => lookDirection; set => SetLookDirection(value); }

        protected Live2DAppearanceController Live2DController { get; private set; }

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
            var prefab = new LocalizableResourceLoader<Live2DAppearanceController>(metadata.LoaderConfiguration, providerMngr, localeMngr).Load(name);

            Live2DController = Engine.Instantiate(prefab);
            Live2DController.transform.SetParent(Transform);

            SetVisibility(false);
        }

        public override Task ChangeAppearanceAsync (string appearance, float duration)
        {
            this.appearance = appearance;

            if (string.IsNullOrEmpty(appearance)) return Task.CompletedTask;

            Live2DController.SetAppearance(appearance, DurationToSpeed(duration));

            return Task.CompletedTask;
        }

        public override Task ChangeVisibilityAsync (bool isVisible, float duration)
        {
            // TODO: Implement async version.
            SetVisibility(isVisible);
            return Task.CompletedTask;
        }

        public Task ChangeLookDirectionAsync (CharacterLookDirection lookDirection, float duration)
        {
            this.lookDirection = lookDirection;

            Live2DController.SetLookDirection(lookDirection, DurationToSpeed(duration));

            return Task.CompletedTask;
        }

        public override Task PreloadResourcesAsync (string appearance = null) => Task.CompletedTask;

        public override Task UnloadResourcesAsync (string appearance = null) => Task.CompletedTask;

        protected virtual void SetAppearance (string appearance)
        {
            this.appearance = appearance;

            if (string.IsNullOrEmpty(appearance)) return;

            Live2DController.SetAppearance(appearance, DurationToSpeed(0));
        }

        protected virtual void SetVisibility (bool isVisible)
        {
            this.isVisible = isVisible;

            GameObject?.SetActive(isVisible);
        }

        protected virtual void SetLookDirection (CharacterLookDirection lookDirection)
        {
            this.lookDirection = lookDirection;

            Live2DController.SetLookDirection(lookDirection, DurationToSpeed(0f));
        }

        protected virtual float DurationToSpeed (float duration) => 1 / Mathf.Clamp(duration, .000001f, float.MaxValue);
    }
}
