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

일반 스테이지는 출구방 조건을 충족하면 출구를 이용할 수 있습니다. 모든 선택 탐색방을 클리어해야 하는 구조는 아닙니다. 보스 스테이지는 실제 보스 처치와 출구방 조건을 모두 충족해야 합니다. 사용 가능한 청록색 출구 영역에 들어가면 한 번만 다음 스테이지로 이동합니다. 마지막 챕터의 마지막 출구는 런 승리로 연결됩니다.

## 앵커 배치와 전환

방 루트의 `StageMapAnchors`에서 위치를 지정합니다.

- **`playerSpawn`은 플레이어 루트의 정확한 피벗 위치·회전입니다.** 바닥 좌표가 아닙니다. CharacterController 전체가 바닥 위에 놓이도록 프리팹에서 높이를 맞춥니다. 기본 `RunStart/PlayerSpawn`의 로컬 위치는 `(0, 1.05, 0)`입니다. 런타임 바닥 투영이나 높이 자동 보정은 하지 않습니다.
- **`exitPoint`는 출구 영역 중심의 바닥 위치**입니다. `exitSize`는 이 지점부터 위로 생성되는 영역의 크기입니다.
- **`bossSpawn`은 보스방 안의 바닥 위치**입니다. 근처의 유효한 방 NavMesh와 충돌 여유를 확인한 후 보스 NavMeshAgent의 기존 `baseOffset`을 적용합니다.

첫 생성과 스테이지 전환 모두 **생성 전에** 플레이어 입력·이동·중력·루트 모션·피해를 정지합니다. 기존 공격, 넉백, 히트스톱, 장판·지연 공격, 이전 월드의 VFX와 대상 참조를 정리하고 이전 적을 정지합니다. 던전과 조우 준비를 마치면 CharacterController를 끈 상태에서 앵커의 정확한 위치·회전으로 이동하고 카메라 워프를 처리한 뒤 조작을 재개합니다. `OnStageStart`는 이 재개와 진행 정보 반영 뒤 발생합니다.

동일 런에서는 현재 HP·SP·필살기 게이지·아이템·폼과 남은 쿨타임을 유지합니다. 로딩 동안 SP 재생, 보호막 시간, 폼/아이템 타이머도 멈춥니다. 진행 중이던 스킬 취소는 기존 종료 경로를 사용하므로 아직 시작되지 않은 스킬 쿨타임은 그때 시작됩니다. 생성 전에 플레이어를 바닥으로 끌어내리거나 스탯 초기화를 호출하지 않습니다.

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
