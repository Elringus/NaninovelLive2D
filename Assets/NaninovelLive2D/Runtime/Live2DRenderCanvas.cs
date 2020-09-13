using UnityEngine;

namespace Naninovel
{
    public class Live2DRenderCanvas : MonoBehaviour
    {
        public Vector2 Size = Vector2.one;
        
        private void OnDrawGizmos ()
        {
            Gizmos.DrawWireCube(transform.position, Size);
        }
    }
}
