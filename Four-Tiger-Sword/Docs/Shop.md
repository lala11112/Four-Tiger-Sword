# 골드와 스테이지 상점

`Game Scene`의 `StageManager` 오브젝트에 `StageShopService`가 연결되어 있습니다. 일반·보스 스테이지에 각각 하나의 안전한 상점방을 생성합니다. 상점방은 선택 갈림길의 끝에 있으며 방문하지 않아도 다음 스테이지로 진행할 수 있습니다.

판매대 2.5m 안에서 **F**를 누르면 상점이 열리고, **Esc** 또는 닫기 버튼으로 닫습니다. 상점 화면에서는 게임 시간·조작·카메라 입력을 정지합니다. 우측 상단의 골드는 한 런 동안 유지되며 새 런에서 초기화됩니다. 적의 실제 사망에만 한 번 지급하고, 맵 정리나 적 삭제에는 지급하지 않습니다.

## 설정

`Assets/_Game/Shop/EconomySettings.asset`에서 다음 값을 조절합니다.

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| Starting Gold | 0 | 새 플레이어의 시작 골드 |
| Ordinary Kill Gold | 10 | 일반 적 사망 보상 |
| Boss Kill Gold | 50 | 해당 스테이지 보스 사망 보상 |
| Base Item Count | 3 | 판매 아이템 수 기준; 매 스테이지 N−1~N+1개 |
| Healing Price | 20 | 즉시 회복 가격 |
| Healing Fraction | 0.3 | 현재 최대 HP의 30% 회복 |
| Interaction Distance | 2.5 | 판매대 상호작용 거리 |

N은 1 이상의 정수입니다. N=1이면 0~2개가 나올 수 있습니다. 현재 보유하지 않은 유효한 카탈로그 아이템 중 중복 없이 뽑으며 후보가 부족하면 가능한 만큼만 판매합니다. 회복은 아이템 수에 포함하지 않습니다. 아이템 가격·이름·설명·아이콘은 기존 ItemCatalog/ItemDefinition을 사용합니다. 생성된 아이템 에셋을 직접 수정하지 않고 기존 백호툴 반입 흐름을 유지하세요.

각 아이템과 회복은 상점당 한 번씩 구매할 수 있습니다. 보유 중·잔액 부족·품절이면 구매할 수 없으며, 만피에서는 회복 비용과 재고를 소비하지 않습니다. 재방문과 같은 스테이지의 생성 오류 재시도는 상품 구성과 구매 여부를 유지합니다. 다음 스테이지에서는 새 상점을 구성합니다.

## 방과 프리팹

- `Assets/_Game/RunProgression/Rooms/RunShop.prefab`: 상점방. `StageMapAnchors.shopPoint`는 바닥 위 판매대 기준 위치입니다. 방 내부, 출입구와 겹치지 않는 위치를 사용합니다.
- `Assets/_Game/Shop/ShopCounter.prefab`: 임시 판매대 외형. `StageShopService.counterPrefab`으로 교체합니다.
- 던전 설정의 `Shop Room`: 전용 상점방. 설정하지 않은 기존 던전에는 상점방을 강제 추가하지 않습니다.

Map Count는 상점방을 포함한 총 방 수입니다. 설치 도구는 기존 구성에 상점 슬롯을 한 번만 추가합니다. 현재 기본값은 5±1이며, 상점이 있는 일반 스테이지 최소 4개·보스 스테이지 최소 5개를 보장합니다. Shop 역할의 방은 전투 예산이나 적 마커가 있어도 적을 생성하지 않습니다.

## 정식 UI로 교체

구매 규칙은 `ShopStock`, 골드는 `PlayerWallet`, 씬 연결과 화면 전달은 `StageShopService`가 담당합니다. UI는 지갑·아이템·재고를 수정하지 않습니다.

1. 기존 uGUI를 꾸밀 때는 `Assets/_Game/Shop/UI/TemporaryShop.prefab`, `ShopOffer.prefab`, `GoldHUD.prefab`을 편집합니다. Inspector의 표시 컴포넌트 참조를 유지합니다.
2. 화면 구조나 UI 프레임워크를 바꿀 때는 `IShopView`를 구현한 MonoBehaviour 프리팹을 제작하고 `StageShopService.shopViewPrefab`에 지정합니다. `Render(ShopViewState)`로 표시하고 `PurchaseRequested(offerId)`와 `CloseRequested` 이벤트만 보냅니다.
3. 골드 HUD는 `IGoldView.Render(int gold, bool visible)`를 구현하고 `goldViewPrefab`에 지정합니다. 기존 ARPG HUD 코드는 변경할 필요가 없습니다.

상품 목록은 `ShopViewState.Offers`에 표시 정보·가격·버튼 상태로 전달됩니다. 임시 UI는 ScrollRect를 사용하므로 상품 수를 늘려도 스크롤할 수 있습니다. 한국어 TMP 폰트, CanvasScaler, GraphicRaycaster와 활성 EventSystem을 유지하세요. 임시 상점 Canvas 정렬은 150, 결과 화면은 200입니다.

상점 일시정지는 `RunManager.TryOpenShop(owner)` / `CloseShop()`이 관리합니다. 직접 Time.timeScale을 바꾸거나 스테이지 전환 함수를 호출해서 상점을 열지 마세요. 외부 시간 제어는 기존 `CombatHitStopService.SetExternalWorldTimeScale`을 사용합니다.

## 검증과 설치 도구

프로젝트 루트에서 연결된 Unity Editor에 실행합니다.

```powershell
# Edit Mode
unity command eval --caller plugin --skill unity-cli 'return ShopEconomyRegression.Run();'
unity command eval --caller plugin --skill unity-cli 'return ShopModalRegression.Run();'
unity command run_tests --caller plugin --skill unity-cli -- --mode editor --filter BaekhoDungeonPlannerTests --async_tests true
unity command run_script --caller plugin --skill unity-cli -- --file Tools/RunProgressionRegression.cs --entry RunProgressionRegression.Run

# Game Scene의 Play Mode, 스테이지 준비 완료 후
unity command run_script --caller plugin --skill unity-cli -- --file Tools/ShopPlayModeRegression.cs --entry ShopPlayModeRegression.Start
unity command eval --caller plugin --skill unity-cli 'return UnityEditor.SessionState.GetString("Shop.PlayModeRegression", "No result");'

# 실제 F 키·마우스 클릭·휠·Esc 입력 경로
unity command run_script --caller plugin --skill unity-cli -- --file Tools/ShopPointerRegression.cs --entry ShopPointerRegression.Start
unity command eval --caller plugin --skill unity-cli 'return UnityEditor.SessionState.GetString("Shop.PointerRegression", "No result");'
```

통합 검사는 비동기로 진행합니다. 시작 응답과 마지막 결과를 구분해서 확인하세요. 실행 중에는 소스를 수정하거나 재컴파일하지 마세요.

입력 검사는 포커스가 없는 Editor에서도 가상 키보드·마우스가 플레이 루프로 전달되도록 검사 동안만 입력 포커스 설정을 바꾸고 종료 시 복원합니다. 프로젝트 설정을 저장하지 않습니다.

설치 메뉴는 `Tools > Studio Baekho > Shop > Install In Game Scene`입니다. 기존 상점 설정과 프리팹을 초기화하지 않습니다. 씬 설치 전 현재 편집 상태의 복사본을 프로젝트 루트 `ShopIntegration/Backups`에 남깁니다. 영구 저장, 아이템 판매, 리롤, 소모품 인벤토리는 포함하지 않습니다.

### 검증 결과 · 2026-10-24

- Unity 6000.3.10f1 컴파일 오류 없음.
- 경제 회귀 1,035개, 모달 소유권 25개, 던전 플래너 NUnit 42개 통과.
- 런 정의·방 수·플래너 회귀 4,977개 통과. 기존 아이템 런타임·전투 회귀도 통과.
- 실제 상점 Play Mode 104개, F·마우스·스크롤·Esc 입력 43개 통과.
- 현재 설정의 8개 스테이지 전체 진행 1,047개 검증 통과. 보스·출구·챕터 전환·오류 재시도·자원 유지·최종 승리를 포함합니다.
- 화면 확인본: 프로젝트 루트 `ShopIntegration/shop-preview.png`.
