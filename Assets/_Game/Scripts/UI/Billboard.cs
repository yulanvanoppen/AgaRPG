using UnityEngine;

namespace AgaRPG.UI
{
    /// <summary>Helpers for world-space UI that should always face the player's camera.</summary>
    public static class Billboard
    {
        /// <summary>Rotates <paramref name="target"/> to match the main camera's rotation.</summary>
        public static void FaceMainCamera(Transform target)
        {
            Camera camera = Camera.main;
            if (camera != null) target.rotation = camera.transform.rotation;
        }
    }
}
