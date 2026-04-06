using UnityEngine;

public class Enemy : MonoBehaviour, IDamageable
{
    [SerializeField] int hp = 100;

    public int Hp { get { return hp; } set { hp = value; } }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void TakeDamage(int damage)
    {
        hp -= damage;
        Debug.Log($"{name} took {damage} damage. HP: {hp}");
    }
}
