using UnityEngine;
using System.Collections.Generic;

public class Flow : MonoBehaviour
{
    UnityEngine.AI.NavMeshAgent _navMeshAgent;
    public Transform _target;
    public Dictionary<string, Transform> _targetDict;
    void Awake()
    {
        _navMeshAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        _navMeshAgent.SetDestination(_target.position);
    }
}
