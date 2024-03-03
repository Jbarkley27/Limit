using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthManager : MonoBehaviour
{
    private GameObject EnemyToFollow;

    [Header("Base")]
    public EnemyBase enemyBase;
    public EnemyDatabase.EnemyData enemyHealthData;

    [Header("UI")]
    public Slider enemyHealthSlider;
    public Slider enemyBarrierSlider;

    [Header("Settings")]
    public float currentHealth;
    public float currentBarrier = 0;
    public float healthSliderDropSpeed = 100f;
    public bool isDead = false;

    [Header("Health Values")]
    public float maxHealth;
    public bool hasBarrier;
    public float maxBarrier;


    // Start is called before the first frame update
    void Start()
    {
        transform.SetParent(GlobalDataReference.instance.EnemyCanvas.transform);
        SetupHealth();
    }

    // Update is called once per frame
    void Update()
    {
        if (EnemyToFollow) transform.position = Camera.main.WorldToScreenPoint(EnemyToFollow.transform.position);
        DeathWatcher();
        UpdateUISliders();
    }

    public void AssignEnemy(GameObject enemyToFollow)
    {
        EnemyToFollow = enemyToFollow;
    }

    public void SetupHealth()
    {
        // reads its health and barrier values from enemy data base
        enemyHealthData = EnemyDatabase.GetEnemyData(enemyBase.type);

        // setup health
        currentHealth = enemyHealthData.maxHealth;
        enemyHealthSlider.maxValue = currentHealth;
        enemyHealthSlider.value = currentHealth;

        // setup barrier
        if (enemyHealthData.hasBarrier)
        {
            enemyBarrierSlider.maxValue = enemyHealthData.maxBarrier;
            currentBarrier = enemyHealthData.maxBarrier;
            enemyBarrierSlider.value = currentBarrier;
        }
        else
        {
            // hide the barrier UI if we dont have one
            enemyBarrierSlider.gameObject.SetActive(false);
        }
    }

    public void TakeDamage(float damage)
    {

        // make enemy flash
        if (isDead) return;


        // enemy has barrier
        if (enemyHealthData.hasBarrier)
        {

            float difference = currentBarrier - damage;

            // enemy still have barrier left
            if (currentBarrier > 0)
            {
                // this means that we don't have to cut into health yet
                if (difference >= 0)
                {
                    currentBarrier = difference;

                }

                // this means we need to take some from barrier and health
                else
                {
                    currentHealth -= damage;
                }


                // todo check here if currentBarrier is <= 0 so we can play cool barrier break sound
            }

            // we dont have barrier and we should subtract from health
            else
            {
                currentHealth -= damage;

                if (currentHealth <= 0)
                {
                    isDead = true;
                }
            }

        }

        // enemy does not have barrier
        else
        {
            currentHealth -= damage;

            if (currentHealth <= 0)
            {
                isDead = true;
            }
        }
    }

    public void DeathWatcher()
    {
        if (isDead)
        {
            Debug.Log("I have died");

            // todo spawn explosion here , death sound, explosion sound
        }
    }

    public void UpdateUISliders()
    {
        // barrier
        if (enemyHealthData.hasBarrier) enemyBarrierSlider.value = currentBarrier;
    
        // health
        enemyHealthSlider.value = currentHealth;
    }
}
