# 경계ZERO 골드 HUD · 디자인 수정본

실제 채택된 BoundaryZERO QHD 최신 UI를 기준으로 골드 잔액과 획득 로그를 추가했습니다. 칼날 문장·붉은 봉인, 미니맵, 오행 슬롯, E/R 배치를 보존합니다. 수정 납품본에서는 이전의 일반적인 PENTA 검정 패널 버전을 이 디자인으로 대체합니다.

골드 잔액은 테두리 없이 오행 폼 위에 배치하고, 동전 아이콘의 왼쪽 선을 첫 번째 폼에 맞췄습니다. 획득 로그는 오른쪽 가운데의 기존 프레임을 사용합니다.

납품 위치: `Deliverables/11_골드_HUD_획득로그/경계ZERO_디자인수정`  
패키지: `BoundaryZERO_GoldHUD.unitypackage`

## 적용

Unity 패키지를 가져온 뒤 통합 프리팹 `BoundaryCompleteUI_Gold.prefab`을 사용합니다. 제작용 프리팹에는 골드 샘플 입력(G/B/V/P/F2)이 없습니다. 기존 Canvas 기준 해상도는 2560×1440이며, 다른 화면 비율에서는 전체 디자인을 중앙 안전 영역에 비율 유지하여 맞춥니다.

기존 Canvas에 위젯만 추가하려면 아래 독립 프리팹을 사용합니다.

- `Assets/BoundaryUI/Resources/BoundaryUI/GoldHUD/GoldBalance.prefab`
- `Assets/BoundaryUI/Resources/BoundaryUI/GoldHUD/GoldRewardFeed.prefab`

통합 UI에 이미 포함된 위젯을 독립 프리팹으로 다시 배치하면 중복 표시됩니다. 통합 방식과 독립 배치 중 하나를 선택하세요.

## 게임 연결

`BoundaryUIController`의 `GoldHud`, `SetGold(int)`, `ShowGoldGain(int)`을 사용합니다. 기존 `SetHudData`는 그대로 유지되지만, 요청에 따라 HUD의 `데이터`와 층·방 문구는 표시하지 않습니다. `ReferenceHUD`의 `BoundaryZeroUI.BoundaryGoldHud`에서도 `Balance`, `Feed`, `SetGold`, `ShowGoldGain`에 접근할 수 있습니다.

```csharp
using BoundaryZeroUI;

// 게임의 실제 거래가 확정된 뒤 Unity 메인 스레드에서 호출합니다.
// balanceAfter: 확정된 최종 잔액, actual: 실제 지급된 양
void OnGoldRewardConfirmed(BoundaryUIController controller, int balanceAfter, int actual)
{
    controller.SetGold(balanceAfter);
    controller.ShowGoldGain(actual); // 같은 지급 이벤트당 한 번만 호출
}

// 초기 동기화·소비·불러오기는 잔액만 갱신합니다.
// controller.SetGold(balanceAfter);
```

기존 `ArpgHud.PC.PentaGoldBalanceView.SetGold(int)`, `PentaGoldRewardFeed.ShowGoldGain(int)`, `Clear()` API도 유지합니다. 잔액 설정은 로그를 만들지 않습니다. 로그는 양수 획득에만 사용하며 잔액을 직접 변경하지 않습니다.

획득 로그는 0.4초 내 연속 획득을 합산하고, 2초 유지 뒤 0.3초 동안 사라집니다. 최대 3줄이며 일시정지 중에도 unscaled time으로 진행합니다. 로그 오브젝트를 숨기거나 비활성화하면 내용을 비웁니다.

새 로그는 아래에서 떠오르며 나타나고, 기존 줄은 위로 부드럽게 이동합니다. 사라질 때도 위로 살짝 떠오르며 흐려집니다. `PentaGoldRewardFeed` Inspector의 `Upward Motion`에서 등장 거리·등장 시간·줄 이동 시간·퇴장 거리를 조절할 수 있습니다.

## 샘플

`Assets/BoundaryUI/GoldHUD/Samples/BoundaryGoldHUD_Demo.unity`를 엽니다. 샘플 입력은 `BoundaryGoldHudDemo`에만 있습니다.

| 키 | 동작 |
|---|---|
| G | 골드 20 획득 |
| B | 합계 60 연속 획득 |
| V | 골드 30 소비, 획득 로그 없음 |
| P | 일시정지 전환 |
| F2 | 샘플 초기화 |

기존 E·Tab·Esc·F1 기능은 유지됩니다. 샘플 금액은 표시 확인용입니다. 실제 게임 소스, 경제·영구 성장·허브 시스템은 포함하거나 변경하지 않았습니다.
