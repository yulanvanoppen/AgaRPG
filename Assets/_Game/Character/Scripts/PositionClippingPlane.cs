using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Feeds a clipping plane to the arrow-clip material so arrows outside the weapon's
    /// magazine are cut off as the clip slides.
    /// </summary>
    public class PositionClippingPlane : NetworkBehaviour
    {
        [FormerlySerializedAs("offset")]
        [SerializeField] private float _offset;

        private static readonly int PointId = Shader.PropertyToID("_Point");
        private static readonly int NormalId = Shader.PropertyToID("_Normal");

        private Material _material;

        private void Awake() => _material = GetComponent<MeshRenderer>().material;

        private void Update()
        {
            Vector3 up = transform.up;
            _material.SetVector(PointId, transform.parent.position - _offset * up);
            _material.SetVector(NormalId, -up);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
