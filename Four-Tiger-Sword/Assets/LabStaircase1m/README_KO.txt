산업용 계단 1m — Unity 6 URP 추가 패키지

검증 환경: Unity 6000.3.21f1 / URP 17.3.0. Assets > Import Package > Custom Package에서 Staircase_1m_URP.unitypackage를 가져옵니다.
단일 프리팹: Assets/LabStaircase1m/Prefabs/32_industrial_staircase_1m.prefab.
정확한 외곽 치수는 폭 1m × 깊이 1m × 높이 1m입니다. 1 Unity unit = 1m이며 프리팹 루트 스케일은 (1,1,1)입니다.
바닥 발자국 중앙이 피벗입니다. 계단은 +Z로 올라갑니다. 5단이며 각 단의 높이와 깊이는 0.2m입니다.
동일 모듈을 (0,1,1)m 간격으로 반복 배치하면 연결됩니다. 예제 씬: Scenes/Staircase_1m_Stacked_Demo.unity (2개 연결, 1m 격자와 높이 기준).
각 단의 턱에 걸리지 않도록 WalkableRamp 자식에 단일 Convex MeshCollider를 적용했습니다. 폭·깊이·높이는 1m이며, 입구 높이 0.005m에서 정상 1m로 이어지는 약 44.86도 경사입니다. 콜라이더는 Ground 레이어를 사용하며, (0,1,1)m 연속 배치로 오르내리기를 검증했습니다.

재질·텍스처는 이 폴더 안에 독립적으로 포함되며 기존 LabProps29 파일을 수정하지 않습니다. URP 프로젝트에 가져오세요.
모델의 기존 UV0와 추가 EmissionMaskUV를 보존했습니다. 추가 UV는 Shader Graph UV1 / C# Mesh.uv2 / glTF TEXCOORD_1입니다.
EmissionMasks의 PNG는 RGB 흰색, Alpha 전체 0입니다. 이 계단에는 발광 파트가 없습니다. 마스크는 재질에 자동 연결하지 않았습니다.
마스크 가져오기: Linear(sRGB 끔), 입력 Alpha, Alpha Is Transparency 끔, Mip Map 끔, Uncompressed, Point, Clamp.
Generate Lightmap UVs를 켜서 두 번째 UV를 덮어쓰지 마세요. 게임 캐릭터·애니메이션·LOD는 포함하지 않습니다.
