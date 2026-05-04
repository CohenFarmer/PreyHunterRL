using UnityEngine;

public class ArcheryTarget : MonoBehaviour, IDamageable
{
    [HideInInspector] public float moveSpeed = 0f;
    [HideInInspector] public float arenaWidth = 40f;
    [HideInInspector] public bool movingRight = true;

    private bool destroyed = false;
    public bool IsAlive => !destroyed;

    void FixedUpdate()
    {
        if (moveSpeed <= 0f) return;

        float dir = movingRight ? 1f : -1f;
        Vector3 pos = transform.localPosition;
        pos.x += dir * moveSpeed * Time.fixedDeltaTime;

        float halfWidth = arenaWidth / 2f;
        if (pos.x > halfWidth)
        {
            pos.x = halfWidth;
            movingRight = false;
        }
        else if (pos.x < -halfWidth)
        {
            pos.x = -halfWidth;
            movingRight = true;
        }

        transform.localPosition = pos;
    }

    // one shot kill, flag prevents double trigger from same frame arrows
    public void TakeDamage(float damage)
    {
        destroyed = true;
    }
}
