using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AimAssistSystem : MonoBehaviour
{
    public GameObject currentEnemyToAimAssist;

    public static AimAssistSystem instance { get; private set; }

    public Vector3 aimAssistedDirection;


    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found an AimAssistSystem object, destroying new one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Update is called once per frame
    void Update()
    {
        GetAimAssistedDirection();
    }

    public float debugLength;
    public void GetAimAssistedDirection()
    {
        if (currentEnemyToAimAssist == null || !TouchingMarker) return;
        

        aimAssistedDirection = Camera.main.WorldToScreenPoint(currentEnemyToAimAssist.transform.position);
        Debug.DrawRay(GlobalDataReference.instance.player.position, aimAssistedDirection * debugLength, Color.blue);
    }

    public bool TouchingMarker;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("AAMarker"))
        {
            TouchingMarker = true;
            

            if (other.gameObject.TryGetComponent<AAMarker>(out var hitEnemy))
            {
                currentEnemyToAimAssist = hitEnemy.enemyToFollow;
            }
            
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("AAMarker"))
        {
            TouchingMarker = false;
            currentEnemyToAimAssist = null;
        }
    }
}
