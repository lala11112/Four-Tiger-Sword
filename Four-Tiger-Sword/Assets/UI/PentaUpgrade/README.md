# 경계ZERO 각인 강화 패널

Unity 6 · uGUI · TextMeshPro용 UI 패키지입니다. 기본값은 열린 상태입니다.

## 기존 씬에 넣기

1. Unity에서 `Assets > Import Package > Custom Package...`를 선택하고 `BoundaryZERO_UpgradePanel.unitypackage`를 가져옵니다.
2. `Assets/UI/PentaUpgrade/Prefabs/BoundaryZERO_UpgradePanel.prefab`을 기존 Canvas의 **마지막 자식**으로 드래그합니다. 또는 Canvas/그 자식을 선택한 뒤 `Tools > PENTA Upgrade > Add panel to selected Canvas`를 실행합니다.
3. 호스트 Canvas에 `GraphicRaycaster`가 있고, 씬에 프로젝트의 입력 방식에 맞는 Input Module을 가진 `EventSystem`이 하나 있는지 확인합니다. 패널은 Canvas·CanvasScaler·GraphicRaycaster·EventSystem을 추가하거나 기존 설정을 바꾸지 않습니다.
4. 패널 루트의 전체 늘이기 앵커와 0 오프셋을 유지합니다. 부모 영역에 맞춰 내부 패널이 자동 축소/확대됩니다. 호스트 Canvas가 Layout Group으로 직접 자식을 배치한다면 별도의 전체 크기 컨테이너 아래에 넣습니다.

## 열기와 닫기

패널 루트의 `PentaUpgradeView.Open()` / `Close()`를 호출하거나 루트 GameObject의 활성 상태를 전환합니다. 닫기 버튼도 사용할 수 있습니다. 프로젝트에서 ESC를 별도 Input Action으로 관리하는 경우 해당 동작을 `Close()`에 연결할 수 있습니다.

## 세로 줄기형 v7 상태

사용자가 선택한 것은 2안 세로 줄기형 배치와 탐색 방향입니다. 노드 이름·효과·상세 설명과 해금 관계는 시각화용 후보이며 승인된 기획이 아닙니다. 기존 40개 노드·39개 의미 연결과 의미 단계별 1/5/16/13/5개 구성을 유지하면서, 1178×5276 영역을 세로로 탐색합니다. 정상 배율에서 가로 폭이 뷰포트에 들어오며 큰 노드 크기를 유지합니다.

## 포함 범위

큰 노드, 드래그·확대/축소 트리, 무료 시작점 1개와 강화 노드 39개(총 40개)·단순 연결 39개, 상세 설명을 포함합니다. 후속 각인 영역은 제거한 상태입니다. 이름과 효과는 검토 중이며 수치·가격·레벨은 미배정입니다. 실제 능력 효과·강화 수치·재화 차감·저장·게임 진행 연동은 구현하지 않았습니다.

미확정 시각화 후보 참고: [노드 기획 v6](노드_기획_v6.md).

독립 Canvas 버전은 `Prefabs/PENTA_Upgrade.prefab`, 배경이 있는 샘플은 `Samples/PENTA_Upgrade_Demo.unity`입니다. 기존 Canvas에 넣을 때는 `BoundaryZERO_UpgradePanel.prefab`을 사용합니다.

에디터 재생성: `Tools > PENTA Upgrade > Create or rebuild assets and demo`.
검사: `Validate assets`. 빌드 없이 기존 자산을 재수출: `Export package only`.
