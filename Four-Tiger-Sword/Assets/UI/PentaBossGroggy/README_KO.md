# 보스 HP·그로기 교체 패키지

`BoundaryZERO_BossHP_Groggy_Replacement.unitypackage`에는 양끝 연결부를 다듬은 보스 HP·그로기 UI, 글꼴, 스프라이트와 값 연결용 컴포넌트가 들어 있습니다.

## 교체 순서

1. Unity에서 `Assets > Import Package > Custom Package`로 패키지를 선택하고 Import합니다.
   TextMeshPro 기본 리소스가 처음 필요한 프로젝트에서는 패키지가 Unity의 TMP Essential Resources를 자동으로 준비합니다. 준비가 끝날 때까지 기다리세요.
2. Project 창에서 `Assets/UI/PentaBossGroggy/Prefabs/BossHPWithGroggy.prefab`을 찾습니다.
3. 기존 보스 UI가 들어 있는 Canvas 안에 프리팹을 배치합니다. 기본 크기는 640 × 54이며, RectTransform으로 위치와 폭을 맞출 수 있습니다.
4. 아래 방식으로 기존 보스의 이름·체력·그로기 값을 연결합니다.
5. 값이 정상적으로 표시되는 것을 확인한 뒤 이전 보스 UI 오브젝트를 비활성화합니다. 기존 보스 제어 스크립트가 붙어 있는 부모를 함께 끄지 않도록, 교체할 표시용 오브젝트를 선택하세요.

프리팹 루트의 `BossHealthGroggyUI`에는 Health Fill, Groggy, Boss Name 참조가 연결되어 있습니다. HP는 가득 찬 상태, 그로기 누적은 0으로 시작합니다.

## 기존 보스 코드에서 값 전달

기존 보스 표시 스크립트에 참조를 추가하고 Inspector에서 새 프리팹을 연결합니다.

```csharp
using Penta.BossUI;
using UnityEngine;

public class BossHudBinding : MonoBehaviour
{
    [SerializeField] private BossHealthGroggyUI bossUI;

    // 보스 등장과 상태 초기화 시 호출합니다.
    public void InitializeBoss(string bossName, float currentHP, float maxHP,
        float accumulatedGroggy, float maxGroggy)
    {
        bossUI.SetBossName(bossName);
        bossUI.SetHealth(currentHP, maxHP);
        bossUI.SetGroggy(accumulatedGroggy, maxGroggy);
    }

    // 기존 보스의 체력 변경 이벤트에서 호출합니다.
    public void OnHealthChanged(float currentHP, float maxHP)
    {
        bossUI.SetHealth(currentHP, maxHP);
    }

    // 기존 보스의 그로기 수치 변경 이벤트에서 호출합니다.
    public void OnGroggyChanged(float accumulatedGroggy, float maxGroggy)
    {
        bossUI.SetGroggy(accumulatedGroggy, maxGroggy);
    }
}
```

게임이 이미 0~1 비율을 계산한다면 `SetHealthNormalized(value)`와 `SetGroggyNormalized(value)`를 사용하세요. 위 메서드는 게임의 상태 변경 시 호출해야 합니다. 보스 전투 시스템에서 자동으로 데이터를 찾거나 경직을 발생시키는 기능은 별도 연결이 필요합니다.

기존 코드가 `Image.fillAmount`를 직접 갱신한다면 새 프리팹의 `HP Fill` Image를 기존 참조 필드에 연결할 수 있습니다. 보스 이름은 `Boss Name`의 TextMeshPro를 사용합니다. Slider를 직접 조작하던 코드라면 위 `SetHealth` 방식으로 연결하세요.

## 그로기 표시 방향

맞을수록 누적 수치는 올라가고 회색 잔량은 줄어듭니다.

| 전달값 | 회색 잔량 |
|---|---|
| `SetGroggy(0, 100)` | 100% |
| `SetGroggy(65, 100)` | 35% |
| `SetGroggy(100, 100)` | 0% |

누적 100%에서 `bossUI.Groggy.IsBroken`이 참입니다. 실제 보스 경직과 회복 판정은 기존 전투 시스템에서 처리하고, 회복된 누적값을 UI에 전달합니다.

## 기존 HP 디자인에 회색 게이지만 추가

기존 HP의 `Filled / Horizontal / Left` Image 또는 왼쪽부터 채워지는 Slider를 선택하고 `GameObject > 경계 ZERO > UI > 보스 HP 아래에 그로기 게이지 추가`를 실행합니다. 기존 채움 영역의 아래 25%에 회색 게이지가 생성됩니다. 같은 대상에 다시 실행하면 추가된 게이지를 선택합니다.

생성된 `GroggyTrack`의 `BossGroggyHPIntegration.SetGroggy(누적값, 최대값)`에 값을 전달합니다. 직접 만든 셰이더나 별도 Layout으로 크기를 제어하는 HP는 개별 연결이 필요할 수 있습니다.

`Samples/BossHPWithGroggy_Sample.prefab`은 HP 75%·그로기 누적 65%로 저장된 미리보기용입니다. `Prefabs/BossGroggyGauge.prefab`은 이전 독립형 게이지와의 호환을 위해 포함합니다. 전체 UI를 교체할 때는 `Prefabs/BossHPWithGroggy.prefab`을 사용하세요.

Unity 6000.3.21f1, uGUI 2.0.0 기준으로 제작했습니다. 기본 배경은 투명하고 별도 Canvas나 EventSystem을 추가하지 않습니다.
