using Live2D.Cubism.Framework.LookAt;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// Used by <see cref="Live2DCharacter"/> to control a Live2D character.
    /// </summary>
    /// <remarks>
    /// All the apperance changes are handled by invoking an <see cref="Animator.SetTrigger(string)"/> with the apperance name as the trigger name.
    /// Look direction is handled with <see cref="CubismLookController"/>.
    /// </remarks>
    [RequireComponent(typeof(Animator), typeof(CubismRenderController), typeof(CubismLookController))]
    public class Live2DController : MonoBehaviour
    {
        public Vector3 ModelScale { get => modelTransform.localScale; set => modelTransform.localScale = value; }

        [Tooltip("Whether to make the Live2D model to look at right, left or center, depending on the position on the scene.")]
        [SerializeField] private bool controlLook = true;

        private Animator animator;
        private CubismRenderController renderController;
        private CubismLookController lookController;
        private CubismLookTargetBehaviour lookTarget;
        private Transform modelTransform;

        public void SetRenderCamera (Camera camera)
        {
            renderController.CameraToFace = camera;
        }

        public void SetAppearance (string appearance)
        {
            animator.SetTrigger(appearance);
        }

        public void SetLookDirection (CharacterLookDirection lookDirection)
        {
            if (!controlLook) return;

            switch (lookDirection)
            {
                case CharacterLookDirection.Center:
                    lookTarget.transform.localPosition = lookController.Center.position;
                    break;
                case CharacterLookDirection.Left:
                    lookTarget.transform.localPosition = lookController.Center.position - lookController.Center.right;
                    break;
                case CharacterLookDirection.Right:
                    lookTarget.transform.localPosition = lookController.Center.position + lookController.Center.right;
                    break;
            }
        }

        private void Awake ()
        {
            modelTransform = transform.Find("Drawables");
            Debug.Assert(modelTransform, "Failed to find Drawables gameobject inside Live2D prefab.");

            animator = GetComponent<Animator>();
            renderController = GetComponent<CubismRenderController>();
            lookController = GetComponent<CubismLookController>();

            if (controlLook)
            {
                lookTarget = new GameObject("LookTarget").AddComponent<CubismLookTargetBehaviour>();
                lookTarget.transform.SetParent(transform, false);
                lookController.Center = transform;
                lookController.Target = lookTarget;
            }

            renderController.SortingMode = CubismSortingMode.BackToFrontOrder;
        }
    }
}
