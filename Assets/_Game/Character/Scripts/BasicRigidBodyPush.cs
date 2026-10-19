using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Player
{
    /// <summary>Lets the CharacterController push non-kinematic rigidbodies (from Unity's Starter Assets).</summary>
    public class BasicRigidBodyPush : MonoBehaviour
    {
        [FormerlySerializedAs("pushLayers")]
        [SerializeField] private LayerMask _pushLayers;

        [FormerlySerializedAs("canPush")]
        [SerializeField] private bool _canPush;

        [FormerlySerializedAs("strength")]
        [Range(0.5f, 5f)]
        [SerializeField] private float _strength = 1.1f;

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_canPush) PushRigidBody(hit);
        }

        private void PushRigidBody(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (((1 << body.gameObject.layer) & _pushLayers.value) == 0) return;

            // Don't push objects below us.
            if (hit.moveDirection.y < -0.3f) return;

            Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            body.AddForce(pushDirection * _strength, ForceMode.Impulse);
        }
    }
}
