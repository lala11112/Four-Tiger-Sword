# 런타임 맵 생성

`Assets/_Game/ProceduralDungeon/DungeonDemo.unity`를 열고 Play하면 맵을 생성합니다.
게임 씬에 적용할 때는 `DungeonGenerator.prefab`을 배치합니다. 프리팹의 `DungeonEncounterDirector`가
생성기의 `Completed` 이벤트를 받아 방별 NavMesh 준비, 몬스터 배치와 출입문 전투를 연결합니다.
플레이어 자동 배치와 스테이지 전체 승리 처리는 별도입니다.

## 미니맵과 전체지도

`Game Scene`에는 `Assets/_Project/Prefabs/UI/DungeonMinimap.prefab`이 연결되어 있습니다.
속성 HUD 아래에 300×300 미니맵을 표시하고, 골드는 미니맵 아래에 배치합니다.
`DungeonMinimapView > World Span`으로 가로·세로 표시 범위(기본 100m)를 조절할 수 있습니다.

방은 사각형, 통로는 폭이 있는 선으로 단순화합니다. 생성 루트의 +Z가 위쪽이며 플레이어를 중심으로
지도가 이동합니다. 시작방은 즉시 공개하고 방·직선 통로·코너는 방문한 뒤 표시합니다.
밝은 청록색은 현재 영역, 어두운 청록색은 방문한 영역입니다. 상점과 출구 아이콘은 해당 방을 방문한 뒤
실제 앵커 위치에 표시합니다. 전투 클리어 여부와 무관하며 높낮이·구덩이·장애물 윤곽은 표현하지 않습니다.
방문 기록은 같은 스테이지의 UI 재활성화 동안 유지하고, 새 생성 결과가 적용되면 초기화합니다.
미니맵에는 현재 방에 속한 살아 있는 적만 빨간 점으로 표시합니다. 적의 이동을 실시간으로 반영하고,
사망·비활성화·방 이동 시 갱신하며 통로·코너에서는 숨깁니다. 적 표시는 탐험 기록을 변경하지 않습니다.
`DungeonMinimapView > Enemy markers`의 Sprite·Color·Icon Size로 적 표시 아트를 교체할 수 있습니다.

게임 중 **Tab**으로 전체지도를 열고 다시 Tab 또는 **Esc**로 닫습니다.
전체지도는 `Assets/_Project/Prefabs/UI/DungeonFullMap.prefab`의 별도 캔버스로 표시하며,
현재 던전 전체가 Viewport 안에 들어오도록 축척을 맞춥니다. 공개 범위와 현재 위치·방향,
상점·출구 표시는 미니맵과 같은 방문 기록을 사용하므로 전체지도를 열어도 미방문 방이 공개되지는 않습니다.
지도를 열면 작은 미니맵은 숨겨지고 게임이 일시정지되며 마우스 커서가 표시됩니다.
플레이어·카메라 조작, 상점 열기와 필살기 선택은 지도 사용 중 차단됩니다.
스테이지 전환·오류·런 종료·포커스 상실·지도 UI 비활성화 시 지도를 닫고 소유한 일시정지를 해제합니다.

현재 비전투 상태에서 **방문했고 전투가 완료된 방을 왼쪽 클릭**하면 지정된 안전 지점으로 이동하고 지도를 닫습니다.
현재 방·미방문 방·미완료 방·통로·코너는 이동할 수 없습니다. 상점·출구 아이콘도 해당 방 선택으로 처리합니다.
비용·쿨다운·확인창은 없으며, 이동이 불가능하면 지도 하단에 이유를 표시합니다.
순간이동은 중간 경로를 방문 처리하거나 다른 방의 전투를 시작하지 않습니다.

설치를 다시 연결하려면 `Tools > Studio Baekho > Dungeon > Install Minimap`을 사용합니다.
기존 미니맵과 전체지도 인스턴스·프리팹을 재사용하며 씬 저장은 별도로 수행합니다.
전체지도는 미니맵의 자식이 아닌 독립된 루트 캔버스입니다. 정렬 순서는 미니맵 100, 상점 150,
전체지도 160, 결과 200입니다. 전체지도만 포인터 입력을 받고 배경 클릭을 차단하며 미니맵은 입력을 통과시킵니다.
같은 메뉴의 `Run Minimap Model Regression`, `Run Minimap Integration Regression`,
`Run Full Map Regression`은 임시 씬에서 좌표·방문·UI 수명·해상도 배치와 두 지도 간 공유를 검사합니다.
`Run Minimap Enemy Regression`은 현재 방 필터링·적 이동과 사망·마커 재사용·탐험 기록 분리를 검사합니다.

### 지도 UI 아트와 레이아웃 교체

`DungeonMinimap.prefab`과 `DungeonFullMap.prefab`을 Prefab Mode에서 수정합니다.
`Panel`의 앵커·크기·여백, `Background`와 전체지도의 `Backdrop` 이미지, 제목과 닫기 힌트는
자유롭게 교체할 수 있습니다. `Viewport`는 `RectMask2D`를 유지하고, 그 안의 `MapContent`,
`MarkerLayer`, `PlayerArrow` 역할과 `DungeonMinimapView`의 직렬화된 참조를 유지합니다.
전체지도는 `Mode=EntireDungeon`, 미니맵은 `Mode=FollowPlayer`를 사용합니다.
새 UI에서도 CanvasScaler는 `Scale With Screen Size`, 기준 1920×1080, Match 0.5를 권장합니다.

방·현재 영역·입구의 색, 아이콘 색과 크기, 플레이어·상점·출구 스프라이트는
`DungeonMinimapView`의 Inspector 설정으로 교체합니다. 스프라이트를 비워 두면 기본 도형 아이콘을 사용합니다.
`DungeonMinimapModel`의 좌표·방문 로직과 `DungeonMinimapPresenter`의 스테이지·입력 연결은 유지하므로
아트 교체를 위해 다시 구현할 필요가 없습니다. 전체지도 프리팹에는 View만 두고 Presenter는
기존 미니맵 쪽의 하나를 공유합니다.

완전히 새 프리팹으로 바꾸면 씬의 Presenter에서 `Run`, `View`(미니맵), `Full Map View`(전체지도)를
새 인스턴스에 다시 연결합니다. 각 View의 Visibility·Viewport·Map Content·Marker Layer·Player Arrow도
새 계층에 연결하고, 전체지도 캔버스를 미니맵 CanvasGroup 아래에 넣지 않습니다.
전체지도는 GraphicRaycaster, Viewport의 DungeonMapPointerHandler와 포인터 수신 Image,
Backdrop의 입력 차단, 상태 안내 TMP 참조도 유지합니다. 방 도형과 아이콘 자체는 raycastTarget을 끕니다.
설치 메뉴는 기존 프리팹의 아트와 배치를 덮어쓰지 않으며, 별도 경로의 새 프리팹은 이 명시적 씬 참조로 사용합니다.

### 순간이동 도착 지점

각 main-room 프리팹의 `StageMapAnchors.teleportPoint`는 바닥이 아닌 **플레이어 루트의 위치와 방향**입니다.
바닥 위에 실제 CharacterController 캡슐 전체가 놓이도록 설정하고 문짝·상점 카운터·출구 자동 진행 범위를 피합니다.
시작방·일반 방·상점·출구·보스방은 모두 지원하며, 코너에는 설정하지 않습니다.
제작 도구는 현재 Run Definition에서 사용하는 main-room 프리팹을 중복 없이 검사하고 기존 도착 지점을 보존합니다.
`Tools > Studio Baekho > Dungeon > Install Teleport Anchors`는 누락된 지점만 설치하고,
`Validate Teleport Anchors`는 에셋을 변경하지 않고 기존 지점을 검사합니다.
`Run Fast Travel Landing Regression`, `Run Fast Travel Service Regression`, `Run Map Modal Regression`은
도착 공간·클릭 시점의 재검증·자원 보존·일시정지와 입력 소유권을 검사합니다.
NavMesh가 없는 빈 방도 실제 바닥과 캡슐 여유 공간으로 검증합니다. 클릭 직전에도 동적 장애물을 재검사합니다.
지점이 없거나 안전하지 않은 기존 콘텐츠는 정상 플레이를 유지하며 해당 방으로의 순간이동만 차단됩니다.

## 방별 몬스터와 전투

- 맵 프리팹 루트의 `BaekhoMapModule > Encounter Budget`으로 방 예산을 설정합니다. 기본값은 20이며,
  같은 맵이 여러 번 생성되어도 예산은 같습니다. 0이면 전투를 만들지 않습니다.
- `Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset`에서 프리팹과 양의 정수 비용을 지정합니다.
  초기 비용은 Chang=2, Drone=5, BlackSlayer=4입니다. 프리팹 루트에는 활성 Enemy와 NavMeshAgent가 필요합니다.
- 예산을 넘지 않는 정확한 최적 조합을 고릅니다. 가능하면 두 종류 이상을 포함하고 최저비용 종류들의
  합계 마릿수를 전체의 절반 이하로 제한합니다. 그 안에서 잔액을 최소화하고 동률만 랜덤 선택합니다.
  균형 조합이 하나도 없으면 제한을 완화하고 경고합니다. 비용이 모두 같으면 저비용 비율 제한은 없습니다.
- 각 방의 `BaekhoGameplayMarker.kind == "enemy"`를 배치 기준으로 사용합니다. 한 지점 주변에 여러 마리를
  놓을 수 있으며 기본 반경은 3m입니다. 바닥 높이, NavMesh, 벽과 몬스터 간 간격을 검사합니다.
  같은 던전 시드와 설정은 같은 조합과 배치를 재현합니다. 마커가 없는 방은 빈 방입니다.
- 기존 베이크 데이터는 재사용하고 없는 방만 런타임에 베이크합니다. 현재 카탈로그는 NavMesh Agent Type 0을
  사용합니다. 스폰이나 이동 데이터가 잘못되면 해당 방은 Failed 상태가 되고 문을 잠그지 않습니다.
- 일반 방은 총예산으로 뽑은 적을 웨이브로 나눕니다. `DungeonEncounterDirector`의 웨이브당 목표 코스트(기본 20)와
  최대 마릿수(기본 6)가 기준입니다. 방 생성 로그에서 웨이브별 명단과 코스트를 확인할 수 있습니다.
- 첫 웨이브는 문 밖에서도 보이며 스폰 지점 3m 안에서 걷다가 1~3초씩 쉽니다. 방에 들어오기 전에는
  플레이어를 감지하거나 피해를 받지 않습니다. 문 안쪽 안전 경계를 지나면 문이 잠기고 현재 위치에서 교전을 시작합니다.
- 일반 적은 공격 쿨타임 동안 스킬의 선호 거리(미설정 시 실행 거리)를 유지합니다. 해당 거리 근처에서는 플레이어를 바라보고 멈춥니다.
- 일반 방의 후속 웨이브는 앞 웨이브 전멸 1.5초 뒤 안전한 적 마커 주변에 등장합니다. 증원 대기 중에도 전투와 문 잠금이 유지됩니다.
  마지막 웨이브까지 끝나면 문을 열며, 방 처치 보상은 실제 처치한 경우에 지급합니다. 보스방은 보스 고유 페이즈를 사용합니다.
  클리어한 방은 재입장해도 재스폰하거나 잠그지 않습니다. 별도 수동 배치 적은 집계하지 않습니다.
- `BaekhoMapConnection > Runtime Door`에 기존 LabDoor 오브젝트를 연결하면 위치를 유지해 재사용합니다.
  비어 있으면 생성기 프리팹의 Door Prefab으로 설치합니다. Closed Wall 측정값이 있으면 벽 두께의 중심에
  맞추고 출입구의 바닥 높이는 유지합니다. 미사용 출구의 Runtime Door는 벽 봉쇄 시 숨깁니다.
  `관측고지_전투실`의 `south_3`는 기존 수동 배치 문을 연결해 두었습니다.
- 방별 `DungeonRoomEncounter`의 State, AliveCount, SpentBudget, RemainingBudget, Error와 Console 로그로
  진행 상황을 확인할 수 있습니다. 성공한 재생성은 전투를 초기화하고 실패한 맵 생성은 기존 상태를 유지합니다.

검증: `StudioBaekho.Maps.Tests`에 예산 최적성/혼합/시드 테스트가 포함됩니다.
프로젝트 `Tools/`의 `DungeonEncounterGateRegression.Run`, `DungeonEncounterPlacementRegression.Run`,
`DungeonEncounterCombatRegression.Run`은 Edit Mode 회귀 검사입니다. 실제 프레임과 NavMeshAgent 활성화,
마지막 처치, 재생성 검증은 Play Mode에서 `DungeonEncounterPlayModeRegression.Start`를 실행합니다.
마지막 검사는 현재 실행 중인 던전에서 적을 처치하고 맵을 재생성·제거하므로 테스트용 Play 세션에서 사용합니다.
결과는 Editor SessionState의 `DungeonEncounter.PlayModeRegression` 키에 PASS 또는 FAIL로 기록됩니다.

## 설정

`DungeonSettings.asset`에서 맵 목록과 개수, 시드, 간격, 통로 폭을 지정합니다.
같은 맵을 여러 번 사용할 수 있으며 코너 방은 일반 맵 개수에 포함하지 않습니다.
새 설정의 기본값은 6개, 통로 폭 2m, 최소 문 폭 1m입니다. 현재 `DungeonSettings.asset`은 10개, 통로 폭 4m입니다.
직선과 코너 연결의 통로는 모두 설정 폭을 유지합니다. 좁은 문이 있어도 통로 전체를 줄이지 않으며,
문과 통로의 폭 차이는 입구 양옆 벽으로 마감합니다. 방과 코너 프리팹에 있는 실제 문 폭은 유지합니다.
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
4. 현재 스타일은 `Placement=TilePreserveSize`를 사용합니다. 프리팹의 로컬 메시 크기에 원본 루트 스케일을 곱한
   실제 크기(현재 가로 2m)로 반복합니다. 바닥은 X/Z, 측벽은 Z, 입구 마감은 X 방향을 유지하고,
   높이와 두께만 요청 크기에 맞춥니다. 마지막 조각은 압축하지 않고 메시를 잘라 맞춥니다.
5. `Stretch`와 `Repeat`는 기존 호환 모드입니다. Stretch는 전체 크기에 맞춰 늘리고,
   Repeat는 Repeat Length마다 Z 방향으로 반복하며 마지막 조각은 남은 길이에 맞게 압축합니다.

TilePreserveSize는 읽기 가능한 정적 MeshFilter/MeshRenderer를 사용합니다. 모델을 교체할 때는 Read/Write를 켭니다.
잘린 원래 면의 UV·노멀·재질 구분은 유지하며, 새 단면은 첫 번째 재질로 막습니다.
구멍이 있는 절단 단면이나 열린/비다양체 메시처럼 안전하게 닫을 수 없는 형상은 대상 이름과 함께 생성 오류로 보고합니다.
절단 메시만 복사하여 생성하고 맵 제거 시 정리합니다. 일반 맵의 Closed Wall 복원은 기존 방식을 유지합니다.

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

현재 연결 통로와 6×6 코너 방에는 높이 4m, 두께 0.2m의 천장과 LabLights 벽등·천장등이 있습니다.
코너 방의 북쪽·동쪽 출입구는 폭 4m를 유지하며, 중앙 천장등과 서쪽·남쪽 벽등을 사용합니다.
`GreyboxCorridorStyle.asset`에서 Ceiling을 비우면 Floor를 천장으로 재사용합니다.
Wall Light Prefab 또는 Ceiling Light Prefab을 비우면 해당 통로 조명을 생략합니다.
통로 조명은 기본 6m 간격으로 균등 배치하며 벽등은 높이 2.2m에서 좌우 번갈아 설치합니다.
짧은 통로에서 기구가 출입구나 실내 범위를 벗어나면 그 조명은 배치하지 않습니다.
조명 인스턴스는 원래 크기·색·밝기를 유지하고 Collider와 실시간 그림자를 끕니다.
기존 코너 방에 스타일 변경을 다시 적용하려면 `Tools > Baekho > Dungeon > Update Existing 6x6 Corner Room`을 실행합니다.
이 도구는 기존 바닥·벽의 수평 배치와 프리팹 GUID를 유지하며 천장·조명을 중복 없이 갱신합니다.
코너 프리팹에 저장할 천장 타일은 6m에 잘림 없이 들어맞는 크기를 사용합니다(현재 2m).

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
