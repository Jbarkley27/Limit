using System.Collections;
using System.Collections.Generic;
//using FMODUnity;
using UnityEngine;

public class BlasterSystem : MonoBehaviour
{
    [Header("Primary Blaster")]
    public float burstAmount;
    public float burstsPerShot;
    public float burstTime;
    public float spread;
    public float projectileSpeed;
    public float burstPerShotFireRate;
    public float baseFireRate;
    public float range;
    public float baseDamage;
    public GameObject testProjectile;
    //public AudioLibrary.WeaponSound primaryWeaponSound;
    public bool primaryIsAuto;

    [Header("Secondary Blaster")]
    public float burstAmountSecondary;
    public float burstsPerShotSecondary;
    public float burstTimeSecondary;
    public float spreadSecondary;
    public float projectileSpeedSecondary;
    public float burstPerShotFireRateSecondary;
    public float baseFireRateSecondary;
    public float rangeSecondary;
    public float baseDamageSecondary;
    public GameObject testProjectileSecondary;
    //public AudioLibrary.WeaponSound secondaryWeaponSound;
    public bool secondaryIsAuto;

    [Header("Settings")]
    public bool isShooting;
    public bool CanShoot;


    // Start is called before the first frame update
    void Start()
    {
        CanShoot = true;
        isShooting = false;
    }

    // Update is called once per frame
    void Update()
    {
        ListenForShooting();
    }

    // Shooting
    public void ListenForShooting()
    {
        if (InputManager.instance.ShootingPrimary)
        {
            // making sure that the player canshoot - this variable will be used for stopping shooting in the game
            // making sure their not already shooting
            // making sure they still have enought bullets given their burst amount = burstAmount * burstAmountPerShot
            //int ammoPlannedToUse = EquipSystem.instance.ActiveStatus[EquipSystem.ActiveStatusKey.ACTIVE].burstAmount *
            //        EquipSystem.instance.ActiveStatus[EquipSystem.ActiveStatusKey.ACTIVE].burstAmountPerShot;

            //if (CanShoot && !isShooting && EquipSystem.instance.CanShoot(ammoPlannedToUse) && !InputManager.instance.BlockShootingUntilShootButtonIsStopped) InitiateShootingMode();

            if (CanShoot && !isShooting && !InputManager.instance.BlockPrimaryShootingUntilShootButtonIsStopped) InitiatePrimaryShootingMode();
        }

        else if (InputManager.instance.ShootingSecondary)
        {
            if (CanShoot && !isShooting && !InputManager.instance.BlockSecondaryShootingUntilShootButtonIsStopped) InitiateSecondaryShootingMode();
        }
    }

    public void InitiatePrimaryShootingMode()
    {
        isShooting = true;

        StartCoroutine(StartPrimaryShooting());
    }


    public void InitiateSecondaryShootingMode()
    {
        isShooting = true;

        StartCoroutine(StartSecondaryShooting());
    }


    // Shooting Happens Here ==============================================================================
    public IEnumerator StartPrimaryShooting()
    {
        // will shoot however many bursts and wait between those shots
        for (int i = 0; i < burstAmount; i++)
        {
            // shoots the amount of projectils per burst
            for (int j = 0; j < burstsPerShot; j++)
            {

                // aim direction with spread added
                Vector3 aimDirectionWithSpread = PlayerAimManager.instance.GetProjectileDirection(spread);


                // Create Bullet =========================================================================
                GameObject spawnedProjectile = Instantiate(testProjectile, PlayerAimManager.instance.GetAttackPointPosition().position, Quaternion.LookRotation(aimDirectionWithSpread, Vector3.up));

                //// start the projectiles routine
                spawnedProjectile.GetComponent<PlayerBullet>().StartMoving(projectileSpeed, baseDamage, range);

                // reduce ammo
                //EquipSystem.instance.ShootActiveWeapon(1);

                // this makes it so that all shots are not fired at the same time causing it to look like one shot at first
                yield return new WaitForSeconds(burstPerShotFireRate);
            }

            //// shake screen
            //ScreenShakeManager.Instance.DoShake(ScreenShakeManager.Instance.ShootProfile);

            ////// play sound
            //RuntimeManager.PlayOneShot(AudioLibrary.GetWeaponSound(primaryWeaponSound), transform.position);

            // wait time between burst, example three round burst should have a slight wait time between them
            yield return new WaitForSeconds(burstTime);
        }

        isShooting = false;

        StartCoroutine(ApplyPrimaryFireRate(baseFireRate));
    }

    public IEnumerator StartSecondaryShooting()
    {
        // will shoot however many bursts and wait between those shots
        for (int i = 0; i < burstAmountSecondary; i++)
        {
            // shoots the amount of projectils per burst
            for (int j = 0; j < burstsPerShotSecondary; j++)
            {

                // aim direction with spread added
                Vector3 aimDirectionWithSpread = PlayerAimManager.instance.GetProjectileDirection(spreadSecondary);


                // Create Bullet =========================================================================
                GameObject spawnedProjectile = Instantiate(testProjectileSecondary, PlayerAimManager.instance.GetAttackPointPosition().position, Quaternion.LookRotation(aimDirectionWithSpread, Vector3.up));

                //// start the projectiles routine
                spawnedProjectile.GetComponent<PlayerBullet>().StartMoving(projectileSpeedSecondary, baseDamageSecondary, rangeSecondary);

                // reduce ammo
                //EquipSystem.instance.ShootActiveWeapon(1);

                // this makes it so that all shots are not fired at the same time causing it to look like one shot at first
                yield return new WaitForSeconds(burstPerShotFireRateSecondary);
            }

            //// shake screen
            //ScreenShakeManager.Instance.DoShake(ScreenShakeManager.Instance.ShootProfile);

            ////// play sound
            //RuntimeManager.PlayOneShot(AudioLibrary.GetWeaponSound(secondaryWeaponSound), transform.position);

            // wait time between burst, example three round burst should have a slight wait time between them
            yield return new WaitForSeconds(burstTimeSecondary);
        }

        isShooting = false;

        StartCoroutine(ApplySecondaryFireRate(baseFireRateSecondary));
    }


    public IEnumerator ApplyPrimaryFireRate(float fireRate)
    {
        CanShoot = false;
        yield return new WaitForSeconds(fireRate);
        CanShoot = true;

        // this ensures that the player can't keep holding down if its semi
        if (!primaryIsAuto)
        {
            InputManager.instance.BlockPrimaryShootingUntilShootButtonIsStopped = true;
        }
    }

    public IEnumerator ApplySecondaryFireRate(float fireRate)
    {
        CanShoot = false;
        yield return new WaitForSeconds(fireRate);
        CanShoot = true;

        // this ensures that the player can't keep holding down if its semi
        if (!secondaryIsAuto)
        {
            InputManager.instance.BlockSecondaryShootingUntilShootButtonIsStopped = true;
        }


    }
}
