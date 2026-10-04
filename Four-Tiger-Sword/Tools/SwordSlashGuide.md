# 공격 검기 이펙트

기본 콤보 14타와 물 강공격에 속성별 검기를 연결했습니다.
프리팹: `Assets/_Project/SlashEffects/` (Water/Fire/Wood/Iron/EarthSlash).

각 FormAction 에셋의 Combo Steps 또는 Heavy Attack Step에서 조정합니다.

- Slash VFX: 검기 프리팹. 비우면 끕니다.
- Slash VFX Start Time: -1이면 각 유효 Hit Event 시작에 재생합니다. Hit Events가 비어 있으면 Hit Start Time을 사용합니다. 0 이상이면 해당 공격 시간에 한 번 재생합니다.
- Slash VFX Offset: Hit Box Offset에 더하는 플레이어 로컬 위치. 기본 Y=1m.
- Slash VFX Rotation: 로컬 각도. 콤보는 ±18도, 다단 공격은 매 타격 Z축 기울기를 교대합니다.
- Slash VFX Scale / Lifetime: 크기 / 공격 속도 1 기준 수명. 수명 기본 0.22초.

프리팹의 Sword Slash Visual에서 Tint(HDR 색), Radius, Width, Arc를 조정합니다.
별도 텍스처 없이 URP 가산 블렌딩 셰이더와 런타임 메시로 렌더링합니다.
검기는 생성 시점의 월드 위치에 남고, 빠르게 사라집니다. 검에 부착하는 트레일은 아닙니다.
생성 시 공격 속도로 수명을 보정하고, 히트스톱/일시정지의 Time.timeScale을 따릅니다.
공격 종료·취소·다음 타수 시작 시 이전 검기는 제거됩니다.
공통 스킬/궁극기/착지/반격 처리도 같은 재생 기능을 지원하지만, 이번 기본 에셋 연결은 콤보와 물 강공격만 포함합니다.

검증: Unity Pipeline run_script로 `Tools/SwordSlashRegression.cs`의 `SwordSlashRegression.Run` 실행.
`Tools/SwordSlashSetup.cs`의 `SwordSlashSetup.Preview`는 격리된 Preview Scene에서 렌더링합니다.
실제 애니메이션의 칼날 위치에 맞춘 최종 각도·크기는 플레이 화면에서 조정하세요.
