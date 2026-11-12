# 게임 씬을 재사용하는 챕터·스테이지 진행

`Assets/_Project/Scenes/Game Scene.unity`를 유지하면서 생성된 던전만 교체합니다. `RunManager`가 런 순서·시드·승패를, `StageManager`가 현재 던전·조우 준비·출구를 관리합니다. `DungeonEncounterDirector`와 기존 방별 전투 시스템이 적 생성과 방 클리어를 처리합니다.

## 설정 위치

| 에셋 | 편집할 내용 |
| --- | --- |
| `Assets/_Game/RunProgression/DefaultRun.asset` | 챕터와 스테이지 순서, 각 스테이지의 던전 설정·적 카탈로그·방 수 변화량·보스 프리팹 |
| `Assets/_Game/RunProgression/Settings/NormalStage.asset` | 일반 스테이지의 방 후보, 시작방·출구방, 기본 `Map Count`, 복도 설정 |
| `Assets/_Game/RunProgression/Settings/BossStage.asset` | 보스 스테이지의 동일 설정과 `Boss Room` |
| `Assets/_Game/RunProgression/Rooms/RunStart.prefab` | 시작방과 `PlayerSpawn` |
| `Assets/_Game/RunProgression/Rooms/RunExit.prefab` | 출구방과 `ExitPoint` |
| `Assets/_Game/RunProgression/Rooms/ChapterBossRoom.prefab` | 보스방과 `BossSpawn` |
| `Assets/_Game/RunProgression/Prefabs/ChapterBoss.prefab` | 챕터 마지막 스테이지에 생성할 기존 보스의 복사본 |

기본값은 **2개 챕터 × 3개 스테이지**, 총 6개입니다. 순서는 `1-1 → 1-2 → 1-3(보스) → 2-1 → 2-2 → 2-3(보스) → 런 승리`입니다. 총 개수는 목록에서 계산하므로 별도 숫자를 맞출 필요가 없습니다. 챕터/스테이지 목록을 늘리거나 줄이면 진행 순서도 함께 바뀝니다.

기본 일반 스테이지들은 `NormalStage`, 보스 스테이지들은 `BossStage`를 공유합니다. 특정 스테이지만 다르게 만들려면 설정 에셋을 복제해 그 항목에 연결하세요. `ChapterBoss`는 게임 씬의 **`RealBoss (7)`를 프리팹으로 복사한 기존 콘텐츠**이며, 새로운 보스 AI나 패턴을 추가한 것이 아닙니다. 현재 두 챕터가 같은 보스를 사용하므로 챕터별 보스를 바꾸려면 각 마지막 항목의 `Boss Prefab`을 교체합니다. 원래 씬의 독립 테스트 적들은 보존하되 비활성화했습니다.

씬의 `StageManager` 오브젝트에는 `StageManager`, `RunManager`, `StageResultUI`가 연결되어 있습니다. `RunManager`의 `Definition`, `Player`, `Stage Manager`, `Result UI`를 지정하고, `StageManager`에는 기존 생성기와 조우 디렉터를 연결합니다. 생성기의 `Generate On Start`는 꺼 두어 런 준비 경로에서만 생성합니다.

## 방 수와 보스 규칙

각 스테이지는 던전 설정의 `Map Count`를 기준으로 `Map Count Variation`만큼 범위 안에서 방 수를 뽑습니다. 변화량 `0`은 고정입니다. 시작방·출구방·보스방·상점방도 이 수에 포함하고, 복도와 연결용 코너는 포함하지 않습니다. 런타임 복사본에만 선택한 방 수를 적용하므로 원본 설정 에셋은 바뀌지 않습니다.

상점 설치 후 현재 기본값은 `5 ± 1`입니다. 선택 갈림길의 상점방을 보장하기 위해 일반 스테이지는 **4~6개**, 보스 스테이지는 **5~6개**를 생성합니다. 상점이 없는 설정은 아래의 기존 최소 방 수를 사용합니다. 골드·상품·임시 UI 교체 방법은 [Shop.md](Shop.md)를 참고하세요.

- 일반 스테이지의 기본 방 수는 최소 2개이며 `Boss Room`과 `Boss Prefab`을 비워야 합니다.
- **각 챕터의 마지막 항목이 보스 스테이지**입니다. 기본 방 수 최소 3개, 보스방과 보스 프리팹이 필수입니다. 변화량을 적용한 하한도 필수 방 수 아래로 내려가지 않습니다.
- 시작·출구·보스 앵커는 해당 방 프리팹 안에 있어야 하고 출구 크기는 양수여야 합니다. 보스는 실제 조우 준비 단계에서 활성 프리팹, `Enemy`·`EnemyStat`·활성 NavMeshAgent, 방 내부의 유효한 NavMesh 위치를 검사합니다.
- 보스 스테이지의 출구방은 보스방 뒤에 연결됩니다. 보스방의 출구 방향 문은 미리 잠기며 보스 처치 후 열립니다. 보스를 삭제하거나 비활성화한 것만으로 처치로 인정하지 않습니다.

일반 스테이지는 출구방을 방문하고 해당 방이 클리어되면 허브와 같은 포탈이 2.4초에 걸쳐 자동으로 열립니다. 현재 기본 출구방은 적이 없어 도착 즉시 개방을 시작합니다. 모든 선택 탐색방을 클리어할 필요는 없습니다. 보스 스테이지에는 실제 보스 처치 조건도 적용합니다. 개방 중에는 통로가 막히며, 완전히 열린 포탈을 정면에서 안쪽으로 통과하면 한 번만 다음 스테이지로 이동합니다. 마지막 챕터의 마지막 포탈은 런 승리로 연결됩니다.

`StageManager`의 출구 프리팹은 `Assets/_Game/RunProgression/Prefabs/StageExitPortal.prefab`입니다. `Tools > Studio Baekho > Dungeon > Set Up Stage Exit Portal`은 허브의 문틀·개방 효과·내부 공간으로 프리팹을 만들고 게임 씬과 현재 런 설정이 참조하는 끝 방을 연결합니다. `GateTransit.eventOnlyTraversal`은 씬 로드나 텔레포트 없이 통과 이벤트를 전달하며, `StageExit`와 기존 `RunManager`가 스테이지 진행을 처리합니다. 허브 게이트의 기본 통과 모드는 그대로 유지됩니다.

## 앵커 배치와 전환

방 루트의 `StageMapAnchors`에서 위치를 지정합니다.

- **`playerSpawn`은 플레이어 루트의 정확한 피벗 위치·회전입니다.** 바닥 좌표가 아닙니다. CharacterController 전체가 바닥 위에 놓이도록 프리팹에서 높이를 맞춥니다. 기본 `RunStart/PlayerSpawn`의 로컬 위치는 `(0, 1.05, 0)`입니다. 런타임 바닥 투영이나 높이 자동 보정은 하지 않습니다.
- **`exitPoint`는 포탈의 바닥 피벗 위치**이며 로컬 +Z가 접근 방향입니다. `exitSize`는 빠른 이동과 보상 상자가 포탈 가까이에 배치되지 않도록 하는 제외 범위입니다. 실제 통과는 열린 포탈의 개구부와 진행 방향으로 판정합니다.
- **`bossSpawn`은 보스방 안의 바닥 위치**입니다. 근처의 유효한 방 NavMesh와 충돌 여유를 확인한 후 보스 NavMeshAgent의 기존 `baseOffset`을 적용합니다.

첫 생성과 스테이지 전환 모두 **생성 전에** 플레이어 입력·이동·중력·루트 모션·피해를 정지합니다. 기존 공격, 넉백, 히트스톱, 장판·지연 공격, 이전 월드의 VFX와 대상 참조를 정리하고 이전 적을 정지합니다. 던전과 조우 준비를 마치면 CharacterController를 끈 상태에서 앵커의 정확한 위치·회전으로 이동하고 카메라 워프를 처리한 뒤 조작을 재개합니다. `OnStageStart`는 이 재개와 진행 정보 반영 뒤 발생합니다.

동일 런에서는 현재 HP·필살기 게이지·아이템·폼과 남은 쿨타임을 유지합니다. 로딩 동안 보호막 시간과 폼·아이템 타이머도 멈춥니다. 진행 중이던 스킬 취소는 기존 종료 경로를 사용하므로 아직 시작되지 않은 스킬 쿨타임은 그때 시작됩니다. 생성 전에 플레이어를 바닥으로 끌어내리거나 스탯 초기화를 호출하지 않습니다.

## 낭떠러지 복귀

현재 생성된 던전의 `BaekhoMapAsset`에서 `role`이 `Deadzone`인 활성 트리거를 추락 구역으로 사용합니다. 플레이어 캡슐이 트리거와 겹치거나 한 프레임 사이에 통과하면 추락으로 처리합니다. 트리거가 없는 V3 구덩이나 맵 바깥에서는 플레이어의 발이 생성된 방·코너 모듈의 가장 낮은 경계보다 0.5m 아래로 내려갈 때 감지합니다. 정상적인 아래층 착지는 피해를 주지 않습니다.

기본 연출은 **1초간 수직 낙하 → 0.15초 암전 → 가려진 상태에서 이동 및 최대 HP의 10% 차감 → 0.2초 화면 복구 → 0.25초 동안 빈 복귀 장소 표시 → 1초간 청록색 홀로그램으로 등장 → 조작 재개 후 1초 무적**입니다. 연출과 복귀 후 무적은 실제 경과 시간을 기준으로 합니다. 체력은 보호막·피해 감소·대시 무적과 무관하게 직접 소모하며, 최소 HP 1을 남기고 부활 효과를 소모하지 않습니다. HP가 이미 1보다 낮다면 추락으로 회복시키지 않습니다.

0.2초 이상 접지한 동안 0.1초 간격으로 안전 위치를 기록합니다. 바닥 지지와 캐릭터 충돌 공간, 가장자리·출입문·추락 구역과의 여유를 확인하고 복귀 직전에 다시 검증합니다. 최근 위치를 쓸 수 없으면 같은 방의 `StageMapAnchors.teleportPoint` 및 주변 위치를 사용합니다. 전투 중에는 같은 전투 방 안으로 복귀하며, 비전투 상태에서만 스테이지 시작점을 최종 후보로 사용합니다.

추락 감지부터 홀로그램 등장 완료까지 플레이어의 조작과 일반 피해를 차단하며 월드는 계속 진행합니다. 복귀 시 공격·넉백·낙하 속도·입력 버퍼를 정리하고 카메라 및 방 진입·미니맵 이동 기록을 보정합니다. 적의 HP, 조우 진행, 플레이어의 아이템·자원은 유지합니다. 스테이지 전환과 빠른 이동은 이전 안전 위치와 트리거 판정에 쓰는 이동 기록을 비웁니다. 복귀가 끝나거나 취소되면 화면·입력 잠금과 플레이어의 렌더러 상태를 복원합니다.

`PlayerController`의 `Fall Recovery`에서 HP 차감 비율, 경계 여유 높이, 낙하 대기, 암전·화면 복구, 빈 장소 표시, 무적 시간을 조정합니다. `PlayerFallReveal`에서는 등장 시간(`Duration`), 홀로그램 색(`Hologram Color`), 글리치 강도(`Glitch Amount`)를 조정합니다. `Visual Roots`의 몸과 무기 렌더러에 `Materials`로 연결된 AllIn1 변형 머티리얼을 사용하고, 연출이 끝나면 원래 머티리얼과 렌더러의 속성 블록을 복원합니다.

플레이어와 무기 머티리얼을 바꿨다면 Edit Mode에서 기존 씬 변경을 저장한 뒤 **Tools > Studio Baekho > Player > Prepare Fall Reveal Materials**를 실행합니다. 도구는 `Assets/_Project/Models/Materials/PlayerFallReveal`에 변형 머티리얼을 준비하고 `Game Scene`의 `PlayerFallReveal`에 참조를 저장합니다.

## 실패·재시도와 시드

`RunManager.randomizeSeed`가 켜져 있으면 새 런마다 `RunSeed`를 정하고, 끄면 Inspector의 `seed`를 사용합니다. 챕터·스테이지 인덱스로 `stageSeed`를 결정하고 이 값으로 방 수를 뽑습니다. 첫 생성의 `layoutSeed`는 `stageSeed`와 같습니다.

생성·조우 준비 실패 시 `Error` 상태에서 플레이어와 전투를 정지하고 오류 내용을 표시합니다. 진행 번호는 준비 성공 후에만 확정합니다. **Retry stage**는 같은 대기 스테이지의 같은 방 수를 유지하면서 시도 횟수로 파생한 새 `layoutSeed`로 다시 생성합니다. 플레이 중 조우 실패는 현재 스테이지를 대기 대상으로 설정해 같은 방식으로 재시도합니다. 로그에 런·스테이지·배치 시드와 방 수가 표시됩니다.

사망은 런 패배입니다. 승리·패배 화면의 **New run**은 게임 씬을 다시 로드해 새 플레이어 상태로 시작하며, 메뉴 버튼은 `TitleScene`으로 돌아갑니다. 생성 오류 화면의 재시도와 새 런 시작은 별도 동작입니다. 앱 종료 후 런을 이어 하는 저장 기능은 포함하지 않습니다.

## 검증과 도구

아래 명령은 Unity 프로젝트 루트(`Assets`의 상위 폴더)에서 연결된 Editor에 실행합니다. Play Mode 중 소스 파일을 수정하거나 재컴파일하면 실행 중인 테스트가 중단될 수 있습니다.

```powershell
# Edit Mode: 컴파일 요청과 완료 상태 확인
unity command recompile --caller plugin --skill unity-cli
unity command recompile_status --caller plugin --skill unity-cli

# Edit Mode: 순서·방 수·시드·보스 그래프 및 플레이어 전환 검증
unity command run_script --caller plugin --skill unity-cli --file Tools/RunProgressionRegression.cs --entry RunProgressionRegression.Run
unity command run_script --caller plugin --skill unity-cli --file Tools/PlayerStageTransitionRegression.cs --entry PlayerStageTransitionRegression.Run
unity command eval --caller plugin --skill unity-cli 'Debug.Log(DungeonStageEncounterRegression.Run());'

# Game Scene의 Play Mode: 실제 런 진행 회귀 시작
unity command run_script --caller plugin --skill unity-cli --file Tools/RunProgressionPlayModeRegression.cs --entry RunProgressionPlayModeRegression.Start
unity command eval --caller plugin --skill unity-cli 'Debug.Log(UnityEditor.SessionState.GetString("RunProgression.PlayModeRegression", "No result"));'
```

방별 보스 수명·문 잠금 검사는 Editor 메뉴 `Tools > Studio Baekho > Dungeon > Run Stage Encounter Regression`에서도 실행할 수 있습니다. Play Mode 검사는 비동기로 진행하므로 시작 명령의 성공과 최종 결과를 구분해 확인합니다.

씬 연결과 기본 에셋을 처음 설치하는 도구는 `Tools/RunProgressionSetup.cs`의 `RunProgressionSetup.Install`입니다. Edit Mode에서 `Game Scene`을 연 상태로 실행하며 에셋과 씬을 저장합니다. 이미 존재하는 런 설정을 기본값으로 초기화하는 도구는 아닙니다.

### 실제 실행 결과 · 2026-10-23

- `RunProgressionPlayModeRegression.Start`: **917개 검증 통과**, 실제 6개 스테이지 생성·진행 완료. 미방문 선택방, 빈 출구방 도착, 보스 1마리 확정 생성·문 잠금·실제 사망, 챕터 전환, 생성 실패·재시도, 중복·이전 맵 출구 요청 차단, 플레이어·자원·아이템·성장·모든 폼 쿨타임 유지, 이전 맵·적·NavMesh·투사체 정리와 최종 승리를 확인했습니다.
- `RunProgressionPlayModeRegression.StartReturnToTitle`: **3개 검증 통과**. `TitleScene` 이동, 이전 런 매니저 제거, 시간 배율 복원을 확인했습니다.
- `RunProgressionPlayModeRegression.StartDeathAndRestart`: **19개 검증 통과**. 실제 치명타에 의한 패배, 결과 화면의 전투 정지, 새 플레이어와 첫 챕터부터의 재생성을 확인했습니다.

사망과 첫 챕터부터의 새 런 재시작은 새 Play Mode 세션에서 `RunProgressionPlayModeRegression.StartDeathAndRestart`로 별도 실행할 수 있습니다. 결과 키는 `RunProgression.DeathRestartRegression`이며, 타이틀 이동 검사의 결과 키는 `RunProgression.TitleRegression`입니다.

### 포탈 전환 검증 · 2026-11-12

- `RunProgressionPlayModeRegression.Start`: **1,128개 검증 통과**, 임시 2개 챕터 × 2개 스테이지를 실제 생성·진행했습니다. 도착 전 닫힘, 2.4초 자동 개방, 방향·개구부·모달 판정, 한 번의 빠른 이동으로 통과, 다단계 보스의 최종 처치, 생성 실패·재시도, 상태 유지와 최종 승리를 확인했습니다.
- `ShopPlayModeRegression.Start`: **115개 검증 통과**. 실제 구매·회복·골드 보상, 모달 입력, 재시도 시 재고 유지 및 포탈 이동 후 아이템·골드 유지를 확인했습니다.
- `HubGateRegression.Start`: **51개 검증 통과**. 기존 허브의 개폐·통과·실패 복구 동작을 확인했습니다.
- 끝 방에서 열린 포탈의 공간 렌더링과 빠른 이동 안전지점을 확인했습니다. 위 런·상점 검사는 메모리 계정을 사용하며, 설치된 캠페인 설정과 영구 저장 데이터는 변경하지 않습니다.

### 추락 복귀 연출 검증 · 2026-11-12

- `PlayerFallRecoveryRegression.Run()`: **23개 검증 통과**. 생성 경계, 안전 위치와 전투 방 제한, 등록된 추락 트리거 겹침·통과, 스테이지 범위와 워프 후 판정 초기화를 확인했습니다.
- `DungeonFastTravelRegression.Run()`: **17개 검증 통과**. 공통 착지 검사 변경 뒤 기존 빠른 이동 동작을 확인했습니다.
- `EnemySpawnRevealRegression.Run()`: **통과**. 기존 적 등장 효과의 머티리얼, 격리, 복원, 조우 상태를 확인했습니다.
- `PlayerFallRecoveryRegression.Start()` / `Status()`: 실제 V3·Refresh 방 프리팹으로 **118개 Play Mode 검증 통과**. 추락 판정, 수직 낙하와 화면 전환, 빈 장소 표시, 청록색 등장 및 머티리얼 복원, 조작·무적·HP 처리를 확인했습니다. `Time.timeScale = 0.25`에서도 연출이 실제 시간에 맞춰 진행됐습니다. 암전·화면 복구·빈 장소 표시·등장 중 비활성화와 스테이지 전환을 추가로 검사해 화면·재질·입력 잠금 복원과 중복 차감 방지를 확인했습니다.
- 실제 Game Scene의 카메라 캡처에서 빈 복귀 장소, 청록색 글리치 등장과 정상 재질 복원을 확인했습니다. 본체·머리카락·의상·불 속성 검 및 접지 그림자는 빈 장소 표시 동안 숨겨졌고, HP 1에서도 연출 후 정상 복귀했습니다. 캡처·보고서는 `Temp/FallRevealLive`에 남겼습니다.
- 임시 저장 계정의 실제 전투 방에서 물 속성 쌍검으로 재검증했습니다. HP **2000 → 1800**이 한 번 차감됐고 같은 방으로 복귀했습니다. 적 4마리의 HP·생존 수·웨이브와 전투 상태를 유지했고, 양손 무기의 숨김·홀로그램·원래 재질 복원 및 완료 후 보호 상태를 확인했습니다.
- Unity 컴파일 오류는 없었습니다. 실제 던전 카메라 캡처에서는 기존 다수 광원 설정에 대한 URP 최대 추가 광원 수 오류가 있었고, 적 소환 Edit Mode 회귀 검사는 기존 Animator 비활성 오류를 출력했습니다. 추락 연출 외의 해당 경로는 변경하지 않았습니다. 검증 후 TitleScene의 Edit Mode로 돌아왔습니다.

### 이전 추락 복귀 기본 동작 검증 · 2026-11-12

- `PlayerFallRecoveryRegression.Run()`: **15개 검증 통과**. 회전된 방과 낮은 코너의 경계, 안정 접지, 가장자리·출입문 제외, 전투 방 제한, 장애물이 생긴 위치와 오래된 스테이지를 검사했습니다.
- `DungeonFastTravelRegression.Run()`: **17개 검증 통과**. 공통 착지 검사로 변경한 뒤에도 기존 빠른 이동 동작을 유지합니다.
- `PlayerFallRecoveryRegression.Start()` / `Status()`: **58개 플레이 검증 통과**. 실제 V3·Refresh 구덩이의 중력 낙하, 정상 착지, 암전 중 이동·체력 차감, HP 1 반복 추락, 보호막·부활 유지, 무적 종료, 같은 스테이지 재개와 중단을 확인했습니다.
- 실제 Game Scene에서 임시 저장 계정으로 8개 방을 생성해 연결을 확인했습니다. 비전투 추락은 HP 2000에서 1800으로 감소한 뒤 복귀했고, 전투 중 추락은 같은 방의 적 4마리·적 HP·웨이브를 유지한 채 복귀했습니다. 컴파일과 추락 실행에는 오류가 없었습니다.
- 검증 후 씬을 종료할 때 기존 `EnemyAttackState.Exit()`의 비활성 Animator 오류가 기록됐으며, MCP 화면 캡처에서도 렌더 타깃 크기 오류 후 카메라 캡처로 대체됐습니다. 추락 코드 외의 해당 경로는 수정하지 않았습니다. 에디터는 변경 사항 없이 TitleScene의 Edit Mode로 복원했습니다.
