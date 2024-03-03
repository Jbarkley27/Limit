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

    [Header("Health UI")]
    public EnemyHealthManager enemyHealth;
    public GameObject UIPlacementPosition;
    public EnemyDatabase.EnemyType type;


    public Rigidbody meshRigidbody;


    [Header("Debug")]
    public bool isActive = true;

    void Start()
    {
        if (!isActive) return;

        current_state = EnemyState.IDLE;
        meshRigidbody.isKinematic = true;

        GameObject HealthUI = Instantiate(GlobalDataReference.instance.EnemyHealthPrefab);
        enemyHealth = HealthUI.GetComponent<EnemyHealthManager>();
        enemyHealth.AssignEnemy(UIPlacementPosition);
        enemyHealth.enemyBase = GetComponent<EnemyBase>();
    }



    // Update is called once per frame
    void Update()
    {
        if (!isActive) return;
        HandleState();
        EnemyAgentMovement();
        EffectiveRangeFinder();

        if(enemyHealth.isDead && !IsDying)
        {
            StartCoroutine(Die());
        }
    }

    [Header("Death")]

    public bool IsDying;
    public float deathForce;

    public IEnumerator Die()
    {
        IsDying = true;
        

        yield return new WaitForSeconds(.4f);

        meshRigidbody.isKinematic = false;

        meshRigidbody.AddForce(deathForce * -transform.up, ForceMode.VelocityChange);

        yield return new WaitForSeconds(1f);
        Destroy(actualmesh);
        Destroy(enemyHealth.gameObject);
        Destroy(gameObject);
    }

    public void SwitchState(EnemyState newState)
    {
        Debug.Log("Switching from " + current_state + " to " + newState);

        if (current_state == EnemyState.IDLE)
        {
            // HANDLE SWITCHING FROM IDLE STATE
            StopCoroutine(randomWayPointGetter);
            StopCoroutine(RandomHeightCo);
            StopCoroutine(SpinAnimationCo);
            StopCoroutine(SpeedGeneratorCo);
            StartCoroutine(ResetSpinRotation());
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
            case EnemyState.ATTACK:
                RunAttackState();
                break;
            default:
                break;
        }
    }






    // STATES ====================================

    // IDLE

    #region Idle

    public void RunIdleState()
    {
        // Check if we need to leave Idle state
        if (DetectionVolume.EnemyDetected)
        {
            Debug.Log("Switching to Attack State");
            SwitchState(EnemyState.ATTACK);
            return;
        }


        // Start the Random Way Point Generator Flow
        if (randomWayPointGetter == null) randomWayPointGetter = StartCoroutine(RandomWaypointGenerator());

        if (SpeedGeneratorCo == null) SpeedGeneratorCo = StartCoroutine(RandomSpeedGenerator());

        if (RandomHeightCo == null) RandomHeightCo = StartCoroutine(RandomHeightMovement());

        if (CanSpin) SpinAnimationCo = StartCoroutine(RandomSpinAnimation());

        RandomHeightMovementHelper();

        
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
            yield return new WaitForSeconds(Random.Range(minSpeedSwitchTime, maxSpeedSwitchTime));
        }
    }


    IEnumerator RandomHeightMovement()
    {

        while (current_state == EnemyState.IDLE && !enemyHealth.isDead)
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

    IEnumerator ResetSpinRotation()
    {

        float startRotation = innerAnimationLevel.transform.eulerAngles.z;
        //float endRotation = 360.0f - startRotation;
        float t = 0.0f;
        float duration = Random.Range(minSpinDuration, maxSpinDuration);
        while (t < duration)
        {
            t += Time.deltaTime;
            float zRotation = Mathf.Lerp(startRotation, 0, t / duration) % 360.0f;
            innerAnimationLevel.transform.eulerAngles = new Vector3(innerAnimationLevel.transform.eulerAngles.x, innerAnimationLevel.transform.eulerAngles.y, zRotation);
            yield return null;
        }
    }


    public void RandomHeightMovementHelper()
    {
        GoalHeight.x = heightParentObject.transform.position.x;
        GoalHeight.z = heightParentObject.transform.position.z;

        heightParentObject.transform.position = Vector3.Lerp(heightParentObject.transform.position, GoalHeight, Time.deltaTime * RandomHeightSpeed);
    }


    #endregion

    // MOVEMENT

    public void EnemyAgentMovement()
    {
        
        // IDLE MOVEMENT =====================
        if (current_state == EnemyState.IDLE)
        {
            Vector3 groundHitPoint = MainWaypoint.position;

            if (current_waypoint != null)
            {
                if (Physics.Raycast(current_waypoint.position, -current_waypoint.up, out var hitInfo, Mathf.Infinity, groundMask))
                {
                    groundHitPoint = hitInfo.point;
                }
            }
            


            navMeshAgent.destination = groundHitPoint;

            Quaternion toRotation = Quaternion.LookRotation(GetComponent<NavMeshAgent>().velocity);
            transform.rotation = Quaternion.Lerp(transform.rotation, toRotation, Random.Range(minAngularSpeed, maxAngularSpeed) * Time.deltaTime);
        }

        // ATTACK MOVEMENT ======================
        else if (current_state == EnemyState.ATTACK)
        {
            // PLAYER NAVMESH DESTINATION
            Transform player = GlobalDataReference.instance.player;
            Vector3 groundHitPoint = player.position;
            groundHitPoint.y = 0;

            // IF ENEMY CANT SEE PLAYER
            if (!CanSeePlayer && !inEffectiveRange)
            {
                navMeshAgent.destination = groundHitPoint;
                navMeshAgent.speed = Random.Range(LookForPlayerMovementSpeedMin, LookForPlayerMovementSpeedMax);
                navMeshAgent.angularSpeed = Random.Range(LookForPlayerAngularSpeedMin, LookForPlayerAngularSpeedMax);


                if (!Mathf.Approximately(navMeshAgent.velocity.sqrMagnitude, 0f))
                {
                    Quaternion toRotation = Quaternion.LookRotation(GetComponent<NavMeshAgent>().velocity);
                    transform.rotation = Quaternion.Lerp(transform.rotation, toRotation, Random.Range(LookForPlayerAngularSpeedMin, LookForPlayerAngularSpeedMax) * Time.deltaTime);
                }
                   


                CanAttack = false;
            }

            else
            {

                if(!inEffectiveRange)
                {
                    
                    navMeshAgent.destination = groundHitPoint;
                    navMeshAgent.speed = Random.Range(AttackMovementSpeedMin, AttackMovementSpeedMax);
                    navMeshAgent.angularSpeed = Random.Range(AttackAngularSpeedMin, AttackAngularSpeedMax);

                    if (!Mathf.Approximately(navMeshAgent.velocity.sqrMagnitude, 0f))
                    {
                        Quaternion toRotation = Quaternion.LookRotation(GetComponent<NavMeshAgent>().velocity);
                        transform.rotation = Quaternion.Lerp(transform.rotation, toRotation, Random.Range(AttackAngularSpeedMin, AttackAngularSpeedMax) * Time.deltaTime);
                    }

                    
                }

                else
                {
                    //Debug.Log("Agent Destination " + navMeshAgent.destination);
                    //Debug.Log("In Effective Range Movement Logic");
                    //Debug.Log("UpdateRotation Setting " + navMeshAgent.updateRotation);

                    navMeshAgent.destination = groundHitPoint;
                    navMeshAgent.speed = Random.Range(AttackMovementSpeedMin, AttackMovementSpeedMax);
                    navMeshAgent.angularSpeed = Random.Range(AttackAngularSpeedMin, AttackAngularSpeedMax);

                    if (!Mathf.Approximately(navMeshAgent.velocity.sqrMagnitude, 0f))
                    {
                        Quaternion toRotation = Quaternion.LookRotation(GetComponent<NavMeshAgent>().velocity);
                        transform.rotation = Quaternion.Lerp(transform.rotation, toRotation, Random.Range(AttackAngularSpeedMin, AttackAngularSpeedMax) * Time.deltaTime);
                    } else
                    {
                        //MANUALLY CONTROL WHILE IN RANGE


                        Vector3 direction = (player.transform.position - actualmesh.transform.position);
                        Quaternion lookRotation = Quaternion.LookRotation(direction);
                        
                        Quaternion targetRot = Quaternion.Lerp(actualmesh.transform.rotation, lookRotation, Time.deltaTime * effectRangeLookSpeed);

                        targetRot.x = 0;
                        targetRot.z = 0;

                        transform.rotation = targetRot;

                        Debug.Log("Remaing Distance " + navMeshAgent.remainingDistance);

                        if (navMeshAgent.remainingDistance <= MinDistanceTobackUp)
                        {
                            Debug.Log("Backing Up");
                            GetComponent<Rigidbody>().AddForce(-direction * backUpSpeed);
                        }
                    }
                }
               
                CanAttack = true;
            }
        }


        
    }

    public float MinDistanceTobackUp;
    public GameObject actualmesh;
    public float effectRangeLookSpeed;
    public float backUpSpeed;



    // ATTACK
    [Header("Attack State")]
    public bool CanAttack;
    public float LookForPlayerMovementSpeedMax;
    public float LookForPlayerMovementSpeedMin;
    public float LookForPlayerAngularSpeedMax;
    public float LookForPlayerAngularSpeedMin;

    public float AttackMovementSpeedMax;
    public float AttackMovementSpeedMin;
    public float AttackAngularSpeedMax;
    public float AttackAngularSpeedMin;

    public bool inEffectiveRange;
    public void RunAttackState()
    {
        FindPlayer();
        //Debug.Log("Coroutine is Null -- " + AttackCo == null);
        if (!StartedAttackSequence) AttackCo = StartCoroutine(Attack());


    }

    public void EffectiveRangeFinder()
    {
        if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
        {
            //Debug.Log("In Effective Range");
            navMeshAgent.updateRotation = false;
            //navMeshAgent.updatePosition = false;
            inEffectiveRange = true;
            //navMeshAgent.enabled = false;
        }
        else
        {
            navMeshAgent.updateRotation = true;
            //navMeshAgent.updatePosition = true;
            inEffectiveRange = false;
            //navMeshAgent.enabled = true;
        }
    }


    [Header("Attack Values")]
    public GameObject AttackPoint;
    public List<EnemyAttackLibrary.AttackType> attackTypesAvailable = new List<EnemyAttackLibrary.AttackType>();
    public float attackCastTime;
    public float minTimeBetweenAttacks;
    public float maxTimeBetweenAttacks;
    public bool StartedAttackSequence = false;
    Coroutine AttackCo;

    public IEnumerator Attack()
    {
        
        StartedAttackSequence = true;

        yield return new WaitForSeconds(Random.Range(1f, 2f));

        while (current_state == EnemyState.ATTACK)
        {

            // Get Random Attack

            EnemyAttackLibrary.AttackType randomAttack = attackTypesAvailable[Random.Range(0, attackTypesAvailable.Count)];

            // Attack
            //Debug.Log("Performing Attack ++ " + randomAttack);

            EnemyAttackLibrary.instance.UseAttack(randomAttack, AttackPoint.transform, GlobalDataReference.instance.player);

            // Time to complete attack, this helps with longer/stronger attacks not being spammed too frequently
            //yield return new WaitForSeconds(attackCastTime);

            // Wait To Attack
            yield return new WaitForSeconds(Random.Range(minTimeBetweenAttacks, maxTimeBetweenAttacks));
        }

        StartedAttackSequence = false;
    }

    [Header("Enemy Vision")]
    public float enemyVisionRadius;
    public float enemyVisionDistance;
    public bool CanSeePlayer;
    public void FindPlayer()
    {
        Debug.DrawRay(innerAnimationLevel.transform.position, innerAnimationLevel.transform.forward * enemyVisionDistance, Color.black);
        if(Physics.SphereCast(innerAnimationLevel.transform.position, enemyVisionRadius, innerAnimationLevel.transform.forward, out var hit, enemyVisionDistance))
        {
            //Debug.Log("Success Hit Info " + hit.transform.gameObject.tag);
            if (hit.transform.gameObject.CompareTag("Player"))
            {
                CanSeePlayer = true;
                //if (AttackCo == null) AttackCo = StartCoroutine(Attack());
            }

            else
            {
                //Debug.Log("Failed To Hit Info ");
                CanSeePlayer = false;
                //if (AttackCo != null) StopCoroutine(AttackCo);
            }

        }
        
    }



    

}
