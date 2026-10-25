using UnityEngine;

namespace Baekho.Generated.a3355a1c8240244a995e4f671b0dc48bb
{
    public sealed class MonsterTripleSphereBurstSO : MonsterSkillData
    {
        [Header("준비 및 연사")]
        [Min(0f)] public float startupTime = 1f;
        [Min(0.01f)] public float shotInterval = 0.15f;
        [Range(1, 32)] public int projectileCount = 3;

        [Header("탄환")]
        [Tooltip("공격 속도 배율과 별개인 행동 시간 기준 m/s")]
        [Min(0.1f)] public float projectileSpeed = 8f;
        [Min(0.01f)] public float projectileRadius = 0.12f;
        [Tooltip("공격 속도 배율과 별개인 행동 시간 기준 최대 수명")]
        [Min(0.05f)] public float projectileLifetime = 3f;
        public Vector3 spawnOffset = new Vector3(0f, 1.5f, 0f);
        [Tooltip("플레이어 루트 위치에서 위쪽으로 더할 조준 높이")]
        [Min(0f)] public float targetHeight = 1.5f;

        [Header("피해")]
        [Tooltip("ATK에 곱하는 피해 배율")]
        [Min(0f)] public float damage = 0.3f;
        [Min(0f)] public float knockbackForce = 0f;

        [Header("선택 외형")]
        [Tooltip("비워두면 Unity 기본 구형 외형의 기본 재질을 사용합니다.")]
        public Material projectileMaterial;

        public MonsterTripleSphereBurstSO()
        {
            animName = "Attack";
            engageRange = 10f;
            executeRange = 8f;
            preferredRange = 6f;
            weight = 100f;
        }

        public override EnemyAction CreateAction(Enemy enemy)
        {
            ValidateRuntime();
            return enemy == null ? null : new EnemyTripleSphereBurst(this, enemy);
        }

        public void ValidateRuntime()
        {
            startupTime = Safe(startupTime, 1f, 0f, 30f);
            shotInterval = Safe(shotInterval, 0.15f, 0.01f, 10f);
            projectileCount = Mathf.Clamp(projectileCount, 1, 32);
            projectileSpeed = Safe(projectileSpeed, 8f, 0.1f, 100f);
            projectileRadius = Safe(projectileRadius, 0.12f, 0.01f, 2f);
            projectileLifetime = Safe(projectileLifetime, 3f, 0.05f, 10f);
            damage = Safe(damage, 0.3f, 0f, 1000f);
            knockbackForce = Safe(knockbackForce, 0f, 0f, 100f);
            targetHeight = Safe(targetHeight, 1.5f, 0f, 10f);
            spawnOffset = new Vector3(
                Safe(spawnOffset.x, 0f, -10f, 10f),
                Safe(spawnOffset.y, 1.5f, -10f, 10f),
                Safe(spawnOffset.z, 0f, -10f, 10f));
            attackSpeed = Safe(attackSpeed, 1f, 0.01f, 10f);
            telegraphDuration = Safe(telegraphDuration, startupTime, 0f, startupTime);
            engageRange = Safe(engageRange, 10f, 0.1f, 1000f);
            executeRange = Safe(executeRange, Mathf.Min(8f, engageRange), 0.1f, engageRange);
            minimumRange = Safe(minimumRange, 0f, 0f, executeRange);
            preferredRange = Safe(preferredRange, 0f, 0f, engageRange);
            weight = Safe(weight, 100f, 0f, 10000f);
            cooldown = Safe(cooldown, 0f, 0f, 300f);
            repeatWeightMultiplier = Safe(repeatWeightMultiplier, 0.5f, 0.1f, 1f);
            staggerLevel = (StaggerResistLevel)Mathf.Clamp((int)staggerLevel,
                (int)StaggerResistLevel.NONE, (int)StaggerResistLevel.SUPER_ARMOR);
            if (string.IsNullOrWhiteSpace(animName)) animName = "Attack";
        }

        private float Safe(float value, float fallback, float minimum, float maximum)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback : Mathf.Clamp(value, minimum, maximum);
        }
    }
}
