PENTA 인게임 HUD — Unity 6 연결 안내

제작 기준: Unity 6000.3.21f1 / Unity UI(com.unity.ugui) 2.0.0 및 포함된 TextMeshPro.
Input System이나 특정 렌더 파이프라인을 요구하지 않습니다.

1. 가져오기
전달 파일 PENTA_InGame_HUD_Friend.zip을 풀고 Unity 프로젝트에서
Assets > Import Package > Custom Package…로
PENTA_InGame_HUD_Unity6.unitypackage를 가져옵니다.
Unity UI 패키지가 없다면 Package Manager에서 먼저 설치하세요.
HUD 전용 폰트는 Integration/Fonts에 포함됩니다. 패키지 자체에는 전역
TMP Settings가 포함되지 않으며 기존 TMP 설정을 덮어쓰지 않습니다.
처음 TextMeshPro를 사용하는 프로젝트는 TMP Essentials를 한 번 준비해야 합니다.
Tools > PENTA HUD > Prepare Text Resources를 실행하세요. TMP_Settings가 없을
때만 Unity의 기본 TMP Essentials를 가져와 Assets/TextMesh Pro에 리소스를 만듭니다.
기존 TMP 설정이 있으면 그대로 유지합니다.

2. 장면에 배치
새 프로젝트에서 직접 프리팹을 드래그하거나 데모 장면을 열기 전에는 위의
Prepare Text Resources를 먼저 실행하세요.
Assets/UI/ARPGHUD/Integration/Prefabs/PENTA_InGameHUD.prefab을
Hierarchy의 최상위(장면 루트)에 한 번 드래그합니다.
자체 Screen Space - Overlay Canvas가 있으므로 기존 Canvas 안에 넣지 마세요.
기준 해상도는 1920×1080, Canvas Scaler의 Match는 0.5입니다.
다른 UI에 가려지면 HUD Canvas의 Sort Order를 프로젝트에 맞게 조정하세요.
표시 전용이므로 EventSystem이나 입력 설정은 필요하지 않습니다.

선택 사항: 편집기 메뉴
Tools > PENTA HUD > Add HUD to Current Scene으로도 장면 루트에 프리팹을
추가할 수 있습니다. TMP 리소스가 없으면 먼저 자동 준비합니다.
기존 HUD가 있으면 해당 HUD를 선택하여 중복 추가를 피하며,
추가는 Undo로 되돌릴 수 있습니다.
HUD를 선택하고 Tools > PENTA HUD > Validate Selected HUD를 실행하면
컴포넌트 연결을 검사하고 Console과 대화상자에 결과를 표시합니다.
이 메뉴는 장면을 자동 저장하거나 Player Settings 및 Packages 설정을 변경하지
않습니다. 최초 TMP 준비 시에는 위에서 설명한 Assets/TextMesh Pro 리소스가
추가됩니다.

3. 게임 데이터 연결
자신의 MonoBehaviour에 [SerializeField] HudController hud;를 선언하고,
프리팹의 HudController 컴포넌트를 Inspector 슬롯에 연결하세요.
API_EXAMPLES.txt의 예제를 복사해서 실제 체력/필살기 충전량/속성/쿨다운 데이터와 연결합니다.
프리팹의 HudView 및 그래픽 참조는 이미 연결되어 있습니다.
게임 코드가 별도 .asmdef를 사용한다면 해당 Assembly Definition의 References에
Penta.ArpgHud.Runtime을 추가하세요. 일반 Assembly-CSharp 스크립트는 바로 사용할 수 있습니다.

호출할 함수
• SetHealth(current, maximum): 체력
• SetUltimateCharge(current, maximum): 필살기 충전량
• SetForm(HudElement) 또는 SetForm(int): Fire=1, Water=2, Wood=3, Metal=4, Earth=5
• SetCooldownE(remainingSeconds, durationSeconds): E 남은 시간과 전체 재사용 시간
• SetCooldownR(remainingSeconds, durationSeconds): R 남은 시간과 전체 재사용 시간
• Refresh(): 보관 중인 상태로 다시 표시
• CurrentState: 현재 보관된 상태의 복사본 / View: 연결된 HudView

체력과 필살기 충전량은 화면에서 0~100%로 제한됩니다. 최대값이 0 이하이면 빈 막대로
표시됩니다. CurrentState는 전달받은 원본 값을 보관합니다.
1~5 밖의 속성 번호는 무시하고 직전 속성을 유지합니다.
속성 변경 시 E/R 아이콘과 테두리 색도 자동으로 바뀌며 쿨다운 시간은 유지됩니다.
쿨다운 숫자는 소수 첫째 자리(예: 3.2)까지 표시하고 준비 완료 시 숨깁니다.

게임이 시간을 관리합니다
HUD는 자체 타이머나 스킬 입력을 실행하지 않습니다. 실제로 시전이 승인된
시점에만 쿨다운 시작 값을 보내고, 진행 중에는 게임이 계산한 남은 시간을
매 프레임 또는 게임의 갱신 주기에 맞춰 전달하세요. 거절된 시도에는 쿨다운을
다시 시작하지 마세요. 준비 완료는 남은 시간 0으로 전달합니다.
일시정지, Time.timeScale, 서버 시간, 속성별 쿨다운 보관 정책은 게임에서 결정합니다.

선택 사항: 연결 샘플
Integration/Samples/HUD_ConnectionDemo.unity를 열어 표시를 살펴볼 수 있습니다.
기존 Input Manager가 활성화되어 있으면 1~5 속성, E/R 스킬,
H/J 체력 감소/증가를 사용합니다.
새 Input System만 활성화된 프로젝트에서는 샘플 키보드 입력이 동작하지 않습니다.
Play 중 HudConnectionExample 컴포넌트 메뉴의 Sample/Use E, Sample/Use R,
Sample/Reset 또는 공개 메서드 UseE(), UseR(), ResetSample()을 사용하세요.
샘플은 자체 모의 값을 계속 전송하므로 실제 플레이어에 붙이지 마세요.

고급 연결
HudView.Render(HudState)로 완전한 상태를 직접 전달하거나
HudView.Bind(IHudStateSource)로 상태 공급자에 연결할 수도 있습니다.
한 HUD에서 이 방식과 HudController를 동시에 사용하면 서로 값을 덮어쓰므로
한 가지 연결 방식만 선택하세요. 일반 연결에는 HudController를 권장합니다.

문제 확인
• HUD가 안 보임: 프리팹 활성 상태, Canvas, Sort Order, 카메라/UI 가림을 확인합니다.
• 쿨다운이 멈춤: HUD에는 타이머가 없으므로 게임이 남은 시간을 계속 보내야 합니다.
• 표시가 샘플 값으로 돌아감: HudConnectionExample이 함께 실행 중인지 확인합니다.
• TMP/UnityEngine.UI 컴파일 오류: Unity UI 패키지 설치 및 패키지 로딩 완료를 확인합니다.


