using UnityEngine;

namespace Naninovel
{
    /// <summary>
    /// Represents configuration data for <see cref="Live2DCharacter"/>.
    /// </summary>
    [System.Serializable]
    public class Live2DConfiguration : Configuration
    {
        [Tooltip("The layer to use when rendering Live2D prefabs to render textures.")]
        public int RenderLayer = 30;
        [Tooltip("Camera prefab to use for rendering Live2D prefabs into render textures. Will use a default prefab when not provided.")]
        public Camera CustomRenderCamera = null;
        [Tooltip("Render camera ofsset from the rendered Live2D prefab.")]
        public Vector3 CameraOffset = new Vector3(0, 0, -10);
    }
}
