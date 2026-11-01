using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor.Baking
{
    /// <summary>Plain per-root inputs for wind baking.</summary>
    public struct WindTarget
    {
        public string Path;
        public Vector3 RestDirection;
        public Quaternion ParentRotation;
        public Quaternion LocalRotation;
    }
}
