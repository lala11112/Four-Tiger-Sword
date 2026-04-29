using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오살신 — 흉측한 해태 (五殺神) 보스.
///
/// Boss를 상속하고 CreatePhases()를 구현합니다.
/// 각 오행 페이즈는 BossPhaseBase를 상속한 내부 클래스로 정의하여
/// 이 파일 밖에서는 구체 페이즈 타입을 노출하지 않습니다.
///
/// [인스펙터 설정]
/// PhaseDatas 리스트에 4개의 BossPhaseData SO를 순서대로 배치합니다.
///   [0] 화(火) — triggerHpPercent : 100
///   [1] 수(水) — triggerHpPercent : 80
///   [2] 목(木) — triggerHpPercent : 60
///   [3] 토(土) — triggerHpPercent : 40
/// </summary>
public class OsalsinBoss : Boss
{
    // ── 토(土) 페이즈 하수인 관리 ─────────────────────────────────────────────

    private readonly List<Enemy> _activeMinions = new();

    /// <summary>현재 살아있는 하수인 수. 토 페이즈 데미지 감소율 계산에 사용됩니다.</summary>
    public int AliveMinionCount => _activeMinions.Count;

    /// <summary>
    /// 토 페이즈에서 소환된 하수인을 등록합니다.
    /// 하수인 사망 시 자동으로 목록에서 제거됩니다.
    /// </summary>
    public void RegisterMinion(Enemy minion)
    {
        if (minion == null || _activeMinions.Contains(minion)) return;

        _activeMinions.Add(minion);
        minion.OnDied += () => _activeMinions.Remove(minion);
    }

    // ── 페이즈 팩토리 ────────────────────────────────────────────────────────

    protected override List<IBossPhase> CreatePhases()
    {
        if (PhaseDatas == null || PhaseDatas.Count < 4)
        {
            Debug.LogError($"[OsalsinBoss] PhaseDatas가 4개 미만입니다. " +
                           $"인스펙터에서 화·수·목·토 순서로 BossPhaseData를 설정하세요.");
            return new List<IBossPhase>();
        }

        return new List<IBossPhase>
        {
            new FirePhase(PhaseDatas[0]),
            new WaterPhase(PhaseDatas[1]),
            new WoodPhase(PhaseDatas[2]),
            new EarthPhase(PhaseDatas[3], this),
        };
    }

    // =========================================================================
    // 내부 페이즈 클래스
    // BossPhaseBase를 상속하여 필요한 메서드만 오버라이드합니다.
    // =========================================================================

    // ── 화(火) 페이즈 [100% ~ 80%] ───────────────────────────────────────────

    private sealed class FirePhase : BossPhaseBase
    {
        public FirePhase(BossPhaseData data) : base(data) { }

        public override void OnEnter(Boss boss)
        {
            base.OnEnter(boss);
            Debug.Log("<color=red>[오살신] 화(火) 페이즈 — 화염의 굴레</color>");
            // TODO: 화염 파티클·BGM 전환
        }

        public override void OnExit(Boss boss)
        {
            // TODO: 화염 이펙트 정리
        }
    }

    // ── 수(水) 페이즈 [80% ~ 60%] ────────────────────────────────────────────

    private sealed class WaterPhase : BossPhaseBase
    {
        public WaterPhase(BossPhaseData data) : base(data) { }

        public override void OnEnter(Boss boss)
        {
            base.OnEnter(boss);
            Debug.Log("<color=cyan>[오살신] 수(水) 페이즈 — 침식의 리듬</color>");
            // TODO: 물 파티클·BGM 전환
        }

        public override void OnExit(Boss boss)
        {
            // TODO: 물 이펙트 정리
        }
    }

    // ── 목(木) 페이즈 [60% ~ 40%] ────────────────────────────────────────────

    private sealed class WoodPhase : BossPhaseBase
    {
        public WoodPhase(BossPhaseData data) : base(data) { }

        public override void OnEnter(Boss boss)
        {
            base.OnEnter(boss);
            Debug.Log("<color=green>[오살신] 목(木) 페이즈 — 생명의 갈증</color>");
            // TODO: 덩굴 파티클·BGM 전환
        }

        public override void OnExit(Boss boss)
        {
            // TODO: 목 이펙트 정리
        }
    }

    // ── 토(土) 페이즈 [40% ~ 20%] ────────────────────────────────────────────

    private sealed class EarthPhase : BossPhaseBase
    {
        private const float ReductionPerMinion = 0.15f;
        private const float MaxReduction       = 0.75f;

        private readonly OsalsinBoss _osalsin;

        public EarthPhase(BossPhaseData data, OsalsinBoss osalsin) : base(data)
        {
            _osalsin = osalsin;
        }

        public override void OnEnter(Boss boss)
        {
            base.OnEnter(boss);
            Debug.Log("<color=yellow>[오살신] 토(土) 페이즈 — 지맥의 갑주</color>");
            // TODO: 토 파티클·BGM 전환
        }

        public override void OnExit(Boss boss)
        {
            // TODO: 토 이펙트 정리
        }

        /// <summary>
        /// 하수인 1마리당 15% 데미지 감소 (최대 75%).
        /// 하수인을 처치하면 감소율이 줄어듭니다.
        /// </summary>
        public override int ModifyIncomingDamage(int rawDamage, Boss boss)
        {
            float reduction = Mathf.Min(_osalsin.AliveMinionCount * ReductionPerMinion, MaxReduction);
            return Mathf.RoundToInt(rawDamage * (1f - reduction));
        }
    }
}
