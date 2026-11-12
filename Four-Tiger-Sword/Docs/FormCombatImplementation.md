# Form combat implementation

Source: 플레이어 무기 폼(Form) 스킬 상세 명세 및 모션, 5 pages.

## 조건부 교체 스킬 (F)

숫자키/Q는 공유 2초 쿨타임을 사용하는 폼 교체만 수행합니다. 자동 공격과 숫자키 패링 전환은 제거했으며 토 폼의 방어/반격은 유지합니다.

- 화: 다른 폼의 일반 콤보 마지막 단계 또는 피니셔가 실제 적중하면 5초 동안 준비됩니다. F 사용 시 공격력 +30%를 20초 부여합니다.
- 수: 대시 무적 구간에 실제 적 공격을 피하면 5초 동안 준비됩니다. F 사용 시 공격속도 +25%를 20초 부여하고, 교체 공격에 맞은 적에게 받는 피해 +15%를 적중 시점부터 20초 부여합니다. 목 취약과 독립적으로 곱하며 보스 취약 면역을 따릅니다.
- F는 준비된 비현재 폼 중 가장 최근의 사용 가능한 대상을 선택합니다. 준비는 폼별 1회이며 재충족 시 5초로 갱신됩니다. E와 일반 교체 쿨타임을 사용하지 않습니다.
- 플레이어 버프는 다른 폼에서도 유지되고 동일 효과는 중첩 대신 시간을 갱신합니다. 게임 시간 기준이며 지도/상점 일시정지 중에는 멈춥니다. 사망/비활성화는 정리하고, 스테이지 이동은 준비만 초기화합니다.
- F 입력은 0.15초 버퍼를 사용합니다. 대기/이동, 완료된 회피, 일반 공격의 행동 취소 가능 후딜과 완료된 피니셔에서 실행합니다. 이전 상태를 종료한 뒤 폼을 교체하고 새 공격을 시작합니다.
- 상점 상호작용도 F를 사용하므로 상점 열기를 전투 입력 처리보다 먼저 실행합니다. 상점이 열리면 같은 입력으로 교체 스킬이 발동하지 않고 준비 기회는 보존됩니다.

각 폼 SO의 **연계 스킬 (Chain Skill)**에서 조건, 준비 유지 시간, 버프 시간을 조정합니다. **공격 목록 (Actions) > 교체 스킬 (Switch Attack)**에 전용 `WeaponActionData` 목록을 지정합니다. 화/수 슬롯은 의도적으로 비어 있고 E 데이터를 대신 사용하지 않습니다. 비어 있는 상태에서도 준비 HUD는 표시하지만 F 대상에는 포함되지 않습니다. 목/금/토는 공통 설정만 제공하며 기본 비활성입니다.

HUD는 기존 오행 표시 아래에서 준비된 화/수의 남은 시간을 표시하고 실행 가능한 F 대상을 강조합니다. `FormManager.GetChainReadyRemaining(element)`와 `ReadyChainSkill`이 상태의 원본입니다. HUD 재연결은 `BoundaryZeroHudSetup.ApplyChainSkills()`로 수행합니다.

회귀 진입점: Edit Mode의 `FormChainRegression.Run()`, `FormChainEffectsRegression.Run()`, `ChainSkillHudRegression.Run()`, `BaekhoItemCombatRegression.Run()`; Play Mode의 `BaekhoItemFlowRegression.RunPlayMode()`, `BaekhoItemCombatRegression.RunPlayMode()`. 검사에서는 임시 전용 공격을 사용하며 실제 화/수 슬롯에 모션을 생성하거나 복사하지 않습니다.

## Scope

This update fills missing mechanics and empty attack data. Existing Fire/Wood basic attack timings, damage and thrust curves, Fire skill's authored three-hit sequence, and Water's existing first combo step and dash initial damage are retained. They are not a full rebalance to the PDF's numerical tables.

- Fire: per-target six-hit stack explosion (2.5 ATK, configurable `FireExplosionDelay`, default 0.15 seconds realtime). Stacks reset immediately; independent pending explosions are cancelled on target death/disable. Delayed ground explosion remains 3.5 ATK, one second after impact.
- Water: existing WaterFlowGauge now drives the 100-point, five-second attack/movement-speed buff. Skill cooldown 6 seconds, delayed five hits of 0.5 ATK. Two missing combo inputs added. Ultimate ten hits of 1.2 ATK, invulnerability and renderer restoration, collision-aware attempt to move behind target.
- Wood: existing five-stack bind duration corrected to 1.5 seconds; missing 1.5 ATK skill and poison field (0.5 ATK/second); 4 ATK ultimate, airborne and healing field.
- Iron: missing three-hit basic attack (1/1.2/1.8 ATK), 3 ATK skill, five-hit 2 ATK laser. Independent 15%/5-second basic and 30%/10-second skill debuffs, refreshed rather than stacked.
- Earth: missing shield bash/slash (0.8/1.2 ATK), frontal guard window, 3.5 ATK counter, 2.5 ATK shockwave/shield, five-second full absorption and 4 ATK + 80% absorbed-damage release.
- Ultimate cooldowns: Fire 30, Water 25, Wood 35, Iron 30, Earth 40 seconds, begin on activation and continue while unequipped.

## Inspector and provisional values

New tuning fields live on each WeaponActionDataSO in Assets/_Project/Scripts/FormActions.

The PDF does not specify field radius, poison/healing duration, healing amount, shield amount, movement-buff amount, many recovery times, or defense formula. Current provisional settings:

- AreaRadius 4 m; FieldDuration 5 seconds.
- HealMaxHpPerSecond 0.05 (5% of max HP each second); only PlayerController allies exist in this project.
- ShieldAmount 300 HP; ShieldDuration 5 seconds; reapplication refreshes, does not stack.
- Water move/run speed multiplier 1.3; charge 12 per hit (retained from existing code).
- Defense mitigation: damage * 100 / (100 + max(0, effective DEF)). This activates previously unused enemy DEF and changes combat balance. Shred subtracts a percentage of base DEF, leaving defense buffs intact. Iron ultimate bypasses DEF, elemental multipliers and critical RNG.
- Frames convert at 60 FPS. New animation names are empty and future animation calls are commented TODOs. Existing valid animation bindings stay enabled.
- Earth prose mentions 2nd/3rd slashes but its damage table only specifies bash + slash; the two-hit numerical table is used.

## Lifecycle

Damage/healing fields and delayed hits finish after form changes, but stop when the caster dies or the effect runner is disabled. Fields are anchored in world space at impact. Water renderer/collision overrides and Earth absorption are restored on state exit/owner disable. Collider hits are deduplicated by damage receiver.

Missing motion/VFX assets are not generated. The water ultimate has functional disappearance/hits/reappearance, but no new cinematic slash animation; requested new animations remain comments.

## 전투 자원과 쿨타임

공용 SP와 폼 유지 SP를 제거했습니다. E는 액션 데이터와 폼별 쿨타임을 확인하며 쿨타임은 동작 종료 시 시작합니다. 대시·백스텝과 숫자키/Q 전환은 각자의 쿨타임을 적용합니다. R은 기존 궁극기 게이지와 쿨타임을 사용합니다.

공격 스냅샷과 지연 피해의 고정 추가 피해, 공격속도 하한 0.1, 토 돌진과 수 궁극기의 고정 시간은 각각 기존 전투 규칙을 따릅니다. 방 전투 상태는 담당 던전이 추적하며 빠른 이동과 각인 보상에 사용합니다.

검증 진입점은 Edit Mode의 `CombatActionStatRegression.Run()`과 `RoomCombatEncounterRegression.Run()`입니다. 실제 Game Scene의 방 입장과 클리어는 Play Mode의 `Tools/DungeonEncounterPlayModeRegression.cs`에서 확인합니다.

## Elemental finisher — first playable implementation

Based on [finisher design v0.3](https://github.com/lala11112/Four-Tiger-Sword/blob/docs/elemental-finisher/docs/design/elemental-finisher/docs/finisher-design.md), with the approved first-phase changes below.

- LMB retains each form's normal 1/2/3 combo. As soon as the second attack's final hit window ends, a fresh RMB press opens the wheel, even before the ordinary LMB combo transition time; holding RMB before the final hit ends does not open it automatically. One selection is allowed per combo, including attacks that miss.
- Selection slows the world, player and remaining combo time to 5%, rather than pausing. The existing second attack duration remains the deadline; there is no extra selection timer. `PlayerController > Finisher Selection > Finisher Time Scale` tunes the default `0.05` value.
- Release over another form equips it and starts its third attack directly. Center/current form starts the current normal third attack without switching or rewarding a switch. The transformed path leaves the existing switch cooldown untouched and never queues an E attack or a fourth combo hit. Existing number/Q switching, parry and Water heavy attack remain separate.
- The runtime uGUI wheel starts at the center, with Fire at the top then Earth, Gold, Water and Wood clockwise. Logical radii are 250/90 at the 1920×1080 reference size. Mouse delta moves a virtual cursor while player-facing and camera mouse input are locked. Cancellation and release consume remaining combat input until buttons are neutral.
- Timeout, Esc, hit/state exit, death, focus loss, shop entry, scene transition and disable cancel the session. The wheel removes only its own world-time lease; shop pause and hitstop compose through `CombatHitStopService`. Realtime Fire explosion delays follow the selection multiplier while continuing to ignore ordinary hitstop.
- `WeaponActionDataSO.Actions.FinisherStep` is optional and editable in the custom Actions Inspector. Empty slots reuse the existing third attack. A populated slot applies only when changing form. The common damage multiplier is 1.0; the proposed 1.15 multiplier and all five dedicated elemental effects are deferred.
- Earth now has a third attack: an independent copy of Earth stage two's damage/hitbox, with `OutSlash` and Fire stage two's duration/hit timing. Earth stages one and two are unchanged. This supersedes the earlier two-hit Earth scope above.

### 선택판 디자인 수정 / 프리팹 교체

선택판은 이제 코드로 계층을 생성하지 않고 아래 프리팹을 인스턴스화한다. 전투 상태, 입력, 시간 배율, 폼 전환과 선택 판정은 기존 코드를 그대로 사용한다.

- 기본 프리팹: `Assets/_Project/Scripts/UI/Finisher/Prefabs/FinisherWheel.prefab`
- 교체 설정: `Assets/_Project/Scripts/UI/Finisher/Resources/FinisherWheelResources.asset`의 **Prefab** 필드
- 프리팹을 더블클릭하여 Prefab Mode에서 수정한다. `Content`의 Image는 화면 배경, `Title/Instruction/Help`는 안내 문구, `Wheel` 아래 각 `Sector…`의 TMP는 폼 이름과 역할 문구, `VirtualCursor`는 선택 커서다. 각 섹터에 일반 uGUI Image를 추가해 아이콘을 넣을 수 있다. 장식과 아이콘은 Raycast Target을 끈다.
- `Wheel > FinisherWheelGraphic`의 Inspector에서 속성별 색, 배경·중앙색, 테두리와 강조 스타일을 수정한다. 글자색·폰트·크기·배치는 TMP/RectTransform에서 수정하며 실행 중에도 유지된다. 선택 중 역할 글자색과 굵게 표시는 루트 `FinisherWheelView > Selection Presentation`에서 설정한다. 역할 문구와 선택 설명의 접미사도 이곳에서 바꾼다.
- 새 디자인은 기본 프리팹을 복제하거나 Prefab Variant를 만들고 설정 에셋의 **Prefab**에 지정한다. 루트 `FinisherWheelView`의 Canvas/Content/Virtual Cursor 참조와 화·토·금·수·목 5개 Sector Binding을 유지한다. 이름 대신 아이콘만 쓰려면 Element Label을 비우고 Display Name을 설정한다. 필요하면 기본 Wheel Graphic을 제거하고 각 Sector Binding의 Selected Visual에 직접 만든 강조 오브젝트를 지정할 수 있다. 강조 오브젝트는 해당 섹터의 별도 자식으로 둔다.
- 방사형 선택의 중심, 순서와 판정 크기(중앙 90 / 바깥 250)는 유지한다. 문구·아이콘·장식 배치를 편집할 수 있지만 메뉴 전체의 위치·회전·배율이나 선택 영역 자체를 바꾸는 것은 입력 좌표도 함께 조정해야 하는 별도 작업이다.
- 변경한 프리팹으로 확인할 때는 Play Mode를 새로 시작한다. 플레이어는 처음 연 선택판 인스턴스를 재사용한다. `Ensure UI Resources` 메뉴는 기본 프리팹이 없을 때만 생성하며 기존 디자인이나 커스텀 프리팹 지정을 덮어쓰지 않는다.

### Regression entry points

The input action, Korean wheel font resources and Earth data are already prepared. The idempotent Editor setup entry points are `FinisherInputSetup.Apply()`, `FinisherWheelSetup.EnsureResources()` and `FinisherDataSetup.Apply()`.

Editor regressions: `FinisherFormRegression.Run()`, `FinisherWheelSetup.RunSelectorRegression()`, `FinisherSelectionRegression.Run()` (including the authored input map and opening before normal combo transition), `CombatWorldScaleRegression.Run()` (39 checks), and the existing actor-clock/shop regressions. `BaekhoItemFlowRegression.RunPlayMode()` passed 156 checks for existing switching, parry and item flows. Live validation also exercised normal 1/2/3 and direct-third entry for all five authored forms, Fire-to-Earth release without SP/E side effects, visible wheel layout, advancing frames at `timeScale=0.05`, and timeout restoring normal speed.

Legacy `FormParryRegression`, `WaterCombatRegression` and `WaterHeavyFeelRegression` contain stale cooldown, stat-initialization or asset-count assumptions and are not a green suite. Current flow/item regressions and an isolated Water hold/tap/heavy-chain compatibility check were used alongside the new finisher tests. No claim is made that the entire legacy suite passes.
