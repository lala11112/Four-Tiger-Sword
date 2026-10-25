# 공격 검기 이펙트

Hovl Studio/Sword slash VFX/Prefabs의 단일 검기를 7개 FormAction 데이터의 35개 공격 항목에 연결했습니다. 현재 Game Scene이 사용하는 WaterFormLab/WaterFormActionSO_V2도 포함합니다. 공급된 프리팹 원본은 수정하지 않습니다.

- 수: 1·2타 Sword Slash 1, 3타 찌르기 이펙트 Prick 3, 돌진/강공격 Sword Slash 2, 착지 Sword Slash 9
- 화: 주황 Sword Slash 10. 3타는 강한 올려베기 모션에 맞춘 수직 검기입니다.
- 목: 잎이 포함된 Sword Slash 3
- 금: 흰 Sword Slash 13
- 토: Sword Slash 12

수 3단 콤보의 실제 양손 무기 끝점을 120Hz로 샘플링했습니다. 이펙트 시작 시간은 순서대로 0.330/0.445, 0.235/0.365, 0.330/0.805초이며, 수 돌진/강공격은 0.245초입니다. 수 3타는 모션·피해량·타격 판정·이동을 유지하고 이펙트만 Prick 3으로 교체했습니다. Prick 3은 크기 0.5, 재생 배율 2, 수명 0.48초이며, 끝 섬광의 0.05초 지연을 보정했습니다.

화 3타 Fire1에는 같은 검 애니메이션 팩의 straight_sword_main_charged_attack_02_release_full을 연결했습니다. 모션 길이 1.3333334초, 타격 0.1333333~0.2333333초, 검기 시작 0.105초, 트레일 0.0666667~0.3초입니다. 기존 피해량·강인도 피해·넉백은 유지합니다.

플레이어 루트는 캡슐 중심이며 모델이 아래로 1m 이동되어 있습니다. 검기 중심은 플레이어 로컬 (0, 0.05, 0.1)에 두고, HitBoxOffset을 상쇄하는 SlashVFXOffset을 저장합니다. 원본 크기의 0.65배, 파티클 재생 2.6배, 공격 시간 기준 수명 0.42초가 기본값입니다.

FormAction 인스펙터의 타격 피드백 → 검기 이펙트에서 프리팹·위치·회전·크기를 조절합니다. Slash VFX Start Time이 음수면 각 타격 시간에 Time Offset을 더하고, 0 이상이면 공격 시작 기준 절대 시간입니다. 개별 Hit Event의 Override Slash VFX를 켜면 해당 타격의 설정을 사용합니다. 켠 상태에서 프리팹을 비우면 해당 검기만 끕니다.

AttackSlashPlayback은 공격의 _timer를 직접 읽어 원본 파티클을 한 번만 시뮬레이션합니다. 공격 속도 변화·히트스톱·같은 프레임의 중복 호출에서도 별도 시계가 누적되지 않습니다. 낮은 프레임에서는 해당 재생 위치로 따라잡고, 수명을 모두 지나친 검기는 뒤늦게 표시하지 않습니다. 공격 종료·취소·다음 타수 시작 시 이전 검기를 즉시 숨기고 제거합니다. Follow Player는 대시 중 검기가 뒤에 남지 않도록 합니다.

설정 재적용: Tools/ImportedSwordSlashSetup.cs의 ImportedSwordSlashSetup.Run()을 Edit Mode에서 실행합니다. 검증: Tools/ImportedSwordSlashRegression.cs의 ImportedSwordSlashRegression.Run() — 타격별 타이밍, 저프레임, 파티클 재생·정지·배속, 위치·회전·크기 25항목.

현재 BDJ 컨트롤러의 Wood1 상태 및 금·토 일부 모션은 아직 없습니다. 이 항목들은 기존 타격 시간 기준으로 검기를 재생합니다. 해당 모션을 연결한 뒤 시각 타이밍을 추가 보정할 수 있습니다. 수 전용 궁극기 및 목·토의 영역 스킬은 검 휘두르기 경로가 아니므로 이번 연결 대상에 포함하지 않았습니다.
