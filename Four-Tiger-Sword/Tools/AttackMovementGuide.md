# 공격 전진 설정

## 동작

일반 공격 및 공통 강공격은 회전 → 이동량 계산 → 접근 제한 → CharacterController 이동 → 타격 판정 순서다.
Use Target Approach와 Follow Moving Target이 켜져 있고 유효한 타깃이 있으면, 각 타수에서 적의 현재 위치를 계속 추적한다.
기존 전진 커브 대신 Target Follow Speed를 사용하며, 초기 회전 이후에도 Target Follow Rotation Speed로 방향을 보정한다.
추적은 이동 시작부터 기존 이동 종료와 마지막 타격 판정 종료 중 늦은 시점까지 이어지고, Duration을 넘지 않는다.
적 앞에서 멈췄다가 적이 멀어지면 남은 시간·거리 안에서 다시 접근한다. 다음 타수는 이동 한도를 새로 부여받는다.
현재 모든 폼의 일반 공격에 기본 적용되며, 전진 커브가 0인 목·금도 유효한 타깃을 따라간다.
타깃이 없거나 추적 옵션이 꺼져 있으면 기존 누적 거리 커브를 사용한다.
공격 속도는 재생 시간을 바꾸지만, 장애물이나 타깃 정지 조건이 없는 경우 총 이동 거리는 유지한다.
총 거리는 실현을 보장하는 거리가 아닌 요청 거리다. 벽, 정지 간격, 방향 불일치, 타깃 상실로 실제 이동은 짧아질 수 있다.
막힌 이동량은 나중에 몰아서 적용하지 않는다.

## Inspector에서 설정할 곳

Assets/_Project/Scripts/FormActions 아래의 각 폼 Action 데이터에서 Combo Steps의 각 타수와 공통 Heavy Attack Step을 펼친다.
데이터 에셋 자체는 이번 변경에서 덮어쓰지 않았다. 기존 값은 자동 호환되므로 필수 수동 이관은 없다.
명시적인 거리 조절을 원하면 아래처럼 새 모드를 켠다.

| 필드 | 의미 / 시작 예시 |
| --- | --- |
| Use Distance Movement | 켜기. 끄면 기존 Thrust Curve/Multiplier를 공격 시작 때 누적 거리 표로 적분한다. |
| Forward Distance | 총 전진 요청 거리. 예: 1.5m |
| Movement Start Time | 이동 시작 시점. 예: 0.08초 |
| Movement End Time | 이동 종료 시점. 예: 0.25초. Start보다 크고 Duration 이하여야 한다. |
| Movement Progress | 누적 이동률 커브. (0,0) → (1,1). 직선은 등속, 시작이 가파르면 초반 이동이 빠름. |
| Untargeted Distance Multiplier | 타깃 없이 시작하는 공격의 거리 배율. 0=제자리, 0.5=절반, 1=동일 거리. |
| Use Target Approach | 일반/공통 강공격의 타깃 접근·정지 간격 적용. |
| Follow Moving Target | 기본 켜짐. 타깃의 이동에 맞춰 지속적으로 회전·접근한다. |
| Target Follow Speed | 추적 속도. 기본 6m/s, 공격 속도 배율 적용. |
| Target Follow Rotation Speed | 초기 정렬 이후 추적 회전 속도. 기본 720도/초, 공격 속도 배율 적용. |
| Stop Distance | 플레이어 몸체와 적 콜라이더 사이 간격. 예: 0.25m |
| Max Approach Distance | 한 타수의 실제 이동 상한. 타깃 없는 공격에도 적용. 예: 2m |
| Approach Alignment Angle | 접근 이동 허용 오차. 기본 15도. 이 값은 접근 정렬 제한이며, 선택 콘은 PlayerController의 Targeting Half Angle로 설정한다. |
| Rotation End Time | 회전 완료 시점. 예: 0.08초. 이동 시작/첫 타격보다 늦으면 자동으로 앞당긴다. 0이면 시작 시 즉시 회전. |

예시는 Hit Start Time이 0.25초 이상이고 Duration도 충분한 공격에 대한 출발점이다.
빠른 공격은 Rotation End Time ≤ Movement Start Time < Movement End Time ≤ Duration로 줄여 조정한다.
타격 시작 전 접근을 끝내려면 Follow Moving Target을 끄고 Movement End Time을 첫 Hit Start Time 이하로 둔다.
콤보 전환이 이동 종료보다 빠르면 다음 타수로 넘어가면서 남은 이동이 취소된다.

누적 커브는 단조 증가하도록 사용한다. 런타임은 샘플 값을 0~1로 정규화하고 감소 구간을 평탄화한다.
끝값이 시작값보다 크지 않은 잘못된 커브는 선형 진행으로 대체한다.

## 기존 데이터 호환

Use Distance Movement가 꺼져 있어도 새 누적 거리 계산 경로를 사용한다.
기존 속도 커브를 128구간 사다리꼴 적분하여 공격 속도 1 기준의 전진 거리를 근사한다.
접근 공격은 Approach End Time까지, 나머지는 Duration까지 적분한다.
기존 모드는 짧은 회전 준비 구간(최대 0.08초)을 확보한 뒤 누적 이동을 재생한다.
기존의 음수 속도(후진)는 이 전진 전용 프로파일에서 제외한다.
새 거리 모드에서는 Thrust Curve, Thrust Multiplier, Approach End Time을 사용하지 않는다.
스킬/궁극기/반격의 공통 MoveForward도 거리 프로파일을 사용하지만, Use Target Approach의 정지·상한은 일반/공통 강공격에만 적용된다.

## 물 강공격과 토 스킬

물 강공격은 별도 돌진 로직을 유지한다. 위 공통 이동 거리·회전·정렬 설정은 물 강공격 이동에 적용되지 않는다.
Water Heavy Dash Distance, Water Heavy Dash Duration, Water Heavy Target Overshoot로 조정한다.
첫 돌진은 이동 입력이 있으면 입력 방향, 없으면 카메라 시선 중심에 가장 가까운 유효 타깃, 타깃도 없으면 현재 방향이다.
연속 돌진은 유효한 기억 타깃을 최우선으로 삼는다. 각도 제한 없이 즉시 방향을 맞춘 뒤 직선 돌진한다.
토 스킬은 기존 Skill Dash Distance/Duration으로 설정한다.

## 검증 실행

Unity Pipeline run_script에서 Tools/PlayerTargetingRegression.cs의 PlayerTargetingRegression.Run을 Edit Mode에서 실행한다.
임시 씬에서 타깃 유효성, 15/30/60/144 FPS × 공격 속도 0.5/1/2, 전진 종료, 허공 거리 상한,
180도 회전과 접근, 정지 간격, 타깃 사망, 벽 충돌, 호환 적분, 물 강공격 8방향/입력/연속 방향을 확인하고 씬을 정리한다.
