using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyAttackLibrary : MonoBehaviour
{
    public enum AttackType { SINGLE_SHOT, CLUSTER_SHOT };

    public static EnemyAttackLibrary instance { get; private set; }

    public GameObject singleShotBullet;
    public GameObject clusterShotBullet;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found an Experience Manager object, destroying new one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }


    public void UseAttack(AttackType attackType, Transform attackSource, Transform attackDestination)
    {
        switch (attackType)
        {
            case AttackType.SINGLE_SHOT:
                UseSingleShot(attackSource, attackDestination);
                break;
            case AttackType.CLUSTER_SHOT:
                ClusterShot(clusterProjectileAmount, attackSource, attackDestination, clusterSpread);
                break;
        }
    }


    #region Attacks
    [Header("Single Shot")]
    public float singleShotForce;
    public int damage;
    public float range;

    public void UseSingleShot(Transform attackSource, Transform attackDestination)
    {
        Vector3 direction = attackDestination.position - attackSource.position;
        //Debug.Log("Used Single Shot");
        GameObject bullet = Instantiate(singleShotBullet, attackSource.position, Quaternion.LookRotation(direction, Vector3.up));
        bullet.GetComponent<EnemyProjectileBase>().StartMoving(singleShotForce, damage, range);
    }






    //===================================================================


    [Header("Cluster Shot")]
    public float clustShotForceMin;
    public float clustShotForceMax;
    public int clusterDamage;
    public float clusterRange;
    public int clusterProjectileAmount;
    public float clusterSpread;

    
    public float clusterDelayBetweenBullets;


    public void ClusterShot(int numberOfBullets, Transform attackSource, Transform playerPosition, float spread)
    {
        StartCoroutine(LaunchBulletPattern(numberOfBullets, attackSource, clusterShotBullet, playerPosition, spread, clusterDelayBetweenBullets));
    }


    IEnumerator LaunchBulletPattern(int numberOfBullets, Transform attackSource, GameObject bulletPrefab,Transform playerPosition, float spread, float delayBetweenBullets)
    {

        for (int i = 0; i < numberOfBullets; i++)
        {
            // Calculate the direction towards the player with added spread
            Vector3 direction = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-spread, spread)) * (playerPosition.position - attackSource.position).normalized;

            // Instantiate a bullet
            GameObject bullet = Instantiate(bulletPrefab, attackSource.position, Quaternion.LookRotation(direction, Vector3.up));
            bullet.GetComponent<EnemyProjectileBase>().StartMoving(Random.Range(clustShotForceMin, clustShotForceMax), clusterDamage, clusterRange);

            // Wait for the delay before launching the next bullet
            yield return new WaitForSeconds(delayBetweenBullets);
        }
    }


    //===================================================================

    [Header("Line Shot")]
    public float lineShotForce;
    public int lineDamage;
    public float lineRange;
    public float lineDelayBetweenBullets;
    public float lineSpread;
    public float lineTimeToShoot;


    public void LineShot(float timeToShoot, Transform playerPosition, GameObject bulletPrefab, Transform attackSource, float spread)
    {
        StartCoroutine(LaunchLineBulletPattern(lineTimeToShoot, playerPosition, bulletPrefab, attackSource, lineSpread));
    }


    IEnumerator LaunchLineBulletPattern(float timeToShoot, Transform playerPosition, GameObject bulletPrefab, Transform attackSource, float spread)
    {

        float startTime = Time.time;

        while (Time.time - startTime < timeToShoot)
        {
            // Calculate the direction towards the player with added spread
            Vector3 direction = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-spread, spread)) * (playerPosition.position - attackSource.position).normalized;

            // Instantiate a bullet
            GameObject bullet = Instantiate(bulletPrefab, attackSource.position, Quaternion.LookRotation(direction, Vector3.up));
            bullet.GetComponent<EnemyProjectileBase>().StartMoving(lineShotForce, lineDamage, lineRange);

            // Wait for the delay before launching the next bullet
            yield return new WaitForSeconds(lineDelayBetweenBullets);
        }
    }


    //=============================================================



    [Header("Grid")]
    public GameObject testPrefab;
    public float gridShootForce;
    public float gridDebugLength;
    public int rowGridAngle;
    public int rowGridCount;
    public int columnGridCount;
    public int columnGridAngle;
    public int gridDamage;
    public float gridRange;




    public void Grid(Transform sourcePos, Transform player)
    {

        if (sourcePos == null || player == null) return;
        int circleRadius = 360;

        // facing direction of the enemy
        Vector3 direction = sourcePos.forward;

        // get the correct starting angle/direction for the rows
        int rowStartAngle = circleRadius - (rowGridAngle * (rowGridCount / 2));


        // Handles How Many Rows for the grid
        for (int i = 0; i < rowGridCount; i++)
        {
            // rotates the starting direction to the proper angle
            Vector3 rowRotatedVector = Quaternion.AngleAxis(rowStartAngle, Vector3.right) * direction;

            //Debug.DrawRay(sourcePos.position, rowRotatedVector * fanDebugLength);







            // get the correct starting angle/direction for the columns
            int columnStartAngle = circleRadius - (columnGridAngle * (columnGridCount / 2));

            for (int j = 0; j < columnGridCount; j++)
            {
                // rotates the starting direction for the columns based on which row is it currently on
                Vector3 rotatedVector = Quaternion.AngleAxis(columnStartAngle, Vector3.up) * rowRotatedVector;

                //Debug.DrawRay(sourcePos.position, rotatedVector * fanDebugLength);



                // Instantiate a bullet
                GameObject bullet = Instantiate(testPrefab, sourcePos.position, Quaternion.LookRotation(rotatedVector, Vector3.up));
                bullet.GetComponent<EnemyProjectileBase>().StartMoving(gridShootForce, gridDamage, gridRange);

                // increment to get next column angle
                columnStartAngle += columnGridAngle;
            }






            // increment to get next row angle
            rowStartAngle += rowGridAngle;
        }

    }










    //=============================================================
    [Header("Ring")]
    public GameObject ringPrefab;
    public float ringRange;
    public Vector3 ringFinalScale = new Vector3(100, 5, 100);

    public void Ring(Transform source)
    {
        GameObject newRing = Instantiate(ringPrefab, source.position, source.rotation);

        // Use DOTween to smoothly scale the object to the target scale
        newRing.transform.DOScale(ringFinalScale, ringRange)
            .SetEase(Ease.OutQuad) // You can change the ease type as needed
            .OnComplete(() => Debug.Log("Scale animation complete")); // Optional callback when the animation is complete
    }







    //=============================================================
    [Header("Single Circle Projectiles")]
    public int numberOfProjectiles;
    public float circleForce;
    public int circleDamage;
    public float circleRange;
    public float circleDebugLength;

    public void ShootOutwardCircle(Transform sourcePosition, GameObject bulletPrefab)
    {
        // facing direction of the enemy
        Vector3 direction = sourcePosition.forward;

        int angleStep = 360 / numberOfProjectiles;
        int circleStartAngle = 0;

        for (int i = 0; i < numberOfProjectiles; i++)
        {
            Vector3 rotatedVector = Quaternion.AngleAxis(circleStartAngle, Vector3.up) * direction;

            //Debug.DrawRay(sourcePosition.position, rotatedVector * circleDebugLength);
            // Instantiate a bullet
            GameObject bullet = Instantiate(testPrefab, sourcePosition.position, Quaternion.LookRotation(rotatedVector, Vector3.up));
            bullet.GetComponent<EnemyProjectileBase>().StartMoving(circleForce, circleDamage, circleRange);

            circleStartAngle += angleStep; ;
        }


    }












    //=============================================================
    [Header("Explosion")]
    public int explosionNumOfProjectiles;
    public float explosionForce;
    public int explosionDamage;
    public float explosionRange;
    public float explosionDebugLength;
    public float explosionDelayBetweenShotsTime;

    public void StartExplosionAttack(Transform sourcePos)
    {
        StartCoroutine(ExplosionProjectiles(sourcePos));
    }

    IEnumerator ExplosionProjectiles(Transform sourcePos)
    {
        for (int i = 0; i < explosionNumOfProjectiles; i++)
        {
            Vector3 randomPoint = Random.onUnitSphere * 150;

            // Instantiate a bullet
            GameObject bullet = Instantiate(testPrefab, sourcePos.position, Quaternion.LookRotation(randomPoint, Vector3.up));
            bullet.GetComponent<EnemyProjectileBase>().StartMoving(explosionForce, explosionDamage, explosionRange);

            //Debug.DrawRay(sourcePos.position, randomPoint * explosionDebugLength);

            yield return new WaitForSeconds(explosionDelayBetweenShotsTime);

        }
    }




    #endregion

}