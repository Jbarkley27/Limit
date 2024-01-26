using UnityEngine;
using System.Collections;

public class PlayerAimManager : MonoBehaviour
{

    public static PlayerAimManager instance { get; private set; }

    public Vector3 AimDirection;

    [Header("Settings")]
    //[SerializeField] private float sightLength = 999999f;
    //[SerializeField] private LayerMask aimColliderLayerMask = new LayerMask();

    [Header("Debugging")]
    //public Transform debugPoint;

    //public bool aimingAtEnemy = false;

    //[SerializeField] private LayerMask enemyLayerMask;
    [SerializeField] private Vector3 mouseWorldPosition;
    [SerializeField] private Transform attackPoint;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found an Aim Manager, destroying new one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Use this for initialization
    void Start()
    {
        //enemyLayerMask = LayerMask.NameToLayer("Enemy");
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        //Aim();
    }

    public Transform GetAttackPointPosition()
    {
        return attackPoint.transform;
    }

    //private void Aim()
    //{

    //    Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
    //    Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);

    //    RaycastHit raycastHit;
    //    if (Physics.Raycast(ray, out raycastHit, sightLength, aimColliderLayerMask))
    //    {
    //        debugPoint.position = raycastHit.point;
    //        AimDirection = raycastHit.point;

    //        AimDirection = debugPoint.transform.position - attackPoint.position;

    //        if (raycastHit.transform.gameObject.layer == enemyLayerMask)
    //        {
    //            aimingAtEnemy = true;
    //        }
    //        else
    //        {
    //            aimingAtEnemy = false;
    //        }
    //    }

    //    else
    //    {
    //        aimingAtEnemy = false;
    //    }

    //}

    public Vector3 GetProjectileDirection(float spread)
    {
        Vector3 directionWithoutSpread = PlayerController.AimDirection;

        float xFactor = Random.Range(0, 1f);
        float yFactor = Random.Range(0, 1f);

        //Calculate spread
        float x = xFactor * spread * GetRandomNegOrPositive();
        float y = yFactor * spread * GetRandomNegOrPositive();

        //calc new direction with spread
        Vector3 directionWithSpread = directionWithoutSpread + new Vector3(x, y, 0); // add spread to last digit

        return directionWithSpread.normalized;
    }

    public int GetRandomNegOrPositive()
    {
        float positiveOrNeg = Random.Range(0, 1);
        int negFactor = 0;

        if (positiveOrNeg > 0.5f)
        {
            negFactor = -1;
        }
        else
        {
            negFactor = 1;
        }

        return negFactor;
    }

    //public bool CheckIfEnemy()
    //{
    //    return aimingAtEnemy;
    //}
}

