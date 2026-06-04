using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 적 머리 위의 월드 스페이스 HP바를 제어합니다.
/// Enemy 프리팹 자식 Canvas 오브젝트에 붙이고, Enemy.Start()에서 Setup을 호출하세요.
/// </summary>
public class EnemyHpBar : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private Image _hpFill;

    [Header("설정")]
    [Tooltip("마지막 피격 후 HP바가 사라지기까지 걸리는 시간 (초)")]
    [SerializeField] private float _hideDelay = 3f;
    [Tooltip("HP바가 줄어드는 부드러움 속도")]
    [SerializeField] private float _lerpSpeed = 8f;

    private Enemy _enemy;
    private float _targetFill;
    private Coroutine _hideCoroutine;

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_hpFill == null) return;

        _hpFill.fillAmount = Mathf.Lerp(_hpFill.fillAmount, _targetFill, Time.deltaTime * _lerpSpeed);
    }

    public void Setup(Enemy enemy)
    {
        Debug.Log("Setup: " + enemy.name);
        _enemy = enemy;
        _targetFill = 1f;
        _hpFill.fillAmount = 1f;

        _enemy.OnDamaged += OnDamaged;
        _enemy.OnDied += OnDied;
    }

    private void OnDamaged(float damage, ElementType damageType, bool isCritical)
    {
        _targetFill = _enemy.EnemyStat.CurrentHp / _enemy.EnemyStat.MaxHp;
        Debug.Log("OnDamaged: " + _targetFill);

        Show();
        RestartHideTimer();
    }

    private void OnDied()
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        gameObject.SetActive(false);
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }

    private void RestartHideTimer()
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(_hideDelay);
        gameObject.SetActive(false);
        _hideCoroutine = null;
    }

    private void OnDestroy()
    {
        if (_enemy == null) return;

        _enemy.OnDamaged -= OnDamaged;
        _enemy.OnDied -= OnDied;
    }
}
