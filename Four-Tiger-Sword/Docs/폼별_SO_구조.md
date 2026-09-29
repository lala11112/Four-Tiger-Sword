# 폼별 공격 데이터 SO

`WeaponActionDataSO`는 공통 데이터만 선언하는 추상 기반 클래스입니다.
폼마다 다른 설정은 해당 폼의 상속 클래스에 추가합니다.

| SO 클래스 | 전용 설정 |
|---|---|
| `WaterFormActionDataSO` | 강공격, 대시, 타겟 관통 거리, 게이지, 스킬 버프, 궁극기 범위 |
| `FireFormActionDataSO` | 범위 스킬, 공격력 버프, 토글 궁극기, 흡혈, 화염 스택 |
| `WoodFormActionDataSO` | 장판 범위/지속 시간, 회복률 |
| `EarthFormActionDataSO` | 보호막 수치/지속 시간, 범위 |
| `IronFormActionDataSO` | 현재 추가 필드 없음. 금 폼 전용 설정이 생기면 여기에 추가 |

기반 클래스에는 콤보, 패링, 공중 공격, 반격, 스킬/궁극기 공격 데이터, 공통 타격 효과와 궁극기 쿨타임만 남깁니다.
`BaseForm`은 공통 데이터를 사용하고, 각 폼은 자신의 타입으로 선언된 전용 데이터 참조를 사용합니다.
공통 전투 확장 지점과 선택적 기능 인터페이스는 유지하지만, 수/불 전용 수치는 공용 SO에 선언하지 않습니다.

새 에셋은 Unity의 `Create → Combat → Forms → Fire/Water/Wood/Iron/Earth`에서 생성합니다.
플레이어의 폼 데이터 슬롯과 폼 생성자도 해당 타입을 요구하므로 다른 폼의 SO는 연결할 수 없습니다.

기존 다섯 에셋은 경로·GUID를 유지한 채 타입을 전환했습니다.
에셋을 다시 만들거나 플레이어의 참조를 수동으로 다시 연결할 필요가 없습니다.
Inspector에서 조정한 공통 및 해당 폼 전용 값도 보존했습니다.

`Tools/FormDataMigration.cs`는 이번 전환용 도구이며, 로컬 변경 전 스냅샷은
`Temp/FormDataMigration`에 있습니다. 정기 실행이나 빌드 단계가 아닙니다.
`Verify`는 현재 데이터와 전환 전 스냅샷의 유지 대상 필드 및 GUID를 비교합니다.
