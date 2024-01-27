using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBase : MonoBehaviour
{

    [SerializeField]
    [Header("General")]
    public NavMeshAgent navMeshAgent;
    public enum EnemyState { IDLE, LOOK_FOR_PLAYER, ATTACK };
    public LayerMask groundMask;
    public EnemyState current_state = EnemyState.IDLE;






    [Header("Enemy Speed Settings")]
    [Range(1, 10)]
    public float maxSpeed;

    [Range(1, 10)]
    public float minSpeed;


    [Range(10, 120)]
    public float maxAngularSpeed;

    [Range(10, 120)]
    public float minAngularSpeed;


    [Range(0, 10)]
    public float maxSpeedSwitchTime;

    [Range(0, 10)]
    public float minSpeedSwitchTime;

    Coroutine SpeedGeneratorCo;







    [Header("Animation")]
    public Animator animator;
    public GameObject innerAnimationLevel;

    [Header("Spin Animation")]
    public float maxSpinTime;
    public float minSpinTime;
    Coroutine SpinAnimationCo;
    public bool CanSpin;
    public float maxSpinDuration;
    public float minSpinDuration;



    [Header("Detection")]
    public bool PlayerNearby;





    [Header("Waypoint")]
    public List<Transform> waypoints = new List<Transform>();
    private Transform current_waypoint;
    Coroutine randomWayPointGetter;

    // the waypoint given if waypoint is somehow null
    public Transform MainWaypoint;





    [Header("Idle State")]
    public float randomWayPointSwitchTimeMin;
    public float randomWayPointSwitchTimeMax;


    [Header("Enemy Height Randomizer")]
    public GameObject heightParentObject;
    public float goalHeight;
    public float signChanger;
    public float minHeight;
    public float maxHeight;
    private Vector3 GoalHeight;

    public float minHeightChangeTime;
    public float maxHeightChangeTime;
    Coroutine RandomHeightCo;
    public float RandomHeightSpeed;

    [Range(0, 4f)]
    public float heightMaxChange;


    void Start()
    {
        current_state = EnemyState.IDLE;
    }


   
    // Update is called once per frame
    void Update()
    {
        HandleState();
    }

    public void SwitchState(EnemyState newState)
    {
        Debug.Log("Switching from " + current_state + " to " + newState);

        if (current_state == EnemyState.IDLE)
        {
            // HANDLE SWITCHING FROM IDLE STATE
            StopCoroutine(RandomWaypointGenerator());
        }


        current_state = newState;
    }


    public void HandleState()
    {
        switch (current_state)
        {
            case EnemyState.IDLE:
                RunIdleState();
                break;
            case EnemyState.LOOK_FOR_PLAYER:
                RunLookForPlayerState();
                break;
            case EnemyState.ATTACK:
                RunAttackState();
                break;
            default:
                break;
        }
    }






    // STATES ====================================

    // IDLE

    public void RunIdleState()
    {
        // Check if we need to leave Idle state
        if (PlayerNearby)
        {
            SwitchState(EnemyState.LOOK_FOR_PLAYER);
            return;
        }


        // Start the Random Way Point Generator Flow
        if (randomWayPointGetter == null) randomWayPointGetter = StartCoroutine(RandomWaypointGenerator());

        if (SpeedGeneratorCo == null) SpeedGeneratorCo = StartCoroutine(RandomSpeedGenerator());

        if (RandomHeightCo == null) RandomHeightCo = StartCoroutine(RandomHeightMovement());

        if (CanSpin) SpinAnimationCo = StartCoroutine(RandomSpinAnimation());

        RandomHeightMovementHelper();

        EnemyAgentMovement();

        // Play Animation
        //animator.Play("EnemyHoverIdle");
    }


    // LIFE SYSTEM

    IEnumerator RandomWaypointGenerator()
    {
        while (current_state == EnemyState.IDLE)
        {
            // Get Random Waypoint
            current_waypoint = waypoints[Random.Range(0, waypoints.Count - 1)];

            // Wait X Seconds for next Waypoint
            yield return new WaitForSeconds(Random.Range(randomWayPointSwitchTimeMin, randomWayPointSwitchTimeMax));
        }
    }


    IEnumerator RandomSpeedGenerator()
    {
        while (current_state == EnemyState.IDLE)
        {
            // Get Random Waypoint
            navMeshAgent.speed = Random.Range(minSpeed, maxSpeed);
            navMeshAgent.angularSpeed = Random.Range(minAngularSpeed, maxAngularSpeed);

            
            yield return new WaitForSeconds(Random.Range(minSpeedSwitchTime, maxSpeedSwitchTime));
        }
    }


    IEnumerator RandomHeightMovement()
    {

        while (current_state == EnemyState.IDLE)
        {
            Vector3 goal = new Vector3(heightParentObject.transform.position.x, heightParentObject.transform.position.y, heightParentObject.transform.position.z);

            goal.y += signChanger * (Random.Range(0.1f, heightMaxChange));

            goal.y = Mathf.Clamp(goal.y, minHeight, maxHeight);

            GoalHeight = goal;

            signChanger *= -1f;

            animator.speed = Random.Range(.3f, 1);

            yield return new WaitForSeconds(Random.Range(minHeightChangeTime, maxHeightChangeTime));
        }


    }


    IEnumerator RandomSpinAnimation()
    {
        CanSpin = false;
        float startRotation = innerAnimationLevel.transform.eulerAngles.z;
        float endRotation = startRotation + 360.0f;
        float t = 0.0f;
        float duration = Random.Range(minSpinDuration, maxSpinDuration);
        while (t < duration)
        {
            t += Time.deltaTime;
            float zRotation = Mathf.Lerp(startRotation, endRotation, t / duration) % 360.0f;
            innerAnimationLevel.transform.eulerAngles = new Vector3(innerAnimationLevel.transform.eulerAngles.x, innerAnimationLevel.transform.eulerAngles.y, zRotation);
            yield return null;
        }

        yield return new WaitForSeconds(Random.Range(minSpinTime, maxSpinTime));

        CanSpin = true;
    }
    public void RandomHeightMovementHelper()
    {
        GoalHeight.x = heightParentObject.transform.position.x;
        GoalHeight.z = heightParentObject.transform.position.z;

        heightParentObject.transform.position = Vector3.Lerp(heightParentObject.transform.position, GoalHeight, Time.deltaTime * RandomHeightSpeed);
    }



    // MOVEMENT

    public void EnemyAgentMovement()
    {
        if(current_state == EnemyState.IDLE)
        {
            // IDLE MOVEMENT
            Vector3 groundHitPoint = MainWaypoint.position;

            if (current_waypoint != null)
            {
                if (Physics.Raycast(current_waypoint.position, -current_waypoint.up, out var hitInfo, Mathf.Infinity, groundMask))
                {
                    groundHitPoint = hitInfo.point;
                }
            }
            


            GetComponent<NavMeshAgent>().destination = groundHitPoint;
            transform.rotation = Quaternion.LookRotation(GetComponent<NavMeshAgent>().velocity);
        }


        
    }














    // LOOK FOR PLAYER

    public void RunLookForPlayerState()
    {

    }







    // ATTACK

    public void RunAttackState()
    {

    }

    
    public void Move()
    {
        //Vector3 groundHitPoint;
        //if (Physics.Raycast(EnemyFollowObject.transform.position, -EnemyFollowObject.transform.up, out var hitInfo, Mathf.Infinity, groundMask))
        //{
        //    groundHitPoint = hitInfo.point;
        //}

        //else
        //{
        //    groundHitPoint = homeBaseObject.transform.position;
        //}
        //GetComponent<NavMeshAgent>().destination = groundHitPoint;
        
    }
}
