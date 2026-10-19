using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>Base class of all networked weapons a player can equip.</summary>
    public abstract class AbstractWeapon : NetworkBehaviour
    {
        [Tooltip("Damage per hit at player size 1, before hit-zone multipliers")]
        [FormerlySerializedAs("baseDamage")]
        [SerializeField] private float _baseDamage = 10f;

        /// <summary>Damage multiplier for head hits.</summary>
        protected const float HeadshotMultiplier = 4f;

        public float BaseDamage => _baseDamage;
    }
}
