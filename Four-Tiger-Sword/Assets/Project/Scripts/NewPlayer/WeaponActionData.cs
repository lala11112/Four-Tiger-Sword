using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WeaponActionData
{
    [Header("Animation & Timing")]
    [Tooltip("애니메이터에 재생할 트리거 또는 State 이름")]
    public string AnimationName; 
    
    [Tooltip("이 공격 애니메이션의 총 지속 시간")]
    public float Duration = 0.5f;
    
    [Tooltip("다음 콤보 입력을 받아들이기 시작하는 시간 (이 시간 이후에 공격을 누르면 콤보 발동)")]
    public float ComboTransitionTime = 0.3f;

    [Header("Combat & Physics")]
    [Tooltip("이 타수의 데미지")]
    public int Damage = 10;
    
    [Tooltip("공격 시 앞으로 살짝 전진하는 힘 (타격감 상승)")]
    public float ForwardThrust = 2f;

    [Header("Effects (선택사항)")]
    [Tooltip("검기 또는 타격 이펙트 프리팹")]
    public GameObject SlashVFX;
    
    [Tooltip("휘두르는 사운드")]
    public AudioClip SwingSound;
}