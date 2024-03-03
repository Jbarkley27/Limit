using UnityEngine;


public class GlobalDataReference : MonoBehaviour
{
    public static GlobalDataReference instance { get; private set; }

    [Header("Player")]
    public Transform player;

    [Header("Enemy Values")]
    public GameObject EnemyCanvas;
    public GameObject EnemyHealthPrefab;
    public GameObject AAMarker;
    public GameObject AAUICanvas;
    

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found an GlobalDataReference object, destroying new one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}

