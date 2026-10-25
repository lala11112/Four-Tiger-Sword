using System;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Prepared local geometry and sockets; also used by replaceable corner rooms.</summary>
    [DisallowMultipleComponent]
    public sealed class BaekhoMapModule : MonoBehaviour
    {
        public Bounds localBounds;
        public BaekhoMapConnection[] connections = Array.Empty<BaekhoMapConnection>();
        public GameObject[] groundObjects = Array.Empty<GameObject>();
        public bool isCornerRoom;
        [Min(0), Tooltip("Monster encounter budget for each generated instance of this map.")]
        public int encounterBudget = 20;

        public BaekhoSocketData GetSocket(int index)
        {
            var socket = connections[index];
            return new BaekhoSocketData
            {
                id = socket.id,
                position = transform.InverseTransformPoint(socket.transform.position),
                forward = transform.InverseTransformDirection(socket.transform.forward).normalized,
                width = socket.width,
                height = socket.height
            };
        }

        private void OnDrawGizmosSelected()
        {
            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = isCornerRoom ? Color.cyan : Color.yellow;
            Gizmos.DrawWireCube(localBounds.center, localBounds.size);
            Gizmos.matrix = previous;
        }
    }

    public struct BaekhoSocketData
    {
        public string id;
        public Vector3 position;
        public Vector3 forward;
        public float width;
        public float height;
    }
}
