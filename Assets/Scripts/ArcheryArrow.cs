using UnityEngine;

public class ArcheryArrow : MonoBehaviour
{
    [HideInInspector] public ArcheryHunterAgent owner;
    [HideInInspector] public float damage = 10f;

    private bool hasHit = false;
    private bool callbackFired = false;

    void OnCollisionEnter(Collision collision)
    {
        if (hasHit) return;
        hasHit = true;

        IDamageable target = collision.gameObject.GetComponentInParent<IDamageable>();

        if (target != null && target.IsAlive)
        {
            target.TakeDamage(damage);
            if (owner != null) owner.OnArrowHit();
        }
        else
        {
            if (owner != null) owner.OnArrowMissed();
        }
        callbackFired = true;
        Destroy(gameObject);
    }

    // arrow timed out without hitting anything, agent still needs to know
    void OnDestroy()
    {
        if (!callbackFired && owner != null)
        {
            owner.OnArrowMissed();
        }
    }
}
