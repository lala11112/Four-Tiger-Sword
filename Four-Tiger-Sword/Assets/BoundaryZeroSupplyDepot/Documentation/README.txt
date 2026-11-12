경계 ZERO · 보급고 Unity 전달 패키지
2026-11-11

1. Unity에서 확인하기

BoundaryZero_SupplyDepot_20261111.unitypackage를 프로젝트로 가져옵니다.
Assets/BoundaryZeroSupplyDepot/Samples/SupplyDepotDemo.unity를 열고 Play를 누르세요.
F 또는 ‘보급 받기’로 추첨, Esc로 닫기, ‘연출 건너뛰기’로 결과를 바로 확인합니다.
다시 테스트할 때는 Play를 종료하고 재생하면 데모 계정이 초기화됩니다.

Unity 6000.3.10f1에서 제작·검증했습니다. uGUI(Unity UI 2.0)와 TextMeshPro를 사용합니다.
프리팹, 데모 씬, 아이템 81종, 시너지 아이콘, 한글 폰트 및 필요한 TMP 리소스를 포함합니다.
TMP 기본 리소스가 없는 새 프로젝트에서는 설치된 Unity UI 패키지에서 자동 준비합니다.
Unity의 기존 씬이나 프로젝트 설정 파일은 패키지에 포함하지 않습니다.

2. 실제 게임에 연결하기

게임 씬에 Prefabs/SupplyDepot.prefab을 배치합니다.
루트의 SupplyDepotController에 게임 계정 컴포넌트를 Account Provider로 연결하세요.
이 컴포넌트는 ISupplyDepotAccount 인터페이스를 구현해야 합니다.
이미 사용하는 영구 재화·아이템 인벤토리·저장 시스템은 이 연결부에서 읽고 갱신합니다.

기존 보급고 F 상호작용 → controller.Open()
런 종료 → controller.NotifyRunEnded()
게임의 입력 처리를 직접 연결한다면 Automatic Keyboard Input을 끄고
열린 창에서 TryDraw(), Close(), SkipAnimation()을 호출합니다.

금액 차감·아이템 지급·이번 런 사용 기록은 TryPurchase() 안에서 한 번에 처리합니다.
RewardGranted는 결과 공개 알림입니다. 이 이벤트에서 아이템을 다시 지급하지 마세요.
정확한 계약과 저장·커서·입력 연결은 Docs/RuntimeContract.md에 있습니다.
DemoSupplyDepotAccount는 테스트용 메모리 계정이며 실제 게임의 저장 기능이 아닙니다.

3. 조정 가능한 항목

Settings/SupplyDepotCatalog.asset에서 비용, 연출 시간, 아이템과 시너지 데이터를 수정합니다.
기본 비용 100, 데모 시작 강화석 1,200은 시안용 설정값입니다.
현재 81개 아이템은 등급 가중치 없이 모두 같은 확률로 추첨합니다.
이번 런에 사용한 뒤에는 닫았다 열어도 재추첨되지 않으며, 런 종료 후 다시 사용할 수 있습니다.

창 내부 레이아웃은 프리팹에서 편집할 수 있습니다.
아이템·시너지 설명이 길어지면 결과 패널 내부만 스크롤됩니다.
완료 상태 배지, 런 횟수 표시, 01·02 번호, 보급품 목록, 진입하기 버튼은 없습니다.

4. 폴더 안내

BoundaryZero_SupplyDepot_20261111.unitypackage : Unity로 가져올 파일
UnitySource/Assets : 패키지에 포함된 편집 원본과 Unity 메타 파일
Docs : 실제 게임 연결 계약과 검증 결과
Screenshots : 승인된 브라우저 시안 및 Unity 실행 화면
Preview/index.html : 설치 없이 확인하는 브라우저 시안
Source : 브라우저 시안 수정용 원본
README-Preview.txt : 브라우저 시안 조작 및 초기화 방법

UnitySource와 unitypackage는 같은 원본입니다. 처음 가져올 때는 unitypackage를 사용하세요.
한글 폰트 Pretendard Regular의 SIL Open Font License 전문을 Fonts에 포함했습니다.
