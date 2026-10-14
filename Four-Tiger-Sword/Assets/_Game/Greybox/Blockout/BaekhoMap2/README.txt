BAEKHO MAP — Unity 가져오기

1. ZIP 파일의 압축을 풉니다.
2. Baekho에서 프리팹을 스캔한 것과 동일한 Unity 프로젝트를 엽니다. GUID로 원본 프리팹을 찾으므로 해당 .meta 파일을 유지해 주세요.
3. Unity/Editor/BaekhoMapImporter.cs를 Assets/Baekho/Editor/BaekhoMapImporter.cs로, Unity/Runtime/BaekhoMapConnection.cs를 Assets/Baekho/Runtime/BaekhoMapConnection.cs로 모두 복사합니다. 두 파일을 설치한 뒤 Unity 컴파일을 기다려 주세요. 이미 설치했다면 같은 파일을 업데이트하고 다른 폴더에 중복 복사하지 마세요.
4. Map.baekhomap.json을 Assets 안의 원하는 폴더로 복사합니다.
5. Unity의 컴파일이 끝나면 Project 창에서 Map.baekhomap.json을 선택하고 Assets > Baekho > Build Prefab 메뉴를 실행합니다.
6. JSON 파일과 같은 폴더에 맵 이름의 .prefab이 자동으로 생성되고 선택됩니다. 같은 이름이 있으면 번호가 붙은 새 파일을 만들며 기존 프리팹을 덮어쓰지 않습니다.

ZIP에는 배치 데이터와 가져오기 스크립트가 포함됩니다. 원본 프리팹 사본은 포함되지 않습니다.
좌표 단위는 미터이며 Y가 위쪽, X/Z가 격자 평면입니다. 맵의 중심은 원점입니다.
연결문은 BaekhoMapConnection 컴포넌트가 붙은 모듈 로컬 연결점으로 생성됩니다.
콜라이더, 내비게이션 베이크, 게임 로직, 조명과 에셋 크기 조정은 Unity 프로젝트에서 구성해 주세요.

blueprint.png는 참고용 AI 설계도 원본입니다.

바닥 두께·오브젝트 높이 (내보내기 형식 버전 3)
- 셀 높이는 바닥 윗면과 벽·문·소품 아랫면의 기준입니다. Unity에서 원본 프리팹의 경계를 측정해 해당 면을 맞춥니다.
- 바닥 두께는 셀 간격 × 12% × 두께 배율 Y입니다. 셀 간격 2m와 배율 1에서는 0.24m이며 바닥 윗면 아래로 두께를 확보합니다. X/Z 배율은 기존과 같습니다.
- Y 오프셋은 맞춰진 기준면을 추가 이동합니다. 바닥과 오브젝트에 서로 다른 Y 오프셋을 주면 그만큼 높이가 달라집니다. 계단은 기존 저단 기준점과 rise / stairNativeRise 계산을 유지합니다.
- 기존 맵에 적용하려면 새 앱에서 저장한 .baekho.json을 열어 ZIP을 다시 내보내세요. ZIP의 두 스크립트를 Unity의 같은 설치 위치에 업데이트하고 새 JSON으로 Build Prefab을 다시 실행하세요. 기존 프로젝트의 배율·오프셋 값은 유지되며, 이미 생성된 프리팹은 자동으로 변경되지 않습니다.
- 이전 내보내기 형식 버전 1/2는 기존 위치·배율 방식으로 가져옵니다. 새 높이·두께 보정은 이 ZIP의 형식 버전 3 JSON에 적용됩니다.

연결문·에셋 선택
- Unity/Runtime/BaekhoMapConnection.cs도 Assets/Baekho/Runtime/BaekhoMapConnection.cs에 복사해야 합니다. 기존 파일은 같은 위치에서 업데이트해 주세요.
- 연결문은 셀의 바깥쪽 가장자리, 셀 높이에 배치됩니다. 로컬 +Z가 모듈 바깥을 가리킵니다. 런타임 컴포넌트의 id/label/kind/width/height를 사용해 다른 모듈의 연결문을 맞출 수 있습니다. 자동으로 다른 맵을 생성하거나 연결하지는 않습니다.
- 연결문 마커의 위치는 에셋 offset과 무관합니다. 문 프리팹에는 가장자리 위치 + world offset, 외향 방향 + rotationY가 적용됩니다.
- 동일 역할에 여러 에셋이 있으면 셀 지정이 우선합니다. 지정하지 않은 셀은 등록 순서 기준 (row * map.width + x) % 해당 역할 에셋 수로 선택합니다.

높이·계단 설정
- 각 셀의 높이는 미터 단위이며 프리팹의 Y 배치에 반영됩니다. 같은 X/Z 셀에 여러 층이 겹치는 구조는 지원하지 않습니다.
- R 타일의 높이는 계단 저단입니다. 상승 화살표 방향으로 이웃한 칸이 고단이며 rise만큼 올라갑니다. 방향 0도는 +Z(북쪽), 90도는 +X(동쪽), 180도는 -Z(남쪽), 270도는 -X(서쪽)입니다.
- 계단 원본은 저단 높이 0에서 -Z 쪽으로 진입하여 +Z 쪽 고단으로 올라가도록 준비합니다. Y 스케일 1일 때 실측한 원본 상승 높이를 Baekho의 stairNativeRise에 입력해 주세요. 내보내는 계단 Y 스케일은 rise / stairNativeRise로 자동 계산합니다.
- 계단의 X/Z 스케일은 사용자 설정입니다. 원본의 폭·깊이가 cellSize에 맞도록 scale과 offset을 조정해 주세요.
- maxStepHeight는 Baekho의 경로 검증 기준입니다. 캐릭터 컨트롤러, 충돌체와 NavMesh 설정도 확인해 주세요.
