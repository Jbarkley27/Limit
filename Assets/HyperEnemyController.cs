using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Infohazard.HyperNav;

public class HyperEnemyController : MonoBehaviour
{

    [SerializeField] private NavAgent _agent;
    [SerializeField] private Transform _destination;
    [SerializeField] private float _agentSpeed;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        _agent.Destination = _destination.position;

        Vector3 vel = _agent.DesiredVelocity;

        Debug.Log("Desired Velocity - " + _agent.DesiredVelocity);

        transform.position += vel * _agentSpeed * Time.deltaTime;

        if (vel.sqrMagnitude > 0.01)
        {
            transform.rotation = Quaternion.LookRotation(vel, Vector3.up);
        }
    }
}
