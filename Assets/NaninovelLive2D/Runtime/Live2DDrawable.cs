using System;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace Naninovel
{
    public readonly struct Live2DDrawable : IEquatable<Live2DDrawable>
    {
        public readonly CubismRenderer CubismRenderer;
        public readonly Transform Transform;
        public readonly Material RenderMaterial;
        public MeshRenderer MeshRenderer => CubismRenderer.MeshRenderer;
        public Mesh Mesh => CubismRenderer.Mesh;
        public Vector2 Position => Transform.position;
        public Quaternion Rotation => Transform.localRotation;
        public Vector2 Scale => Transform.lossyScale;

        public Live2DDrawable (CubismRenderer drawable)
        {
            CubismRenderer = drawable;
            Transform = drawable.transform;
            CubismRenderer.MeshRenderer.forceRenderingOff = true;
            RenderMaterial = CubismRenderer.Material;
            RenderMaterial.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        }
        
        public bool Equals (Live2DDrawable other) => CubismRenderer.Equals(other.CubismRenderer);
        public override bool Equals (object obj) => obj is Live2DDrawable other && Equals(other);
        public override int GetHashCode () => CubismRenderer.GetHashCode();
        public static bool operator == (Live2DDrawable left, Live2DDrawable right) => left.Equals(right);
        public static bool operator != (Live2DDrawable left, Live2DDrawable right) => !left.Equals(right);
    }
}
