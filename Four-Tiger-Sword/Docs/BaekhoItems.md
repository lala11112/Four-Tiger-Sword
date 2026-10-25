# 백호툴 아이템 반입과 게임 연결

백호툴 아이템 제너레이터의 JSON·PNG 출력 묶음을 Unity의 `ItemCatalog`와 `ItemDefinition`으로 반입하고, 플레이어가 실제로 소지한 아이템의 효과를 전투에 적용합니다. 카탈로그를 연결한 것만으로 아이템을 획득하지는 않습니다.

## 출력 형식과 출처

반입 입력은 편집 프로젝트인 `draft.json`이 아니라 다음 파일을 가진 출력 폴더입니다.

```text
<백호툴 출력 폴더>/
  saingeom-items.v1.json
  manifest.json
  validation-report.json
  README.md
  icons/*.png
```

- 지원 형식은 `schemaVersion: 1`입니다. 오행은 문자열 `wood/fire/earth/metal/water`이며 `metal`은 게임의 `ELEMENT_GOLD`로 연결됩니다.
- 비율 `0.08`은 8%입니다. `maxHp`, `normalDamage`, `switchDamage`는 가산 비율이고 `maxSp`, `spRegen`, `dodgeSpCost`, `switchSpCost`는 가산 수치입니다.
- `whileOwned`는 보유 중 적용합니다. `formSwitchSucceeded`는 전환이 성공했을 때 주어진 초 동안 적용하며, 같은 출처의 재발동은 남은 시간을 갱신합니다.
- `targetForms`가 `"all"`이면 모든 폼, 배열이면 해당 폼에서 적용합니다. 아이템의 표시 오행과 효과의 적용 폼은 별개입니다.
- 아이템당 보유 수는 1개입니다. 여러 아이템의 같은 능력치는 출처별로 더합니다. 제거 시 해당 아이템의 상시·기간 효과를 함께 제거합니다.

이번 연결에 사용한 실제 생성본의 원본 보관 위치는 프로젝트 루트 기준 다음과 같습니다.

```text
BaekhoItemIntegration/Source/93190cd9-a826-4b58-b257-cf4ba077b827-r71/
```

이 묶음은 `studio-baekho-tools` 저장소의 `examples/saingeom-items/93190cd9-a826-4b58-b257-cf4ba077b827/review-bundle/93190cd9-a826-4b58-b257-cf4ba077b827-r71-e09139a2-7121-42e6-b75c-e12a533428ed/`에서 가져온 revision 71입니다. 제작 도구의 형식 정의는 같은 저장소의 `shared/four-tiger-items.ts`, 출력 구현은 `engine/four-tiger-items/export.ts`에 있습니다.

20개 모두 `draft`이며 개별 아이템과 정책의 검토·채택 상태도 원본 그대로입니다. 반입은 상태를 `adopted`로 바꾸지 않습니다. 초안도 보유·전투 시험에 사용할 수 있습니다. 초안 보고서의 `canExport: true`는 초안 묶음 저장을 허용한다는 뜻이며, 보고서 안에는 미채택 관련 오류가 함께 있을 수 있습니다.

## 반입과 재반입

1. Play Mode를 종료합니다.
2. `Tools > Studio Baekho > Items > Import Item Package`를 선택합니다.
3. `saingeom-items.v1.json`이 들어 있는 출력 폴더를 선택합니다.
4. 완료 후 선택된 `ItemCatalog`에서 20개 정의와 아이콘을 확인합니다.

생성 위치는 다음과 같습니다. 원본 출력 폴더는 수정하지 않습니다.

```text
Assets/_Project/Generated/BaekhoItems/<projectId>/
  ItemCatalog.asset
  Definitions/<candidateKey>.asset
  Icons/<candidateKey>.png
```

카탈로그에는 원문 JSON·manifest·SHA-256을, 개별 정의에는 원문 아이템 JSON을 보관합니다. 아이콘은 256×256 Sprite, Single로 설정합니다. 시너지 태그·참조와 검토·이미지 출처 기록은 원문에 남기며, 이번 연결은 시너지 조합에 추가 전투 효과를 부여하지 않습니다.

재반입의 동일성은 `(projectId, candidateKey)`로 판단합니다. `itemId`와 이름을 바꿔도 같은 항목의 SO·Sprite 경로와 Unity GUID를 유지합니다. 새로운 candidateKey는 새 정의를 만듭니다. 출력에서 빠진 항목은 현재 카탈로그에서 제외하며, 다른 에셋의 참조를 보존하기 위해 기존 정의 파일을 자동 삭제하지 않습니다. 게임 지급 API는 현재 카탈로그 안의 정의를 사용합니다.

파일 해시, 필수 필드, 지원 버전, 오행·등급·가격, 효과의 연산·단위·발동·적용 폼·지속시간, 정책, PNG와 참조를 쓰기 전에 확인합니다. 지원하지 않는 효과 메모나 효과 필드는 반입 오류입니다. 편집 중인 생성 SO와 다른 소유자의 에셋을 덮어쓰지 않습니다. 적용 중 오류가 나면 기존 파일과 `.meta`를 복원하고 새로 만든 정의·아이콘을 정리합니다. 오류 메시지를 해결한 뒤 같은 출력 폴더를 다시 반입하면 됩니다.

생성 SO의 내용은 재반입 시 원본으로 갱신됩니다. 지속할 콘텐츠 수정은 백호툴에서 수정·출력한 뒤 재반입하세요.

## Game Scene에서 아이템 시험

`Assets/_Project/Scenes/Game Scene.unity`의 Player에는 생성 카탈로그가 연결된 `PlayerItemRuntime`이 있습니다.

1. 씬을 열고 Player를 선택합니다.
2. Play Mode를 시작합니다.
3. `Player Item Runtime` Inspector의 **아이템 시험**에서 이름·ID로 검색하고 **지급**, **제거**, **전체 지급**, **전체 제거**를 사용합니다.
4. 아이템을 소지한 상태로 일반 공격, 숫자 폼 전환, 회피, 패링과 반격을 시험합니다.

지급·제거 버튼은 실제 보유 API를 호출합니다. 같은 아이템을 다시 지급하면 중복 보유하지 않습니다. Play Mode를 종료하면 시험 보유 목록이 사라집니다. 카탈로그 연결은 Edit Mode에서 변경합니다.

다른 씬에 연결하려면 Project 창에서 생성된 `ItemCatalog`를 선택하고 `Tools > Studio Baekho > Items > Attach Selected Catalog To Player`를 실행합니다. 활성 씬의 PlayerController가 1개이면 해당 플레이어에 연결합니다. 여러 플레이어가 있다면 Editor API에 대상을 전달하세요. 이 작업은 Undo를 지원하며 씬을 자동 저장하지 않습니다.

전환은 성공 시에만 SP와 공유 전환 쿨타임을 소비하고 기간 효과를 발동합니다. 비용은 출발 폼에서 계산하고, 기간 효과는 도착 폼을 기준으로 발동합니다. 일반 전환은 전환 공격을 바로 실행하고, 패링 성공 전환은 패링·반격 흐름이 끝난 뒤 예약된 전환 공격을 실행합니다. 지연된 공격의 피해 값은 전환을 확정한 시점이 아니라 공격이 실제 시작하는 시점에 스냅샷으로 고정합니다. 연속 패링의 추가 타격은 전환 비용을 반복 지불하지 않습니다. 성공 전에 누른 공격은 반격으로 사용하지 않으며 성공 후 새 공격 입력을 사용합니다. 비패링 피해와 컨트롤러 비활성화는 예약 공격을 취소합니다.

전환 공격은 `switchDamage`, 일반 공격은 `normalDamage` 보너스를 사용합니다. E 스킬과 R 궁극기의 고유 비용·쿨타임·효과는 각각의 기존 실행 경로를 사용합니다.

전환 공격의 모션·타격·이동은 각 폼의 기존 `SkillSteps`를 재사용합니다. 현재 목·토·금의 일부 `AnimationName`이 빈 설정도 그대로 유지하며, 새 모션 제작은 이번 연결 범위에 포함하지 않습니다.

## 기준 수치와 전체 20개 보유 효과

아래는 현재 연결한 revision 71과 Game Scene의 기본 PlayerStatData를 기준으로 한 계산값입니다. 최대치 증가는 현재 HP·SP를 채우지 않으며, 최대치 감소 시 현재량이 새 최대치를 넘으면 제한합니다.

| 항목 | 아이템 없음 | 전체 20개 보유 | 성공 전환 후 3초 동안 |
| --- | ---: | ---: | ---: |
| 최대 HP | 5,000 | 6,300 (+26%) | 6,300 |
| 최대 SP | 100 | 145 | 145 |
| SP 재생/초 | 20 | 30 | 38 |
| 일반 공격 아이템 배율 | 1.00 | 1.49 | 1.79 |
| 전환 공격 아이템 배율 | 1.00 | 1.65 | 1.65 |
| 회피 SP 비용 | 20 | 15 | 11 |
| 전환 SP 비용 | 10 | 6 | 6 |

SP 소모 후 재생 대기는 1초, 공유 폼 전환 쿨타임은 2초입니다. 현재 묶음의 정책은 최대 SP 200, 재생 40/초, 회피 비용 하한 8, 전환 비용 하한 4입니다. 다른 수치의 묶음을 반입하면 해당 카탈로그의 정책과 효과를 사용합니다.

예를 들어 HP 5,000·SP 100 상태에서 전체 지급을 하면 최대치는 HP 6,300·SP 145가 되지만 현재 HP·SP는 5,000·100입니다. 이 상태에서 전환에 성공하면 SP는 94가 되고, 3초짜리 재생·일반 공격·회피 효과가 발동합니다. 표의 공격 배율은 아이템 부분의 배율이며 최종 피해에는 공격 데이터, 치명타, 방어력, 속성 등도 관여합니다.

## 코드 API와 검증 실행점

플레이어의 기본 초기화와 프레임 갱신은 PlayerController가 처리합니다. 상점·보상·획득물에서 연결할 최소 API는 다음과 같습니다.

```csharp
PlayerItemRuntime items = player.GetComponent<PlayerItemRuntime>();
bool added = items.TryAddItem("IT_WOOD_01");
bool owned = items.Contains("IT_WOOD_01");
bool removed = items.TryRemoveItem("IT_WOOD_01");
items.OnOwnedItemsChanged += RefreshOwnedItems;
// items.OwnedItems: 현재 보유한 ItemDefinition의 읽기 전용 목록
```

- `ItemCatalog.Find(itemId)`: 현재 카탈로그에서 정의를 찾습니다.
- `PlayerItemRuntime.Catalog`: 카탈로그 할당 시 이전 보유 목록을 비우고 효과를 재계산합니다.
- `GetNormalDamageMultiplier(form)` / `GetSwitchDamageMultiplier(form)`: 해당 폼의 아이템 공격 배율입니다.
- `PlayerController.DodgeSpCost` / `SwitchSpCost`: 플레이어에게 적용되는 현재 비용입니다.
- `FormManager.TrySwitchForm(target, reason)`: 상태·등록·자원·쿨타임을 확인하여 실제 전환을 처리합니다. 초기 폼 설정용 `ChangeForm`은 비용과 성공 효과를 발동하지 않습니다.
- `BaekhoItemImporter.ImportPackage(path)`: Editor에서 출력 폴더 또는 카탈로그 JSON 경로를 반입합니다.
- `BaekhoItemSetup.AttachCatalogToPlayer(catalog, player)`: Editor에서 지정 플레이어에 연결합니다.

전환 성공 통지와 기간 효과의 시간 갱신은 게임 연결부가 호출합니다. 보상을 지급할 때는 `TryAddItem`을 사용하면 됩니다.

이번 범위에는 상점·구매 재화·드롭 배치·인벤토리 화면·세이브/로드가 포함되지 않습니다. 가격과 아이콘은 후속 연결에 사용할 수 있는 정의 데이터로 제공됩니다.

회귀 검증 실행점:

- `BaekhoItemImporterRegression.Run()`
- `BaekhoItemImporterRegression.RunPackage(sourceFolder)`
- `BaekhoItemRuntimeRegression.Run()`
- `BaekhoItemCombatRegression.Run()`
- `BaekhoItemCombatRegression.RunPlayMode()`
- `BaekhoItemFlowRegression.RunPlayMode()`

Importer 메뉴 검증은 `Tools > Studio Baekho > Items > Run Import Regression`에서도 실행할 수 있습니다. `RunPlayMode`는 Play Mode에서 실행합니다. Flow 검증은 활성 플레이어와 반입한 실제 20개 카탈로그를 사용하며 임시 복제 플레이어로 검사합니다.

## 확인한 결과

Unity 6000.3.10f1의 열린 Game Scene에서 검증했습니다.

- 실제 revision 71 패키지: 정의 20개·Sprite 20개 반입. Import 회귀 84개 검증 통과(반복 반입, ID 변경 후 GUID 보존, 손상 입력 거절, 실패 롤백 포함).
- Runtime 회귀 8개 시나리오 통과(폼 조건, 중복 보유, 제거·재획득, 기간 효과, 최대 자원, 초기 비활성 상태 포함).
- Combat Edit Mode 및 Play Mode 검사 통과. 5폼의 실제 충돌·피해 경로, 일반/전환 피해 구분, E 비용·쿨타임·강화, R 고유 기능, Water 충돌 복원을 확인했습니다.
- Flow Play Mode 156개 검증 통과. 실제 상태 머신의 전환 실패 조건, 공격 취소 회피와 SP 부족, 연속 패링·선택 반격·예약 취소, 지연 공격 시작 전후의 효과 만료·제거를 확인했습니다.
- 실제 플레이어에게 20개 지급 직후 HP는 5,000/6,300, SP는 100/145였습니다. 성공 전환 시 SP 94, 회복 38/초, 회피 비용 11을 확인했습니다. 일시정지 중 기간 효과가 유지됐고 HUD의 SP 값과 fill 비율은 94/145로 갱신됐습니다.
- SP를 145까지 채운 후 전체 제거하면 100으로 제한됐고 HUD도 100/100으로 복원됐습니다. 시험 보유는 씬에 저장하지 않습니다.

HUD 확인 이미지는 `BaekhoItemIntegration/Verification/items-granted.png`에 보관했습니다.
최종 스크립트 컴파일은 오류 없이 완료됐고 검증 후 Unity Console의 오류 목록은 비어 있었습니다. Play Mode를 종료하고 초기 보유 0개인 상태로 두었습니다.
