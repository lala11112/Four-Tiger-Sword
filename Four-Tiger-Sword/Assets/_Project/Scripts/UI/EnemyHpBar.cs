using System.Collections;
using UnityEngine;

/// <summary>
/// 적 머리 위의 월드 스페이스 HP바를 제어합니다.
/// Enemy 프리팹 자식 Canvas 오브젝트에 붙이고, Enemy.Start()에서 Setup을 호출하세요.
/// </summary>
public class EnemyHpBar : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private UnityEngine.UI.Image _hpFill;
    [SerializeField] private UnityEngine.UI.Image _hpTrail;

    [Header("설정")]
    [Tooltip("마지막 피격 후 HP바가 사라지기까지 걸리는 시간 (초)")]
    [SerializeField] private float _hideDelay = 3f;
    [Tooltip("HP바가 줄어드는 부드러움 속도")]
    [SerializeField] private float _lerpSpeed = 8f;
    [Tooltip("피격 후 잔상이 줄어들기 시작할 때까지의 시간 (초)")]
    [SerializeField, Min(0f)] private float _trailDelay = 0.35f;
    [Tooltip("잔상이 초당 줄어드는 최대 체력 비율")]
    [SerializeField, Min(0.01f)] private float _trailSpeed = 0.6f;

    private Enemy _enemy;
    private float _targetFill;
    private float _trailDelayRemaining;
    private Coroutine _hideCoroutine;

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        UpdateFill(Time.deltaTime);
    }

    private void UpdateFill(float deltaTime)
    {
        if (_hpFill == null) return;

        _hpFill.fillAmount = Mathf.Lerp(_hpFill.fillAmount, _targetFill, deltaTime * _lerpSpeed);
        if (_hpTrail == null) return;

        // Consume the hold first, including frames that cross its end.
        float trailDeltaTime = Mathf.Max(0f, deltaTime - _trailDelayRemaining);
        _trailDelayRemaining = Mathf.Max(0f, _trailDelayRemaining - deltaTime);
        _hpTrail.fillAmount = Mathf.Max(_hpFill.fillAmount,
            Mathf.MoveTowards(_hpTrail.fillAmount, _targetFill, _trailSpeed * trailDeltaTime));
    }

    public void Setup(Enemy enemy)
    {
        Debug.Log("Setup: " + enemy.name);
        _enemy = enemy;
        _targetFill = 1f;
        _hpFill.fillAmount = 1f;
        if (_hpTrail != null) _hpTrail.fillAmount = 1f;
        _trailDelayRemaining = 0f;

        _enemy.OnDamaged += OnDamaged;
        _enemy.OnDied += OnDied;
    }

    private void OnDamaged(float damage, ElementType damageType, bool isCritical)
    {
        float nextFill = _enemy.EnemyStat.MaxHp > 0f
            ? Mathf.Clamp01(_enemy.EnemyStat.CurrentHp / _enemy.EnemyStat.MaxHp) : 0f;
        if (nextFill < _targetFill)
        {
            // Repeated hits keep the current trail and restart its hold without refilling it.
            if (_hpTrail != null && _hpFill != null)
                _hpTrail.fillAmount = Mathf.Max(_hpTrail.fillAmount, _hpFill.fillAmount);
            _trailDelayRemaining = _trailDelay;
        }
        _targetFill = nextFill;
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
