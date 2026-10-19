using FishNet;
using FishNet.Component.Animating;
using FishNet.Object;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;

public class MeleeWeapon : AbstractWeapon
{
    //sfx
    [Tooltip("Weapon swinging audio clips")]
    [SerializeField] private AudioClip[] _swingEffects;

    [Tooltip("Body impact audio clip")]
    [SerializeField] private AudioClip _bodyImpactEffect;

    [Tooltip("Head impact audio clip")]
    [SerializeField] private AudioClip _headImpactEffect;

    [Tooltip("Block impact audio clip")]
    [SerializeField] private AudioClip _blockImpactEffect;

    private enum AudioClipIdx { Swing, BodyImpact, HeadImpact, BlockImpact }

    public float strikeRate = 1f;
    public bool canHit = false;
    public bool canDamage = false;
    public bool canBlock = false;
    [HideInInspector] public float cooldown;

    public bool ready { get { return cooldown == 0; } }


    public void Initialize()
    {
        
    }


    public void SwingAudioEffect() => PlayAudio(AudioClipIdx.Swing, .3f);


    public void OnTriggerEnter(Collider other)
    {
        //Debug.Log($"COLLISION WITH {LayerMask.LayerToName(other.gameObject.layer)}");
        if (!canHit) return;

        if (other.gameObject.tag == "PlayerHead" || other.gameObject.tag == "PlayerBody"
            || other.gameObject.tag == "MeleeWeapon" && other.gameObject.GetComponent<MeleeWeapon>().canBlock)
        {
            PlayerAttributes otherPlayer = other.transform.root.GetComponent<PlayerAttributes>();

            if (base.Owner == otherPlayer.Owner)
                Debug.Log("SELF-HIT");
            else
            {
                Debug.Log("HIT");
                float playerSize = transform.root.GetComponent<PlayerAttributes>().size;
                float damageAmount = baseDamage * playerSize;
                if (other.gameObject.tag == "PlayerHead")
                {
                    damageAmount *= 4f;
                    PlayAudio(AudioClipIdx.HeadImpact);
                }
                else if (other.gameObject.tag == "MeleeWeapon")
                {
                    damageAmount *= .1f;
                    PlayAudio(AudioClipIdx.BlockImpact, .45f);
                }
                else
                    PlayAudio(AudioClipIdx.BodyImpact);

                if (canDamage)
                    otherPlayer.TakeDamage(damageAmount / otherPlayer.size, base.Owner);

                canHit = canDamage = false;
            }

        }
    }


    public void RecoverTime(float delta)
    {
        if (cooldown > 0) cooldown = Mathf.Clamp(cooldown - delta, 0f, 1/strikeRate);
    }


    public void SetCooldown() => cooldown = 1 / strikeRate;

    private void PlayAudio(AudioClipIdx clipIdx, float volume = .6f)
    {
        if (base.IsOwner) AudioSource.PlayClipAtPoint(IdxToClip(clipIdx), transform.position, volume);
        if (base.IsServerInitialized) ObserversRpcPlayAudio(clipIdx, volume);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ObserversRpcPlayAudio(AudioClipIdx clipIdx, float volume)
        => AudioSource.PlayClipAtPoint(IdxToClip(clipIdx), transform.position, volume);

    private AudioClip IdxToClip(AudioClipIdx clipIdx)
    {
        switch (clipIdx)
        {
            case AudioClipIdx.Swing: return _swingEffects[Random.Range(0, _swingEffects.Length)];
            case AudioClipIdx.BodyImpact: return _bodyImpactEffect;
            case AudioClipIdx.HeadImpact: return _headImpactEffect;
            case AudioClipIdx.BlockImpact: return _blockImpactEffect;
            default: return null;
        }
    }
}
