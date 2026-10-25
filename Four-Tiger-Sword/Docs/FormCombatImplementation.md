# Form combat implementation

Source: 플레이어 무기 폼(Form) 스킬 상세 명세 및 모션, 5 pages.

## Scope

This update fills missing mechanics and empty attack data. Existing Fire/Wood basic attack timings, damage and thrust curves, Fire skill's authored three-hit sequence, and Water's existing first combo step and dash initial damage are retained. They are not a full rebalance to the PDF's numerical tables.

- Fire: per-target six-hit stack explosion (2.5 ATK, configurable `FireExplosionDelay`, default 0.15 seconds realtime). Stacks reset immediately; independent pending explosions are cancelled on target death/disable. Delayed ground explosion remains 3.5 ATK, one second after impact.
- Water: existing WaterFlowGauge now drives the 100-point, five-second attack/movement-speed buff. Skill cost/cooldown 150/6, delayed five hits of 0.5 ATK. Two missing combo inputs added. Ultimate ten hits of 1.2 ATK, invulnerability and renderer restoration, collision-aware attempt to move behind target.
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

## 폼 유지 SP

기존 공용 영력 SP와 별개의 자원이며, HUD는 아직 연결하지 않는다. 각 폼의 액션 에셋에서 `공통 밸런스 (Balance) > 폼 유지 SP`를 조정한다. 기본값은 최대 100, 전투 중 완전 소모 30초, 비활성 상태 완전 회복 20초, 고갈 해제 20%, 공격력·공격속도 배율 각각 0.8이다.

- 활성 폼은 담당 던전의 방 전투가 진행 중일 때만 소모한다. 복도·상점·빈 방·클리어한 방에서는 현재 값을 유지한다. 비활성 폼은 전투 여부와 관계없이 즉시 회복한다.
- SP 0에서 고갈이 유지되며, 비활성 상태에서 20%까지 회복해야 해제된다. 20% 미만에서 다시 활성화하면 페널티가 남는다. 스킬·회피·교체의 공용 SP 비용과 아이템의 SP 효과는 이 자원을 변경하지 않는다.
- 상태는 `BaseForm.CurrentFormSp`, `MaxFormSp`, `IsFormSpExhausted`로 조회하며, `OnFormSpChanged`는 변경된 폼을 전달한다. 일반·패링·강화 3타 전환은 같은 폼 객체를 유지하므로 자원이 초기화되지 않는다.
- 공격력은 해당 폼의 공격력 계산에만 곱한다. 기존 공격 스냅샷과 생성된 지연 피해는 유지하고 고정 추가 피해에는 곱하지 않는다. 공격속도 하한은 0.1이다. 토 돌진과 수 궁극기의 고정 시간에는 고갈 배율만 적용하며, 총 돌진 거리와 궁극기 10타는 유지한다.
- `Time.deltaTime`으로 공격 상태 갱신 전에 처리한다. 사망·비활성화·상점 일시정지·층 이동 중에는 멈추고, 방·층 이동 후에도 값을 유지한다. 새 플레이어의 폼은 완충 상태로 생성한다.

검증 진입점: Edit Mode의 `FormSpRegression.Run()` 및 `FormSpEncounterRegression.Run()`. 실제 Game Scene Play Mode에서는 Pipeline `run_script`로 `Tools/FormSpPlayModeRegression.cs`의 `Start()`를 실행하고 `SessionState` 키 `FormSp.PlayModeRegression`으로 결과를 확인한다.

## Elemental finisher — first playable implementation

Based on [finisher design v0.3](https://github.com/lala11112/Four-Tiger-Sword/blob/docs/elemental-finisher/docs/design/elemental-finisher/docs/finisher-design.md), with the approved first-phase changes below.

- LMB retains each form's normal 1/2/3 combo. As soon as the second attack's final hit window ends, a fresh RMB press opens the wheel, even before the ordinary LMB combo transition time; holding RMB before the final hit ends does not open it automatically. One selection is allowed per combo, including attacks that miss.
- Selection slows the world, player and remaining combo time to 5%, rather than pausing. The existing second attack duration remains the deadline; there is no extra selection timer. `PlayerController > Finisher Selection > Finisher Time Scale` tunes the default `0.05` value.
- Release over another form equips it and starts its third attack directly. Center/current form starts the current normal third attack without switching or rewarding a switch. The transformed path has no SP cost, does not read/reset the existing switch cooldown, and never queues an E attack or a fourth combo hit. Existing number/Q switching, parry and Water heavy attack remain separate.
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
