BAEKHO MAP — Unity 가져오기 0.4

1. ZIP 파일의 압축을 풉니다.
2. 원본 프리팹과 .meta가 있는 Unity 프로젝트를 엽니다.
3. Unity/Editor/BaekhoMapImporter.cs를 Assets/Baekho/Editor/BaekhoMapImporter.cs로 복사합니다. Unity/Runtime의 세 파일을 모두 Assets/Baekho/Runtime/에 복사합니다: Assets/Baekho/Runtime/BaekhoMapConnection.cs, BaekhoGameplayMarker.cs, BaekhoMapFixture.cs. 기존 설치는 같은 경로에서 업데이트하고 중복 설치하지 않습니다. 모든 스크립트를 설치한 뒤 Unity 컴파일을 기다립니다.
4. Map.baekhomap.json을 Assets 아래로 복사합니다.
5. JSON을 선택하고 Assets > Baekho > Build Prefab을 실행합니다.
6. JSON 파일과 같은 폴더에 새 프리팹이 생성됩니다. 기존 프리팹을 덮어쓰지 않습니다.

배치 기준
- 형식 버전 4. 좌표는 맵 중심 원점, X/Z 바닥, Y 높이, 미터 단위입니다. 원본 프리팹 사본은 포함되지 않고 GUID로 참조합니다.
- 바닥 윗면과 천장 아랫면이 지정된 높이에 맞춰집니다. 두께는 셀 간격 × 12% × 두께 배율 Y입니다.
- 다리는 상판 전용 프리팹의 윗면을 높이에 맞춥니다. 상판에 난간을 합치지 말고 별도 난간 프리팹을 사용하세요. 다리 아래 바닥·받침·통행면은 생성하지 않습니다.
- 벽의 지정 높이와 절벽 깊이는 실제 경계를 측정해 맞춥니다. 이전 프로젝트의 지정하지 않은 벽 높이와 에셋 배율은 보존합니다.
- 계단·경사로 원본은 저단에서 +Z로 상승합니다. Y 스케일은 rise / stairNativeRise로 자동 계산합니다. X/Z 크기와 원본 상승 높이를 에셋 설정에서 맞춰 주세요.
- 조명과 독립 구조물은 원본 프리팹 루트 피벗에 보정 Transform을 적용합니다. 광원 설정·컴포넌트·중첩 프리팹 참조는 보존합니다.
- Connections에는 BaekhoMapConnection, GameplayMarkers에는 BaekhoGameplayMarker가 저장됩니다. 구조물의 BaekhoMapFixture.linkedId는 연결할 구조물의 내보내기 ID입니다.
- 게임 지점·함정·위험 바닥·문·스위치는 위치와 연결 정보입니다. 실제 스폰·피해·보상·상호작용과 맵 사이 통로 제작은 Unity에서 처리합니다.
- maxStepHeight와 머리 위 공간 검사는 설계 구조 기준이며 실제 캐릭터 충돌체·NavMesh·프리팹 메시 검증은 Unity에서 진행하세요.
- 기존 schemaVersion 1/2/3 JSON도 각 버전의 배치 의미로 가져옵니다.
