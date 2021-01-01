using System.Collections.Generic;
using System.Linq;
using Live2D.Cubism.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Naninovel
{
    /// <summary>
    /// Performs drawing of <see cref="Live2DController"/> to a <see cref="TransitionalRenderer"/>.
    /// </summary>
    public class Live2DDrawer
    {
        private readonly Live2DController controller;
        private readonly List<Live2DDrawable> drawables;
        private readonly CommandBuffer commandBuffer = new CommandBuffer();
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        private readonly Vector3 canvasSize, canvasOffset;

        private RenderTexture renderTexture;

        public Live2DDrawer (Live2DController controller)
        {
            this.controller = controller;
            commandBuffer.name = $"Naninovel-DrawLive2D-{controller.name}";
            drawables = InitializeDrawables(controller);
            (canvasSize, canvasOffset) = InitializeCanvas(controller);
                        
            // Align underlying model game object with the render texture position.
            controller.transform.localPosition += new Vector3(0, canvasSize.y / 2);
        }

        public void Dispose ()
        {
            if (renderTexture)
                RenderTexture.ReleaseTemporary(renderTexture);
        }

        public void DrawTo (TransitionalRenderer renderer, int pixelsPerUnit)
        {
            if (drawables.Count == 0) return;

            var drawDimensions = canvasSize * pixelsPerUnit;
            var drawPosition = controller.transform.position + canvasOffset;
            var orthoMin = Vector3.Scale(-drawDimensions / 2f, renderer.transform.localScale) + drawPosition * pixelsPerUnit;
            var orthoMax = Vector3.Scale(drawDimensions / 2f, renderer.transform.localScale) + drawPosition * pixelsPerUnit;
            var orthoMatrix = Matrix4x4.Ortho(orthoMin.x, orthoMax.x, orthoMin.y, orthoMax.y, float.MinValue, float.MaxValue);
            var rotationMatrix = Matrix4x4.Rotate(Quaternion.Inverse(renderer.transform.localRotation));

            PrepareRenderTexture(drawDimensions, renderer);
            PrepareCommandBuffer(orthoMatrix);
            SortDrawables();
            foreach (var drawable in drawables)
                DrawDrawable(drawable, rotationMatrix, pixelsPerUnit);
            Graphics.ExecuteCommandBuffer(commandBuffer);
        }
        
        private static List<Live2DDrawable> InitializeDrawables (Live2DController controller)
        {
            controller.CubismModel.ForceUpdateNow(); // Required to build meshes.
            return controller.RenderController.Renderers
                .Select(cd => new Live2DDrawable(cd))
                .OrderBy(d => d.MeshRenderer.sortingOrder)
                .ThenByDescending(d => d.Transform.position.z).ToList();
        }

        private static (Vector2 size, Vector2 offset) InitializeCanvas (Live2DController controller)
        {
            if (controller.TryGetComponent<RenderCanvas>(out var renderCanvas))
                return (renderCanvas.Size, renderCanvas.Offset);
            else
            {
                var bounds = controller.RenderController.Renderers.GetMeshRendererBounds();
                var size = new Vector2(bounds.size.x, bounds.size.y);
                return (size, Vector2.zero);
            }
        }
        
        private void PrepareRenderTexture (Vector2 drawDimensions, TransitionalRenderer renderer)
        {
            var requiredSize = new Vector2Int(Mathf.RoundToInt(drawDimensions.x), Mathf.RoundToInt(drawDimensions.y));
            if (CurrentTextureValid()) return;
            
            if (renderTexture) 
                RenderTexture.ReleaseTemporary(renderTexture);
            renderTexture = RenderTexture.GetTemporary(requiredSize.x, requiredSize.y);
            renderer.MainTexture = renderTexture;

            bool CurrentTextureValid () => renderTexture && renderTexture.width == requiredSize.x && renderTexture.height == requiredSize.y;
        }

        private void PrepareCommandBuffer (Matrix4x4 orthoMatrix)
        {
            commandBuffer.Clear();
            commandBuffer.SetRenderTarget(renderTexture);
            commandBuffer.ClearRenderTarget(true, true, Color.clear);
            commandBuffer.SetProjectionMatrix(orthoMatrix);
        }

        private void SortDrawables ()
        {
            var mode = controller.RenderController.SortingMode;
            if (mode == CubismSortingMode.BackToFrontOrder || mode == CubismSortingMode.BackToFrontZ)
                drawables.Sort((x, y) => y.Transform.position.z.CompareTo(x.Transform.position.z));
            else drawables.Sort((x, y) => x.Transform.position.z.CompareTo(y.Transform.position.z));
        }

        private void DrawDrawable (Live2DDrawable drawable, Matrix4x4 rotationMatrix, int pixelsPerUnit)
        {
            if (!drawable.MeshRenderer.enabled) return;
            
            var transform = controller.transform;
            var drawPosition = transform.TransformPoint(rotationMatrix // Compensate actor (parent game object) rotation.
                .MultiplyPoint3x4(transform.InverseTransformPoint(drawable.Position)));
            var drawTransform = Matrix4x4.TRS(drawPosition * pixelsPerUnit, drawable.Rotation, drawable.Scale * pixelsPerUnit);
            drawable.MeshRenderer.GetPropertyBlock(propertyBlock);
            commandBuffer.DrawMesh(drawable.Mesh, drawTransform, drawable.RenderMaterial, 0, -1, propertyBlock);
        }
    }
}
