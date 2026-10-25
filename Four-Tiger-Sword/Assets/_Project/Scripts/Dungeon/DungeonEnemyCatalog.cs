using System;
using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(menuName = "Four Tiger Sword/Dungeon Enemy Catalog")]
public sealed class DungeonEnemyCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public GameObject prefab;
        [Min(1)] public int cost = 1;
    }

    public Entry[] enemies = Array.Empty<Entry>();

    public int[] GetValidatedCosts()
    {
        if (enemies == null || enemies.Length == 0)
            throw new InvalidOperationException("Assign at least one monster to the dungeon enemy catalog.");
        var costs = new int[enemies.Length];
        var seen = new System.Collections.Generic.HashSet<GameObject>();
        for (int i = 0; i < enemies.Length; i++)
        {
            var entry = enemies[i];
            if (entry == null || entry.prefab == null || entry.cost <= 0)
                throw new InvalidOperationException($"Monster entry {i} needs a prefab and positive integer cost.");
            if (entry.prefab.scene.IsValid() || !seen.Add(entry.prefab))
                throw new InvalidOperationException($"Use a distinct prefab asset for monster entry {i}.");
            var enemy = entry.prefab.GetComponent<Enemy>();
            var agent = entry.prefab.GetComponent<NavMeshAgent>();
            if (enemy == null || !enemy.enabled || agent == null || !agent.enabled || !entry.prefab.activeSelf)
                throw new InvalidOperationException($"{entry.prefab.name} needs an active Enemy and NavMeshAgent on its root.");
            costs[i] = entry.cost;
        }
        return costs;
    }
}
