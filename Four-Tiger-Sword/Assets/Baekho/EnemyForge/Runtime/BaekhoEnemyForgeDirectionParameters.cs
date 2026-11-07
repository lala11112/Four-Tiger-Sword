using UnityEngine;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Copies the Enemy's movement channels after its Update without changing its movement or combat logic.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class BaekhoEnemyForgeDirectionParameters : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] string parameterX = "DirX";
        [SerializeField] string parameterY = "DirY";
        static readonly int SourceX = Animator.StringToHash("DirX");
        static readonly int SourceY = Animator.StringToHash("DirY");
        RuntimeAnimatorController cachedController;
        bool cached, hasSourceX, hasSourceY, hasTargetX, hasTargetY;
        int targetX, targetY;

        public void Configure(Animator target, string x, string y)
        {
            animator = target; parameterX = x; parameterY = y; cached = false;
        }

        void Update() => Synchronize();

        public void Synchronize()
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            if (!cached || cachedController != animator.runtimeAnimatorController)
            {
                cached = true; cachedController = animator.runtimeAnimatorController;
                targetX = Animator.StringToHash(parameterX ?? ""); targetY = Animator.StringToHash(parameterY ?? "");
                hasSourceX = hasSourceY = hasTargetX = hasTargetY = false;
                foreach (var parameter in animator.parameters)
                {
                    if (parameter.type != AnimatorControllerParameterType.Float) continue;
                    if (parameter.nameHash == SourceX) hasSourceX = true;
                    if (parameter.nameHash == SourceY) hasSourceY = true;
                    if (parameter.nameHash == targetX) hasTargetX = true;
                    if (parameter.nameHash == targetY) hasTargetY = true;
                }
            }
            float x = hasSourceX ? animator.GetFloat(SourceX) : 0f, y = hasSourceY ? animator.GetFloat(SourceY) : 0f;
            if (hasTargetX && targetX != SourceX && targetX != SourceY) animator.SetFloat(targetX, x);
            if (hasTargetY && targetY != SourceX && targetY != SourceY) animator.SetFloat(targetY, y);
        }
    }
}
