using System.Collections;
using UnityEngine;

public class PlayerBullet : MonoBehaviour
{

    public float damage;
    public float range;

    public void StartMoving(float force, float damage, float range)
    {
        this.damage = damage;
        this.range = range;

        // apply force to begin moving
        gameObject.GetComponent<Rigidbody>().AddForce(gameObject.transform.forward * force, ForceMode.Impulse);

        StartCoroutine(RangeLifeTime());
    }

    // this controls the range
    public IEnumerator RangeLifeTime()
    {
        yield return new WaitForSeconds(range);

        if (gameObject != null)
        {
            Destroy(gameObject);
        }
    }


    private void OnTriggerEnter(Collider collider)
    {
        if (collider.gameObject.layer == LayerMask.NameToLayer("Enemy"))
        {
            //GlobalDataReference.instance.reticleManager.ShowDamageReticle();

            if (collider.gameObject.TryGetComponent<HitFlash>(out var hitEnemy))
            {
                hitEnemy.Flash();
            }
            else
            {
                Debug.Log("NULL");
            }
            Debug.Log("Hit -- " + collider.name);
            Destroy(gameObject);
        }

        // make sure the bullet doesn't collide with the player or themselves
        else if (
            !collider.gameObject.CompareTag("Player") &&
            !collider.gameObject.CompareTag("PlayerProjectile")
            )
        {
            Debug.Log("Hit -- " + collider.name);
            Destroy(gameObject);
        }
    }
}
