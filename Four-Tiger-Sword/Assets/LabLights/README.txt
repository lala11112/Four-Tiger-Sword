연구실 벽부등 / 천장등 — Blender 모델 2종

기준: 1 Unity unit = 1m, 기존 연구실 v3 소품과 2m 기준 격자에 맞춘 최종 제작 치수.
이미지에서 측정한 실측값이 아니라 최신 벽 제어판(높이 1.21875m)에 맞춰 정한 디자인 치수입니다.
기존 v3의 1.5배 확대를 다시 적용하지 않습니다. 프리팹 Scale은 (1,1,1)입니다.

33 벽부등: 폭 0.50m × 높이 1.15m × 돌출 0.22m.
34 천장등: 길이 2.10m × 폭 0.60m × 두께 0.20m.

파일
- laboratory_lights.blend: 부품별 편집이 가능한 Blender 원본. 텍스처 포함.
- models_fbx: Unity용 FBX 2개. 모델당 하나의 메시, 5개 재질 슬롯.
- models_glb: 공유/미리보기용 GLB 2개. 재질/텍스처 포함.
- Unity/Laboratory_Lights_URP.unitypackage: Unity URP 프리팹, 재질, 조명, 충돌체와 배치 예제.
- previews: Blender 실제 모델 렌더. references: 승인된 컨셉 PNG.
- validation_report.json: Blender 기하와 FBX/GLB 재가져오기 검증.
- Unity/unity-validation.json: Unity 프리팹의 치수, 피벗, 재질, UV, 조명 검증.
- scripts/build_lights.py: Blender 재생성 소스. scripts/support를 함께 보관하세요.

배치
Unity에서 Assets/LabLights/Prefabs를 사용합니다. URP 17 / Unity 6000.3.21f1에서 제작했습니다.
벽부등: 피벗이 뒤쪽 부착면 중심입니다. Unity +Z 방향으로 빛과 본체가 돌출됩니다.
천장등: 피벗이 윗면 중심입니다. Unity -Y 방향으로 본체와 빛이 내려갑니다.
원본 Blender는 +Z가 위이며 벽부등은 -Y로, 천장등은 -Z로 발광면이 향합니다.
Blender 원본의 두 루트 위치는 보기 편한 전시 배치입니다. 내보낸 파일의 부착 피벗은 원점입니다.
원본에서 PREVIEW_ONLY_NOT_EXPORTED 컬렉션은 촬영용이며 모델 내보내기에 포함되지 않습니다.

발광과 실제 조명
청백색 확산판만 별도의 LAB_light_diffuser 발광 재질을 사용합니다.
Unity 프리팹에는 벽부등 Spot Light 1개, 천장등 Spot Light 2개가 있습니다.
기본값은 실시간 조명 / 그림자 끔입니다. 맵 밝기에 맞춰 Intensity와 Range를 조절하세요.
조명 수가 많아지는 맵에서는 사용자가 배치한 조명의 베이크/혼합 사용을 검토하세요.
BoxCollider는 외함 영역에만 있으며 바깥쪽 고정 탭까지 넓힌 직육면체가 아닙니다.

발광 마스크
emission_masks PNG: RGB 흰색, 발광 부품 알파 255, 나머지 알파 0.
UV0는 기존 공유 색상 텍스처, 두 번째 UV EmissionMaskUV는 마스크용입니다.
Unity Shader Graph UV1 / Mesh.uv2 / TEXCOORD1로 마스크 알파를 읽으세요.
URP/Lit 기본 Emission Map은 UV0이므로 이 별도 마스크를 직접 연결하지 않습니다.
기본 프리팹의 발광은 분리된 확산판 재질로 작동합니다.
UV2가 예약되어 있으므로 Generate Lightmap UVs는 꺼 두었습니다.
라이트맵용 UV가 필요하면 별도 채널 설계를 먼저 바꾸세요.

재생성
Blender 5.x: blender --background --factory-startup --python scripts/build_lights.py
기존 원본의 수동 편집을 보존하려면 다른 폴더에서 재생성하세요.
Unity 제작 스크립트는 BuildScripts에 있습니다. 다른 PC에서는 LightPackageBuilder.Output 경로를 조정하세요.
