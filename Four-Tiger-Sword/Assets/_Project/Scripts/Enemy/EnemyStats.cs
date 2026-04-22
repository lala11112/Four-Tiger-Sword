using UnityEngine;

public class EnemyStats : MonoBehaviour
{
    public string DisPlayName { get; private set; }
    public DamageType DamageType { get; private set; }
    

    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int BaseDef { get; private set; }
    public int BaseAtk { get; private set; }
    public float MoveSpeed { get; private set; }
    public int DropExp { get; private set; }

    //공격 범위 public float AttackRange { get; private set; }



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //여기서 So랑 연결하는 코드 작성하기
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
