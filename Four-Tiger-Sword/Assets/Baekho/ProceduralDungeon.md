# 런타임 맵 생성

`Assets/_Game/ProceduralDungeon/DungeonDemo.unity`를 열고 Play하면 맵을 생성합니다.
게임 씬에 적용할 때는 `DungeonGenerator.prefab`을 배치합니다. 플레이어 배치, NavMesh,
적 스폰은 이 생성기에 포함하지 않습니다. 게임 시작과 연동하려면 `Completed` 이벤트를 사용합니다.

## 설정

`DungeonSettings.asset`에서 맵 목록과 개수, 시드, 간격, 통로 폭을 지정합니다.
같은 맵을 여러 번 사용할 수 있으며 코너 방은 일반 맵 개수에 포함하지 않습니다.
기본값은 6개, 통로 목표 폭 2m, 최소 문 폭 1m입니다. 두 문 중 좁은 폭에 통로를 맞춥니다.
Randomize Seed를 끄면 Seed 값을 재사용합니다. 실제 생성 시드는 Console에도 출력됩니다.

생성기는 출입구의 바닥 높이가 같아지도록 맵을 위아래로 이동합니다.
맵의 내부 고저차는 유지합니다. 다른 맵과 통로는 높이가 달라도 XZ에서 겹칠 수 없습니다.
4개 이상일 때 갈림길이 있는 트리를 만들며, 모든 직각 회전에는 코너 방을 배치합니다.

## 시작방 / 끝방 / 보스방

`DungeonSettings.asset`의 Start Room과 End Room에 전용 맵 프리팹을 지정합니다.
데모는 `StartRoom_FlankingArena`를 시작방, `EndRoom_Flanking_Pit_Arena`를 끝방으로 사용합니다.
현재는 기존 맵 외형을 사용한 전용 복사본이며, 일반 맵 목록을 유지하면서 각 역할의 아트를 따로 교체할 수 있습니다.
두 슬롯이 비어 있으면 일반 목록에서 선택하며, 시작방은 생성 기준 위치에 놓습니다.
끝방은 다른 방을 더 연결하지 않는 종점입니다. 역할에 지정한 프리팹은 일반 방으로 중복 추첨하지 않습니다.

보스방이 준비되면 Boss Room에 프리팹을 넣습니다. 순서는 **시작방 → 일반/분기 구역 → 보스방 → 끝방**입니다.
보스방은 입구와 출구로 사용할 수 있는 문이 최소 두 개 필요합니다. 끝방은 보스방에만 연결되므로
보스방을 우회해서 끝방에 도달할 수 없습니다. 보스방이 비어 있으면 일반 구역에서 끝방으로 연결합니다.
Map Count는 시작방·끝방·보스방을 모두 포함하며, 코너 방은 제외합니다.
보스방을 사용하려면 최소 3개 방이 필요하고, 갈림길까지 만들려면 최소 5개가 필요합니다.

생성된 Hierarchy에는 `Start_`, `End_`, `Boss_` 접두사가 붙습니다.
각 인스턴스의 `BaekhoGeneratedRoom`에 역할과 인스턴스 ID가 있으며,
완료 이벤트의 `result.startRoom`, `result.endRoom`, `result.bossRoom`으로 직접 접근할 수 있습니다.
이 단계에서는 보스 전투나 클리어 조건을 실행하지 않습니다. 이후 보스 전투 시스템이 Boss 역할 방에서
적을 생성하고, 클리어 시 보스방에서 끝방으로 가는 연결을 열도록 연동하면 됩니다.

## 최종 아트로 교체

### 통로 바닥 / 벽 / 출입구 봉쇄

1. `GreyboxCorridorStyle.asset`의 Floor, Wall, Cap에 새 프리팹을 지정합니다.
2. 해당 스타일 에셋을 선택하고 `Assets > Baekho > Measure Corridor Style Pieces`를 실행합니다.
   로컬 메시 크기와 피벗 오프셋을 측정합니다. 프리팹을 수정한 뒤에도 다시 실행합니다.
3. 각 프리팹은 로컬 X=폭, Y=높이, Z=길이로 제작합니다. 루트 피벗은 중앙이 아니어도 됩니다.
4. Placement=Stretch는 길이에 맞춰 늘립니다. Repeat는 Repeat Length마다 반복하고 마지막 조각을 남은 길이에 맞춥니다.

프리팹의 메시 외곽 크기가 구조물 크기입니다. 바닥은 윗면이 출입구 바닥에 맞춰집니다.
충돌은 생성기가 만드는 BoxCollider로 처리하고 아트 프리팹의 Collider는 인스턴스에서 비활성화합니다.
통로 아트에는 Rigidbody를 넣지 않습니다. 중간이 빈 아치나 복잡한 충돌 형상이 필요한 부품은
이 사각형 통로용 스타일 대신 맵/코너 방 모듈로 제작합니다.

일반 맵의 미사용 문은 통로용 얇은 Cap 대신 **주변 맵 벽의 실제 높이·두께·위치·외형**으로 채웁니다.
준비 도구가 문 옆 벽과 문 위/양옆 보조 벽을 조사해 연결점의 Closed Wall 메타데이터에 저장합니다.
완전히 막을 때만 관련 보조 벽과 해당 벽 칸에 포함된 출입구 바닥/천장 타일을 숨기고, 같은 벽 한 칸으로 교체합니다. 사용 중인 출입구는 유지합니다.
문 아래 절벽/받침이 끊긴 경우에는 주변 받침의 별도 높이·깊이·재질로 그 부분도 복원합니다.
최종 아트에서 자동 측정이 맞지 않으면 Closed Wall의 Manual Override를 켜고 템플릿과 범위를 지정합니다.
이 정보가 없는 사용자 지정 모듈만 스타일의 Cap으로 봉쇄합니다.

### 코너 방

기본 코너 방은 `Modules/TEMP_CornerRoom_6x6.prefab`입니다. 최종 아트를 만들 때는 다음 규약을 따릅니다.

- 루트 스케일은 (1,1,1), X/Z 기울기는 0입니다.
- `BaekhoMapConnection` 2개를 서로 직각으로 향하도록 배치합니다. Forward는 방 바깥쪽,
  위치는 출입구 바닥 중앙이며 두 출입구 바닥 높이는 같아야 합니다.
- 위치와 출입구 폭은 기본 0.5m 격자에 맞춥니다. 문은 실제로 열려 있어야 합니다.
- 임시로 문을 가리는 오브젝트가 있으면 해당 연결점의 Placeholders에 지정합니다.
- 바닥은 `floor_`, 계단은 `stair_`, 다리는 `bridge_`, 경사로는 `slope_`로 이름을 시작하면
  준비 도구가 Ground 레이어를 적용할 대상으로 수집합니다.
- 프리팹 선택 후 `Assets > Baekho > Prepare Corner Room Copy`를 실행합니다.
  출입구 규약을 검사하고 코너 방으로 표시된 복사본을 만듭니다.
- `DungeonSettings.asset`의 Corner Room을 준비된 프리팹으로 교체합니다.

코너 방의 크기와 출입구 위치는 프리팹 메타데이터에서 읽습니다. 더 큰 방으로 교체하면 다음 생성부터
더 넓은 공간을 확보합니다. 공간이 없으면 다른 배치를 시도하며, 기존 결과의 코너만 즉석 교체하지 않습니다.

### 일반 맵 추가

Baekho 맵 JSON을 Build Prefab으로 임포트하면 생성용 메타데이터도 추가됩니다.
기존 프리팹은 Prepare Dungeon Module Copy를 실행해 준비하고 Settings의 Maps에 추가합니다.
각 문 ID는 맵 안에서 유일해야 합니다. 출입구가 없는 맵은 지원하지 않습니다.
프로젝트의 원본 7개 맵은 변경하지 않으며 데모는 Modules 폴더의 준비된 복사본을 사용합니다.

## 호출과 실패 처리

```csharp
generator.Completed += result => Debug.Log($"Ready: {result.seed}");
generator.Failed += result => Debug.LogWarning(result.error);
StartCoroutine(generator.Generate(12345));
// generator.ClearGenerated();
```

Generate는 코루틴입니다. 설정과 시드가 같으면 같은 배치를 재현합니다.
배치 계산 후 비활성 부모 아래에 결과를 생성하고 성공할 때만 기존 결과와 교체합니다.
실패 시 기존 결과를 유지하고 실패 이벤트와 원인을 제공합니다. 생성 중 중복 요청은 거부합니다.
ClearGenerated는 이 생성기가 만든 결과만 정리합니다. 생성기 루트는 단위 스케일로 사용합니다.
Max Search Nodes는 재시도 전체에 걸친 후보 검사와 경로 탐색의 공통 작업 한도입니다.
한도를 초과하면 해당 시드와 원인을 반환합니다. 맵 개수나 코너 방 크기를 늘려 생성이 실패하면
이 한도와 Maximum Connection Length를 조절할 수 있습니다.

데모를 다시 만들려면 `Tools > Baekho > Dungeon > Create Demo Assets`를 실행합니다.
기존 설정/스타일/데모 씬은 덮어쓰지 않습니다. 준비된 맵 복사본의 메타데이터는 다시 계산합니다.
EditMode 테스트는 Unity Test Runner의 `StudioBaekho.Maps.Tests` 어셈블리에서 실행합니다.
