using Live2D.Cubism.Framework.LookAt;
using Live2D.Cubism.Framework.MouthMovement;
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
        [Tooltip("Whether to control mouth animation when the character is the author of the currently printed message. The object should have `CubismMouthController` and `CubismMouthParameter` set up for this feature to work.")]
        [SerializeField] private bool controlMouth = true;
        [Tooltip("When `Control Mouth` is enabled, this property allows to control how fast the mouth will close and open when the character is speaking.")]
        [SerializeField] private float mouthAnimationSpeed = 10f;
        [Tooltip("When `Control Mouth` is enabled, this property limits the amplitude of the mouth openings, in 0.0 to 1.0 range.")]
        [SerializeField] private float mouthAnimationLimit = .65f;

        private Animator animator;
        private CubismRenderController renderController;
        private CubismLookController lookController;
        private CubismLookTargetBehaviour lookTarget;
        private CubismMouthController mouthController;
        private Transform modelTransform;
        private bool isSpeaking;

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

        public void SetIsSpeaking (bool value) => isSpeaking = value;

        private void Awake ()
        {
            modelTransform = transform.Find("Drawables");
            Debug.Assert(modelTransform, "Failed to find Drawables gameobject inside Live2D prefab.");

            animator = GetComponent<Animator>();
            renderController = GetComponent<CubismRenderController>();
            lookController = GetComponent<CubismLookController>();
            mouthController = GetComponent<CubismMouthController>();

            if (controlLook)
            {
                lookTarget = new GameObject("LookTarget").AddComponent<CubismLookTargetBehaviour>();
                lookTarget.transform.SetParent(transform, false);
                lookController.Center = transform;
                lookController.Target = lookTarget;
            }

            renderController.SortingMode = CubismSortingMode.BackToFrontOrder;
        }

        private void Update ()
        {
            if (!controlMouth || mouthController == null) return;

            mouthController.MouthOpening = isSpeaking ? Mathf.Abs(Mathf.Sin(Time.time * mouthAnimationSpeed)) * mouthAnimationLimit : 0f;
        }
    }
}
