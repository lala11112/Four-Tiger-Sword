using System.Collections;
using UnityEngine;

public class WeaponActionExecutor : MonoBehaviour
{
    //WeaponActionData data;
    /*
    public void ExecuteAttack(WeaponActionData data)
    {
        if(data == null) return;

        Vector3 center = transform.position + transform.TransformDirection(data.hitbox);

        Collider[] hits = Physics.OverlapBox(transform.position + transform.forward, data.hitbox, transform.rotation, LayerMask.GetMask("Enemy"));

        foreach(Collider hit in hits)
        {
            hit.GetComponent<IDamageable>()?.TakeDamage(data.damage);
        }
    }
    */
    

    // ──────────────────────────────────────────────
    // Serialized Fields
    // ──────────────────────────────────────────────

/*
    [Header("Weapon Action Data (콤보 순서대로 할당)")]
    [SerializeField] private WeaponActionData[] weaponActionDataList;

    [Header("히트박스 설정")]
    [Tooltip("히트박스 중심 오프셋 (플레이어 앞 방향 기준)")]
    [SerializeField] private Vector3 hitboxOffset = new Vector3(0f, 0f, 1f);
    [SerializeField] private LayerMask hitLayer;

    [Header("콤보 설정")]
    [Tooltip("마지막 공격 후 이 시간(초)이 지나면 콤보가 초기화됩니다")]
    [SerializeField] private float comboResetTime = 1.5f;

    // ──────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────

    private PlayerInput playerInput;
    private int         currentCombo  = 0;
    private bool        isAttacking   = false;
    private float       lastAttackTime;

    // ──────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
    }

    private void Update()
    {
        if (playerInput == null || weaponActionDataList == null || weaponActionDataList.Length == 0)
            return;

        // 콤보 타이머 초기화
        if (currentCombo > 0 && !isAttacking && Time.time - lastAttackTime > comboResetTime)
            currentCombo = 0;

        if (playerInput.AttackPressed)
            OnAttack();
    }

    // ──────────────────────────────────────────────
    // 공격 진입점
    // ──────────────────────────────────────────────

    /// <summary>
    /// 마우스 왼쪽 클릭 시 호출됩니다. 콤보 인덱스에 맞는 WeaponActionData로 공격을 실행합니다.
    /// </summary>
    private void OnAttack()
    {
        if (isAttacking) return;

        WeaponActionData data = weaponActionDataList[currentCombo % weaponActionDataList.Length];
        StartCoroutine(ExecuteAttack(data));
    }

    // ──────────────────────────────────────────────
    // 공격 실행
    // ──────────────────────────────────────────────

    private IEnumerator ExecuteAttack(WeaponActionData data)
    {
        isAttacking   = true;
        lastAttackTime = Time.time;

        // data.frame을 60fps 기준 초로 환산하여 히트 타이밍 대기
        float hitDelay = data.frame / 60f;
        yield return new WaitForSeconds(hitDelay);

        ApplyHitbox(data);

        currentCombo++;
        if (currentCombo >= weaponActionDataList.Length)
            currentCombo = 0;

        isAttacking   = false;
        lastAttackTime = Time.time;
    }

    // ──────────────────────────────────────────────
    // 히트박스 판정
    // ──────────────────────────────────────────────

    private void ApplyHitbox(WeaponActionData data)
    {
        Vector3 center = transform.position + transform.TransformDirection(hitboxOffset);

        Collider[] hits = Physics.OverlapBox(
            center,
            data.hitbox / 2f,
            transform.rotation,
            hitLayer
        );

        foreach (Collider hit in hits)
        {
            hit.GetComponent<IDamageable>()?.TakeDamage(data.damage);

            // IDamageable 인터페이스가 있다면 여기서 hit.GetComponent<IDamageable>()?.TakeDamage(data.damage) 형태로 호출
        }
    }

    // ──────────────────────────────────────────────
    // Gizmos (히트박스 시각화)
    // ──────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (weaponActionDataList == null || weaponActionDataList.Length == 0) return;

        WeaponActionData data = weaponActionDataList[currentCombo % weaponActionDataList.Length];
        if (data == null) return;

        Gizmos.color  = new Color(1f, 0f, 0f, 0.4f);
        Vector3 center = transform.position + transform.TransformDirection(hitboxOffset);
        Gizmos.matrix  = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, data.hitbox);
    }
    */
}
