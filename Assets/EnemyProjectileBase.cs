using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class EnemyProjectileBase : MonoBehaviour
{

    public int damage;
    public float range;


    public void StartMoving(float force, int damage, float range, bool isWaterfall = false)
    {
        this.damage = damage;
        this.range = range;

        // apply force to begin moving
        gameObject.GetComponent<Rigidbody>().AddForce(
            (isWaterfall ? gameObject.transform.up : gameObject.transform.forward) *
            force,
            ForceMode.Impulse
            );

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
        if (collider.gameObject.CompareTag("Player"))
        {
            Debug.Log("Hit Player");
            HealthManager.instance.TakeDamage(damage);
            Destroy(gameObject);
        }

        // make sure the bullet doesn't collide with another enemy or themselves
        else if (
            !collider.gameObject.CompareTag("Enemy") &&
            !collider.gameObject.CompareTag("EnemyBullet") &&
            !collider.gameObject.CompareTag("DetectionVolume") &&
            !collider.gameObject.CompareTag("PlayerProjectile")
            )
        {
            Debug.Log($"Name: {collider.gameObject.name}");
            Debug.Log($"Tag: {collider.gameObject.tag}");
            Debug.Log($"Layer: {LayerMask.LayerToName(collider.gameObject.layer)}");

            Destroy(gameObject);
        }
    }

}
