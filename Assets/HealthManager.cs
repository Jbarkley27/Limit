using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthManager : MonoBehaviour
{
    [Header("Barrier")]
    public GameObject barrierParent;
    public GameObject chargeSprite;
    public List<GameObject> chargeList = new List<GameObject>();
    public int barrierCharges = 8;
    public GameObject emptyCharge;
    private int maxBarrierCharges;

    [Header("Health")]
    public float maxHealth = 100;
    public float currentHealth;
    public Slider healthSlider;
    public Slider hitSlider;
    private bool CanReduceFollow = true;
    Coroutine stopFollowCo;
    public float stopTime = 1f;
    private float currentT = 0f;
    public float sliderDropSpeed = 100f;
    public TMP_Text healthText;
    public bool IsDead = false;

    public static HealthManager instance { get; private set; }

    [Header("Debug")]
    public bool Invincible;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found an Health Manager object, destroying new one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Start is called before the first frame update
    void Start()
    {
        // setup health sliders
        currentHealth = maxHealth;
        healthSlider.maxValue = maxHealth;
        hitSlider.maxValue = maxHealth;
        healthSlider.value = maxHealth;
        hitSlider.value = maxHealth;

        for (int i = 0; i < barrierCharges; i++)
        {
            GameObject newCharge = Instantiate(chargeSprite, barrierParent.transform);
            newCharge.name = $"Barrier Charge {i + 1}";
            chargeList.Add(newCharge);
        }

        maxBarrierCharges = barrierCharges;
    }

    // Update is called once per frame
    void Update()
    {
        LerpHealth();
        barrierParent.GetComponent<HorizontalLayoutGroup>().spacing = AdjustBarrierSpacing();
        healthText.text = $"{Mathf.RoundToInt(currentHealth)}";
    }

    public float spacingScaler;
    public float AdjustBarrierSpacing()
    {
        return spacingScaler * maxBarrierCharges;
    }

    public void TakeDamage(int amount)
    {
        if (Invincible) return;
        if (IsDead) return;

        if (barrierCharges > 0)
        {
            barrierCharges--;
            Destroy(chargeList[barrierCharges]);
            GameObject emptyChargeObject = Instantiate(emptyCharge, barrierParent.transform);
            emptyChargeObject.name = $"Empty Barrier Charge";
        }
        else
        {

            float newHealthAmount = currentHealth - amount;

            if (newHealthAmount <= 0)
            {
                Die();
                return;
            }

            currentHealth = newHealthAmount;
            healthSlider.value = currentHealth;

            // freezing the follow bar since we just took damage
            if (stopFollowCo != null) StopCoroutine(stopFollowCo);
            stopFollowCo = StartCoroutine(FreezeBar());
        }
    }

    public IEnumerator FreezeBar()
    {
        CanReduceFollow = false;
        yield return new WaitForSeconds(stopTime);
        CanReduceFollow = true;
    }

    public void LerpHealth()
    {
        if (!CanReduceFollow) return;
        if ((barrierCharges > 0)) return;
        float healthLeft = Mathf.SmoothDamp(hitSlider.value, currentHealth, ref currentT, sliderDropSpeed * Time.deltaTime);
        hitSlider.value = healthLeft;
    }

    public void Die()
    {
        IsDead = true;
        sliderDropSpeed = sliderDropSpeed * 5;
        
    }
}
