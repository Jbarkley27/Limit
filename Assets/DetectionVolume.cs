using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectionVolume : MonoBehaviour
{
    public static bool EnemyDetected;
    public bool inspectorVisibility_EnemyDetected;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        inspectorVisibility_EnemyDetected = EnemyDetected;
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Player"))
        {
            EnemyDetected = true;

        }
    }
}
