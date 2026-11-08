# 경계 ZERO 인벤토리·아이템 59종

2026-11-08 전달본의 아이템 59종과 시너지 11종을 실제 보유·전투·상점에 연결합니다. 게임에서 사용하는 카탈로그는 기존 `IT_*` 20종에서 새 `Ixx` 59종으로 교체합니다. 이전 생성 에셋과 백호툴 반입기는 보존합니다. 허브와 저장·불러오기는 이번 변경에 포함하지 않으며, 보유 상태는 현재 플레이의 `PlayerItemRuntime`이 관리합니다.

## 반입과 씬 연결

- 표시용 에셋은 `Assets/BoundaryInventory`, 게임용 생성 카탈로그는 `Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset`입니다. 기존 TMP 공유 리소스를 재사용합니다.
- Edit Mode에서 `Tools > Boundary Zero > Inventory > Import Installed 59 Items` 또는 `BoundaryInventoryImporter.ImportDefault()`를 실행합니다. `ImportPackage(path)`는 전달본 루트, `Data/catalog.ko.json`, 설치된 표시 JSON 경로를 받습니다.
- 생성 카탈로그를 Player의 `PlayerItemRuntime.Catalog`에 연결합니다. 기존 `BaekhoItemSetup.AttachCatalogToPlayer(catalog, player)` 또는 `Attach Selected Catalog To Player` 메뉴도 사용할 수 있습니다. 카탈로그 교체는 이전 보유 목록을 비웁니다.
- Game Scene에서 `BoundaryInventorySetup.Install()` 또는 `Tools > Studio Baekho > BoundaryZERO > Install Inventory In Game Scene`으로 표시 프리팹과 `PlayerInventoryPresenter`를 연결합니다. 이 설치 도구는 씬을 자동 저장하지 않습니다.
- 재반입은 ID별 정의 경로와 Unity GUID를 유지합니다. 전체 입력을 확인한 뒤 반영하며 중간 실패 시 기존 생성 파일을 복원합니다. 편집 중인 생성 에셋과 다른 소유자의 에셋을 덮어쓰지 않습니다.
- 효과는 `BoundaryInventoryContent`의 명시적 typed mapping으로 실행합니다. 런타임에서 효과 문구를 해석하지 않습니다. 지원한 날짜·구성·효과 문구·시너지 수치가 바뀌면 반입기가 거절하므로 매핑과 검증용 문구 스냅샷을 함께 갱신해야 합니다.

실제 게임의 표시 데이터도 게임용 `ItemCatalog`를 사용합니다. `PlayerInventoryPresenter`가 `ItemDisplayText.ToInventoryData(catalog)`로 변환한 내용을 `InventoryView.SetDisplayData`에 전달하므로 상점·인벤토리·실제 효과가 같은 정의를 참조합니다. 제공된 프리팹 디자인과 폰트·아이콘 연결은 유지합니다. 독립 데모처럼 게임용 카탈로그를 연결하지 않은 경우에는 패키지의 표시 JSON을 fallback으로 사용합니다.

Downloads 원본은 수정하지 않습니다. 설치된 카탈로그에는 승인한 I89 조건 변경인 **기본 골드 1~10**을 반영합니다. 생성 SO의 수동 수정은 다음 반입에서 갱신됩니다.

## 조작과 보유 API

플레이 중 **I**로 인벤토리를 열고 닫고, **Esc** 또는 닫기 버튼으로 닫습니다. 처음에는 빈 목록이며 데모의 전체 지급을 사용하지 않습니다. 실제 보유 종류·효과 설명·활성 시너지 단계가 표시됩니다. 같은 ID는 한 종류만 보유하고, 여러 태그를 가진 아이템은 각 시너지에 한 종류씩 기여합니다. 시너지는 2·4·6 중 최고 단계만 적용하며 숙련 7종의 표시는 6/6입니다.

인벤토리가 열린 동안 게임 시간·플레이어 입력·카메라 입력을 멈추고 커서를 표시합니다. 닫으면 기존 전투 동작을 이어갑니다. 상점·지도·전환·피니셔 선택 등 다른 모달 상태에서는 열기를 제한합니다. 게임 코드에서는 `PlayerInventoryPresenter.Open/Close/Toggle`을 사용해 일시정지 상태와 표시를 함께 바꾸세요.

```csharp
PlayerItemRuntime items = player.GetComponent<PlayerItemRuntime>();
bool added = items.TryAddItem("I01");
bool owned = items.Contains("I01");
bool removed = items.TryRemoveItem("I01");
items.OnOwnedItemsChanged += RefreshOwnedItems;
// items.OwnedItems: 실제 보유 정의, items.SynergyStates: 실제 시너지 단계
```

표시 프리팹의 `InventoryView.SetOwnedItems`는 화면 동기화 API입니다. 아이템 지급·제거는 위 보유 API로 처리합니다. 상점·합성·보상 설명은 `ItemDisplayText.Description(item, catalog)`와 공통 시너지 표시 메타데이터를 사용합니다.

## 전투·보상 규칙

- 평타 효과는 `Normal/Heavy/Counter`, 스킬 효과는 `Skill(E)/Ultimate(R)/SwitchAttack`에 적용합니다. 명시적으로 평타·스킬을 함께 대상으로 하는 효과는 `NormalOrSkill` 필터를 사용합니다.
- 스킬의 원래 다단 타격과 장비·시너지가 별도로 발생시킨 추가 타격을 구분합니다. 추가 타격은 평타·스킬 전용 보너스를 받지 않고 적중·처치 효과를 다시 발동하지 않습니다. 공격 종류를 한정하지 않은 속성·대상 조건 효과는 해당 조건에 따라 적용합니다.
- 같은 종류의 아이템 비율 보너스를 합산합니다. 치명 확률·배율의 `%p`는 퍼센트포인트로 더하고 치명 확률은 100%까지입니다. 관통은 양수 유효 방어력에만 적용합니다. HP 조건은 피해 적용 전 값과 경계를 포함해 판정합니다.
- 최대 HP 증가는 현재 HP를 회복하지 않고, 감소 시 현재 HP를 새 최대치로 제한합니다. 보호막은 출처별 잔량과 시간을 따로 관리하며 먼저 부여된 출처부터 흡수합니다. 피격 후 발동 효과는 해당 타격을 소급해 줄이지 않습니다. 아이템 제거 시 그 출처의 효과·보호막을 제거하며 재획득으로 쿨다운과 전투 최초 발동 기록을 초기화하지 않습니다.
- 전투 경계는 던전 인카운터 상태를 사용합니다. 적의 `EnemyRank`로 일반·정예·보스를 구분하고 Boss는 보스로 판정합니다.
- 일반 적의 기본 골드는 생성 시 **5~15 정수**로 정하며 재현 가능한 스테이지·방·웨이브·스폰 정보로 고정합니다. 현재 보스의 설정된 보상 그룹은 **150G**를 유지합니다. 보상 그룹이 없는 보스의 기존 fallback 정책은 변경하지 않습니다.
- 처치 골드는 확인된 사망에서 한 번 지급합니다. I89는 기본 골드 1~10일 때 +1G입니다. S19는 기본 골드에만 비율을 곱해 소수를 이월하고 정수 부분을 지급합니다. 추가 골드는 다시 계산에 넣지 않으며 소수 잔여는 새 플레이에서 초기화합니다.

## 등급과 구매 가격

효과의 적용 범위·조건·지속성과 다중 시너지 기여를 기준으로 정한 초기 배정입니다.

| 등급 · 가격 | 수량 | ID |
| --- | ---: | --- |
| 1등급 · 20G | 13 | I13, I41, I47, I51, I66, I68, I71, I75, I77, I89, I91, I93, I94 |
| 2등급 · 40G | 26 | I01, I04, I06, I07, I08, I15, I17, I18, I19, I20, I21, I22, I31, I32, I42, I44, I46, I48, I57, I60, I63, I67, I69, I73, I78, I90 |
| 3등급 · 80G | 14 | I02, I05, I16, I30, I33, I43, I50, I61, I62, I64, I65, I72, I92, I100 |
| 4등급 · 160G | 6 | I09, I14, I34, I56, I81, I95 |

## 검증 실행점

Unity에서 다음 정적 진입점을 호출합니다. 아래는 실행 목록이며 통과 결과를 의미하지 않습니다.

| 진입점 | 실행 상태와 확인 범위 |
| --- | --- |
| `BoundaryInventoryImporterRegression.Run()` | Edit Mode. 59종·11종, 가격·표시 메타데이터, GUID 유지, 잘못된 입력 거절, 실패 롤백 |
| `BoundaryItemRuntimeRegression.Run()` | Edit Mode. 전체 개별 효과·시너지 단계·조건·보호막·골드·전투 수명 |
| `BoundaryCombatIntegrationRegression.Run()` | Edit Mode. 실제 피해·사망·공격 분류·골드·거래 연결 |
| `InventoryIntegrationRegression.Run()` | Edit Mode. 제공 프리팹의 보유 표시·집계·열기/닫기 |
| `InventoryModalRegression.Run()` | Edit Mode. 인벤토리 모달의 입력·시간·기존 동작 복원 |
| `InventoryIntegrationRegression.RunPlayMode()` | Game Scene 준비 완료 후 Play Mode. 현재 플레이어의 실제 보유와 화면·일시정지 연결 |

시각 확인에서는 빈 목록, 여러 줄의 보유 아이콘과 스크롤, 아이템·시너지 상세, I/Esc 조작, 전투 중 열기와 닫기, 상점 거래 후 갱신을 확인합니다.

## 확인한 결과

아래 결과는 [반입 검증 기록](../BoundaryInventoryIntegration/Verification/import-results.txt), [Edit Mode 검증 기록](../BoundaryInventoryIntegration/Verification/edit-results.txt), [실제 플레이 검증 기록](../BoundaryInventoryIntegration/Verification/play-results.txt)에 대응합니다.

| 검증 | 결과 |
| --- | --- |
| 새 카탈로그 반입 | 318개 확인 통과. 59종·11종, GUID 유지, 잘못된 입력 거절, 실패 롤백 |
| 전체 개별 효과·시너지 런타임 | 542개 확인 통과 |
| 실제 전투·사망·거래 연결 | 351개 확인 통과 |
| 인벤토리 표시 | 105개 확인 통과 |
| 인벤토리 모달 소유·복원 | 38개 확인 통과 |
| 실제 플레이의 빈 보유·59종 보유 | 각각 32개 확인 통과 |
| 액션 타이밍 | 161개 확인 통과 |

실제 I/Tab/Esc 입력으로 인벤토리 열기·닫기, 시간 재개, 기존 Tab 지도와 모달 중복 방지를 확인했습니다. 실제 `StageShopService`에서 세 번 구매하고 I13을 10G에 판매하며 I21+I44 합성을 수행한 뒤 보유 화면이 즉시 갱신됐습니다. 스테이지 재생성으로 다음 챕터에 진입해도 I01/I89와 최대 HP +5%가 유지됐고 시간·입력이 복원됐습니다. 결과 전환과 인벤토리 중 사망은 모달을 닫고 재열기를 막았으며, `RestartRun`은 보유와 소수 골드를 초기화했습니다.

최종 재실행에서 기존 아이템·상점·보스·지도·액션을 포함한 Edit Mode 검증 진입점 14개가 모두 통과했습니다. Water 궁극기의 고정 타격 진행을 실제 액터 히트스톱·월드 일시정지 기준으로 멈추도록 정리하고 검증 fixture의 Animator 활성화·초기화 순서를 보완했으며, `ActionTimingRegression`의 161개 확인도 통과했습니다.

최종 컴파일 환경은 Unity 6000.3.10f1입니다. 최종 실행 뒤 콘솔 오류는 0건이며, 닫기 버튼과 실제 생성 적의 일반 보상 5~15G·보스 보상 150G도 확인했습니다. 편집 모드에서 테스트 보유 아이템 0개, 인벤토리 View·Presenter·EventSystem 각 1개로 마쳤습니다. 기존 재질·폰트 28개는 작업 전 백업과 동일하며, 저장된 씬의 기존 내용 위에 인벤토리와 새 카탈로그 연결을 추가했습니다.

화면 기록: [59종 보유](../BoundaryInventoryIntegration/Verification/inventory-full-1.png), [아이템 상세](../BoundaryInventoryIntegration/Verification/inventory-tooltip-final.png), [새 카탈로그 상점](../BoundaryInventoryIntegration/Verification/shop-new-catalog.png), [I89 스크롤 위치](../BoundaryInventoryIntegration/Verification/inventory-scrolled-i89.png), [빈 인벤토리](../BoundaryInventoryIntegration/Verification/inventory-empty.png).
