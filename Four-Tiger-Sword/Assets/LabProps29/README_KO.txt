연구실 소품 27종 — Unity 6 URP v3

검증 환경: Unity 6000.3.21f1 / Universal Render Pipeline 17.3.0.
Assets > Import Package > Custom Package에서 Laboratory_Props_27_URP_2m_v3.unitypackage를 가져옵니다.
호환 경로 Assets/LabProps29/Prefabs의 프리팹을 사용하세요. 씬: Assets/LabProps29/Scenes/LabProps_2m_Demo.unity.

구성: FBX 27개, 프리팹 27개, 공유 URP 재질·텍스처, 발광 마스크 PNG 27개 및 대응표, 2m 격자 데모 씬.
15번 책상에 16·17번 모니터 두 개를 조립했습니다. 책상+모니터가 하나의 모델/프리팹이며 별도 16·17번은 배포하지 않습니다.
v2 사용자가 갱신할 때 기존 Models/16_small_monitor.fbx, Models/17_large_monitor.fbx와 같은 이름의 Prefabs 파일 두 개를 삭제하세요. unitypackage는 이전 파일을 자동 삭제하지 않습니다.
나머지 기존 에셋 GUID와 프리팹 경로는 유지합니다. 기존 버전은 별도 백업으로 보존했습니다.

1 Unity unit = 1m. v2의 개별 랙·터미널·드럼 보정 이후 모든 모델 크기를 1.5배 확대했습니다.
14번 홀로그램 프로젝터는 별도로 약 11% 키운 다음 1.5배 확대했습니다. 최종 폭 3.75m, 본체 높이 약 1.1848m, 표시 효과 포함 높이 약 2.6906m입니다.
최종 랙 폭 1.5m, 터미널 높이 2.025m, 드럼 높이 1.3125m입니다. 상세 치수는 Data/asset_manifest.json을 확인하세요.
일부 겹치는 면·표시판의 간격을 보정했습니다. 홀로그램은 별도 정적 메시이며 애니메이션 로직을 포함하지 않습니다.
데모의 격자는 2m 간격입니다. 모델은 프리팹 스케일 1로 사용하세요.

EmissionMasks: 각 모델용 1024×1024 PNG는 RGB 흰색, Alpha가 발광 면 255(1), 그 외 0인 이진 마스크입니다.
EmissionMaskUV는 두 번째 UV 채널입니다. Shader Graph에서는 UV1, C#에서는 Mesh.uv2, glTF에서는 TEXCOORD_1을 사용합니다. 기존 재질 UV0는 유지했습니다.
마스크는 추가 데이터이며 기존 URP/Lit 재질에 자동 연결하지 않습니다. 사용자 셰이더에서 UV1으로 마스크 Alpha를 샘플링해 emission을 제어하세요.
모델의 Generate Lightmap UVs는 꺼 두어 두 번째 UV를 보존하세요. 마스크 가져오기는 Linear(sRGB 끔), 입력 Alpha, Alpha Is Transparency 끔, Mip Map 끔, Uncompressed, Point, Clamp입니다.
발광 면이 없는 모델의 마스크 Alpha는 전체 0입니다. emission_mask_manifest.json에 모델·발광 파트·UV 타일 대응표가 있습니다.

화면 발광·경고표시 컷아웃·유리/액체/기포/홀로그램 투명 재질은 기존과 같이 연결되어 있습니다.
Blender 절차적 미세 범프, 충돌체, LOD, 게임 로직은 포함하지 않습니다. Built-in/HDRP 전용 재질은 포함하지 않습니다.
패키지는 Assets/LabProps29 내부 파일만 추가하며 프로젝트 렌더 파이프라인 설정이나 빌드 스크립트를 포함하지 않습니다.
08·09번은 포함하지 않습니다.
