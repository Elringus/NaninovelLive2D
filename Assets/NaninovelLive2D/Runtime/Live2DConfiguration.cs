using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// Represents configuration data for <see cref="Live2DCharacter"/>.
    /// </summary>
    [System.Serializable]
    public class Live2DConfiguration : ScriptableObject
    {
        public const string ResourcePath = "Naninovel/" + nameof(Live2DConfiguration);

        public int RenderLayer => renderLayer;
        public Camera RenderCamera => renderCamera;
        public Vector3 CameraOffset => cameraOffset;

        [Tooltip("The layer to use when rendering Live2D prefabs to render textures.")]
        [SerializeField] private int renderLayer = 30;
        [Tooltip("Camera prefab to use for rendering Live2D prefabs into render textures.")]
        [SerializeField] private Camera renderCamera = null;
        [Tooltip("Render camera ofsset from the rendered Live2D prefab.")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(0, 0, -10);

        public static Live2DConfiguration LoadFromResources ()
        {
            return Resources.Load<Live2DConfiguration>(ResourcePath);
        }
    }
}
