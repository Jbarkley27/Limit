using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AAMarker : MonoBehaviour
{

    public GameObject enemyToFollow;
    public EnemyBase enemyBase;
    public GameObject enemyGameObject;

    // Start is called before the first frame update
    void Start()
    {
        transform.SetParent(GlobalDataReference.instance.AAUICanvas.transform);
    }

    // Update is called once per frame
    void Update()
    {
        if (enemyToFollow != null) transform.position = Camera.main.WorldToScreenPoint(enemyToFollow.transform.position);
    }
}
