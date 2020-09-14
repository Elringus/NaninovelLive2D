using Live2D.Cubism.Core;
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
    /// All the appearance changes are handled by invoking an <see cref="Animator.SetTrigger(string)"/> with the appearance name as the trigger name.
    /// Look direction is handled with <see cref="CubismLookController"/>.
    /// </remarks>
    [RequireComponent(typeof(Animator), typeof(CubismRenderController), typeof(CubismLookController))]
    public class Live2DController : MonoBehaviour
    {
        public virtual CubismModel CubismModel { get; private set; }
        public virtual CubismRenderController RenderController { get; private set; }

        [Tooltip("Whether to make the Live2D model to look at right, left or center, depending on the position on the scene.")]
        [SerializeField] private bool controlLook = true;
        [Tooltip("Whether to control mouth animation when the character is the author of the currently printed message. The object should have `CubismMouthController` and `CubismMouthParameter` set up for this feature to work.")]
        [SerializeField] private bool controlMouth = true;
        [Tooltip("When `Control Mouth` is enabled, this property allows to control how fast the mouth will close and open when the character is speaking.")]
        [SerializeField] private float mouthAnimationSpeed = 10f;
        [Tooltip("When `Control Mouth` is enabled, this property limits the amplitude of the mouth openings, in 0.0 to 1.0 range.")]
        [SerializeField] private float mouthAnimationLimit = .65f;

        private Animator animator;
        private CubismLookController lookController;
        private CubismLookTargetBehaviour lookTarget;
        private CubismMouthController mouthController;
        private bool isSpeaking;

        public virtual void SetRenderCamera (Camera camera)
        {
            RenderController.CameraToFace = camera;
        }

        public virtual void SetAppearance (string appearance)
        {
            animator.SetTrigger(appearance);
        }

        public virtual void SetLookDirection (CharacterLookDirection lookDirection)
        {
            if (!controlLook) return;

            switch (lookDirection)
            {
                case CharacterLookDirection.Center:
                    lookTarget.transform.position = transform.position;
                    break;
                case CharacterLookDirection.Left:
                    lookTarget.transform.position = transform.position - new Vector3(10, 0);
                    break;
                case CharacterLookDirection.Right:
                    lookTarget.transform.position = transform.position + new Vector3(10, 0);
                    break;
            }
        }

        public virtual void SetIsSpeaking (bool value) => isSpeaking = value;

        protected virtual void Awake ()
        {
            animator = GetComponent<Animator>();
            RenderController = GetComponent<CubismRenderController>();
            lookController = GetComponent<CubismLookController>();
            mouthController = GetComponent<CubismMouthController>();

            CubismModel = RenderController.FindCubismModel();

            if (controlLook)
            {
                lookTarget = new GameObject("LookTarget").AddComponent<CubismLookTargetBehaviour>();
                lookTarget.transform.SetParent(transform, false);
                lookController.Center = transform;
                lookController.Target = lookTarget;
            }
        }

        protected virtual void Update ()
        {
            if (!controlMouth || !mouthController) return;

            mouthController.MouthOpening = isSpeaking ? Mathf.Abs(Mathf.Sin(Time.time * mouthAnimationSpeed)) * mouthAnimationLimit : 0f;
        }
    }
}
