산업용 양개 슬라이딩 도어 — Unity 미터 기준

요청 치수 (폭 × 높이 × 깊이)
- 닫힌 문짝 두 개의 합산 외곽: 4.0 × 3.0 × 0.8m
- 고정 프레임 외곽: 4.2 × 3.2 × 2.2m
- Unity 1유닛 = 1m. 프리팹 루트 Scale=(1,1,1), Rotation=(0,0,0).
- 원점은 프레임 바닥 중심입니다. 문짝 하단은 바닥에서 0.10m입니다.
- 4m는 문짝 한 개가 아니라 좌우 두 개를 닫았을 때의 전체 폭입니다.
- 중앙 틈 8mm를 포함해 합산 폭이 정확히 4m입니다. 한 짝 메시 폭은 1.996m입니다.

구성
- Frame: 고정 프레임
- Door_Left / Door_Right: 각각 장식까지 합친 단일 메시 오브젝트입니다.
- Blender에서 한 문짝당 15개 부품을 1개 오브젝트로 결합했습니다.
- Unity도 문짝 오브젝트에 메시·렌더러·충돌체가 직접 들어 있으며 자식 오브젝트가 없습니다.
- 전체 폭과 높이 차이에 맞춰 정면 프레임 폭은 약 10cm입니다.
- 사진의 두꺼운 정면 테두리 비율보다 사용자가 지정한 외곽 치수를 우선했습니다.
- 2.2m는 프레임의 앞뒤 깊이이며, 중심에 두께 0.8m 문짝이 배치됩니다.
- 프레임 양쪽에는 문짝이 통과하는 깊이 약 0.86m의 슬라이딩 통로가 있습니다.
- 문짝 하나당 좌우로 2.05m 이동합니다. 프레임 바깥으로 약 1.95m씩 이동하므로
  벽 안에 수납하려면 양쪽에 그만큼의 빈 공간을 확보하세요. 벽 수납함 자체는 포함하지 않습니다.
- 곡선 패널 홈과 중앙의 꺾인 패널 무늬는 실제 기하입니다. 전후면 모두 제작했습니다.

사용 파일
- industrial_sliding_door_4x3.blend: 좌우 문짝이 각각 하나로 합쳐진 원본, 텍스처 포함.
- models_fbx/35_industrial_sliding_door.fbx: Frame / Door_Left / Door_Right 3개 메시.
- models_glb/35_industrial_sliding_door.glb: 재질·텍스처 포함 GLB.
- Unity/Laboratory_Door_4x3_URP.unitypackage: Unity URP 프리팹과 개폐 스크립트·충돌체·예제.
- previews: Blender에서 실제 모델을 렌더한 정면·사선·열림 이미지.
- references/door-reference.png: 사용자가 제공한 원본 이미지.
- asset_manifest.json / validation_report.json: 명시 치수와 FBX/GLB 재가져오기 검사.
- Unity/unity-validation.json: Unity 치수·피벗·재질·충돌체 검사.
- Unity/playmode-validation.json: 실제 재생 중 개폐와 방향 전환 검사.
- single-mesh-validation.json: Blender 문짝의 단일 오브젝트 구조·치수·개폐 검사.

Unity 사용
Unity 6000.3.21f1 / URP 17.3에서 제작했습니다.
프리팹: Assets/LabDoor4x3/Prefabs/35_industrial_sliding_door.prefab
런타임 컴포넌트: LabDoor.SlidingDoor
- Open(), Close(), Toggle(), SetOpen(bool): 게임의 상호작용에서 호출.
- OpenAmount: 0=닫힘, 1=열림.
- 기본 전체 이동 시간: 1.2초. Inspector에서 travel과 duration 조정 가능.
- SetOpenImmediate(bool): 에디터 배치나 즉시 상태 변경용.
- 문짝에는 이동하는 BoxCollider 2개, 프레임에는 고정 BoxCollider 6개가 있습니다.
- 플레이어 입력, 자동 감지 트리거, 사운드, 접근 권한, 끼임 감지 로직은 별도 게임 로직에서 연결하세요.
- 발광하지 않는 중성 회색 금속 재질입니다. UV0은 색상, Unity UV2는 생성된 라이트맵용입니다.

Blender
Z가 위이며 문 앞면은 -Y입니다. Unity는 Y가 위입니다.
타임라인 1프레임=닫힘, 61프레임=열림, 121프레임=닫힘입니다.
FBX/GLB는 닫힌 정적 메시로 내보냈으며 Unity 개폐는 SlidingDoor가 처리합니다.
PREVIEW_ONLY 컬렉션의 카메라·조명은 내보낸 모델에 포함되지 않습니다.
재생성: Blender 5.x --background --factory-startup --python scripts/build_door.py
scripts/support를 함께 보관하세요. 직접 편집한 원본을 보존하려면 다른 폴더에서 재생성하세요.
BuildScripts/DoorPackageBuilder.cs의 Output 경로는 다른 PC에서 재제작할 때 수정하세요.
