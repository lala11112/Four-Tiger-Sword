# 연구실 공통 비주얼 설정

이 구성은 기존 연구실과 캐릭터 디자인을 유지하면서 재질의 빛 반응, 공간의 접촉감, 화면 색조를 맞추는 시범 적용이다. 대표 씬은 `Assets/_Project/Scenes/Lab Visual Pilot.unity`, 비교 기준 씬은 `Assets/_Project/Scenes/Lab Visual Baseline.unity`다. 기존 `Game Scene.unity`와 원본 프리팹·머티리얼을 직접 변경하지 않는다. 프로젝트 전체에 자동 적용되는 설정은 아니다.

## 확인한 환경과 주요 원인

| 항목 | 프로젝트에서 확인한 상태 |
| --- | --- |
| Unity | 6000.3.10f1 |
| 렌더 파이프라인 | Universal Render Pipeline 17.3.0 |
| 설치 셰이더 | All In 1 3D Shader 2.74, `Assets/Plugins/AllIn13DShader/Editor/Constants.cs` 기준 |
| 활성 빌드 대상 | Windows64 |
| 품질 | PC 활성, Mobile 품질과 전용 URP 에셋이 별도로 존재 |
| 맵 구조 | `BaekhoDungeonGenerator`가 연구실 모듈과 통로를 생성하고 encounter 준비 단계에서 추가 오브젝트가 배치됨 |

LabProps29, LabStaircase1m, PipeCable 계열은 이미 AllIn1로 바뀌어 있지만 대부분 `_ShadingModel=0`(Basic), `_SpecularModel=0`(None), `_Reflections=0`(None)이었다. 저장된 Metallic·Smoothness 수치가 실질적인 표면 차이를 만들지 못해 금속, 도장면, 고무가 비슷하게 보이는 상태였다.

PipeCable 재질에는 이전 URP의 `_BumpMap`, `_MetallicGlossMap` 텍스처 연결이 남아 있었다. 반면 AllIn1의 normal 기능은 꺼져 있고 `_NormalMap`은 vendor의 `NormalMapDefault.png`, `_MetallicMap`은 비어 있었다. 기존 텍스처를 복구하면 새 모델링이나 텍스처 교체 없이 재질 특성을 되살릴 수 있다.

실제 `Player_UserFinal_20261012_182802` 캐릭터는 AllIn1 Outline + ToonRamp + Basic을 사용한다. 다른 테스트 캐릭터의 Toon/PBR 설정을 이를 위한 기준으로 혼동하지 않는다. 시범 구성은 캐릭터의 ramp, 얼굴 음영, 기존 outline, 색상, 텍스처를 유지한다.

벽과 통로 벽은 `Assets/Baekho/Shaders/BaekhoWorldTriplanarLit.shadergraph`를 사용한다. 셰이더를 일괄 변환하면 월드 공간 텍스처 크기와 투영 규칙이 달라질 수 있어 그대로 유지한다.

기존 PC renderer도 **Forward+**였다. 설치된 URP의 `RenderingMode` enum은 Forward=0, ForwardPlus=2, Deferred=1, DeferredPlus=3이며 기존 `m_RenderingMode=2`를 소스와 대조해 확인했다. 기존 SSAO도 활성화되어 있었고 intensity 0.4, Before Opaque, Direct Lighting Strength 0.25였다. 따라서 이 작업의 원인은 Deferred와 AllIn1 pass의 비호환이나 SSAO의 부재가 아니다. 시범 renderer는 Forward+를 유지하면서 SSAO를 After Opaque, intensity 0.85로 조정해 적용 방식을 분리했다. 동일 카메라 비교에서 접촉부 명암이 더 읽기 쉽게 나타나는 것을 확인했다.

일부 화면·숫자·그리드의 발광에는 `_EmissionSelfGlow=20`이 중복 적용되어 있었다. AllIn1의 실제 발광 계산은 `EmissionMap × EmissionColor × EmissionSelfGlow`이므로 화면 전체 Bloom을 높이기 전에 이 추가 배율을 정리한다.

## 적용 범위와 원본 보존

- `LabVisualStyle.asset`에는 원본과 복제본의 **정확한 에셋 참조 쌍 51개**를 등록했다. 머티리얼 이름이나 폴더 이름으로 런타임에서 추정하지 않는다.
- 각 복제본은 `new Material(source)`로 만들며 원본의 색상, 텍스처 연결, UV scale/offset, 렌더 상태와 기존 효과를 가져온다. 의도적으로 수정하는 값은 아래 표의 음영·표면 설정과 활성 발광의 추가 배율이다.
- `_BumpMap`과 `_MetallicGlossMap`의 원래 참조도 보존한다. AllIn1의 대응 슬롯을 사용하지 않고 있을 때만 해당 원본 텍스처를 추가로 연결한다. normal importer 형식과 packed metallic 텍스처의 linear/data import를 확인하며 importer 자체를 수정하지 않는다. 이번 복제본에서 normal 16개와 metallic/smoothness map 16개를 복구했다.
- M_Door 등 투명·특수 depth/blend 표면 6개는 이번 자동 프리셋에서 제외했다. 유리·액체·홀로그램은 개별 검토가 필요하다.
- 공유 원본 머티리얼을 바꾸지 않으므로 다른 씬이나 원본 프리팹에 변경이 전파되지 않는다. 범위 안의 renderer가 가리키는 머티리얼만 복제본으로 연결한다.
- 원본 `PC_RPAsset.asset`의 renderer 목록에 새 renderer를 **index 1로 추가**했다. 기본 index 0과 기존 renderer 내용은 유지한다. PC 파이프라인의 다른 카메라는 명시적으로 override하지 않으면 기존 기본 renderer를 사용한다.
- Mobile URP 에셋, 렌더 파이프라인 종류, 패키지 버전은 변경하지 않는다.

실제 매핑의 기준은 `LabVisualStyle.asset`의 `materialReplacements`와 각 복제본의 `LabStyle_Source_<GUID>` label이다. 복제 파일명에도 원본 GUID 일부와 그룹을 붙여 동명 재질을 구분한다.

## 공통 재질 규칙

프리셋은 설치된 shader property와 keyword를 함께 설정한다. `_LightModel` 숫자만 바꾸고 관련 keyword를 그대로 두면 실제 렌더 결과와 inspector 값이 어긋날 수 있다.

| 그룹 | AllIn1 Light Model | `_Smoothness` | `_SpecularAtten` | `_ReflectionsAtten` |
| --- | --- | ---: | ---: | ---: |
| Character | 기존 Toon/ToonRamp 및 shading 유지 | 기존 값 | 기존 값 | 기존 값 |
| Floor | Classic, `_LightModel=1` | 0.22 | 0.25 | 0.15 |
| Wall | Classic, `_LightModel=1`; 기존 triplanar 셰이더는 유지 | 0.22 | AllIn1일 때 0.25 | AllIn1일 때 0.15 |
| PaintedSurface | Toon, `_LightModel=2` | 0.28 | 0.25 | 0.15 |
| Rubber | Toon, `_LightModel=2` | 0.18 | 0.12 | 0.15 |
| Metal | Toon, `_LightModel=2` | 0.42 | 0.25 | 0.30 |
| Prop | Toon, `_LightModel=2` | 0.28 | 0.25 | 0.15 |
| Emissive | Toon, `_LightModel=2`; 원래 발광 마스크 유지 | 0.30 | 0.25 | 0.15 |

AllIn1 환경 복제본의 공통 설정:

- Toon 그룹: `_ToonCutoff=0.35`, `_ToonSmoothness=0.35`, `_LIGHTMODEL_TOON`. 급격한 2단 명암 대신 넓은 전이를 사용한다.
- Floor/Wall: `_LIGHTMODEL_CLASSIC`. 반복되는 넓은 평면의 명암 경계가 띠처럼 보이지 않도록 한다.
- `_ShadingModel=1`, `_SHADINGMODEL_PBR`. 기존 `_Metallic` 값은 유지한다. 금속과 도장·고무를 일괄 동일 Metallic으로 만들지 않는다.
- `_SpecularModel=1`, `_SPECULARMODEL_CLASSIC`; `_Reflections=1`, `_REFLECTIONS_CLASSIC`. 반사 형태를 유지하면서 위 표의 강도로 제한한다.
- 복구한 normal: `_NormalMap`, `_NormalMapEnabled=1`, `_NORMAL_MAP_ON`. 기존 `_BumpScale`을 확인할 수 있으면 `_NormalStrength`에 전달한다. 이미 활성화된 AllIn1 normal은 교체하지 않는다.
- 복구한 metallic/smoothness: `_MetallicMap`, `_MetallicMapOn=1`, `_METALLIC_MAP_ON`. red=metallic, alpha=smoothness이며 원본과 동일한 채널 의미를 사용한다.
- 활성 발광의 RGB가 검정이 아니고 원래 `_EmissionSelfGlow>1`일 때만 추가 배율을 최대 1.5로 제한한다. `_EmissionColor`와 `_EmissionMap`은 유지한다. 검정 발광 색상의 비발광 표면은 이 값을 바꾸지 않는다.
- `_HalfLambertWrap`, `_ToonRamp`, `_CustomAmbientColor`는 공통 프리셋에서 강제하지 않는다. outline·vertex animation·fade 효과를 새로 켜지 않는다.

URP/Lit 재질은 셰이더와 기존 Metallic을 유지하고 그룹별 `_Smoothness`만 조정한다. 기존 world triplanar 재질은 같은 셰이더와 `_BaseMap`, `_BaseColor`, `_TextureSizeMeters`를 유지하며 Wall의 `_Smoothness=0.22`, `_Metallic=0`을 사용한다.

AllIn1의 custom shadow 기능은 per-material `_ShadowColor`가 아니라 전역 `global_shadowColor`를 쓰며 vendor `ShadowsConfigurator`가 이를 설정한다. 이번 구성은 이를 새로 활성화하지 않고 공통 환경광과 Volume의 미세한 그림자 색조를 사용한다.

## 조명과 공간의 연결감

`LabSceneTools.ApplyLightingToActiveScene()`이 만드는 `Lab Visual Lighting` 오브젝트에 Global Volume과 낮은 강도의 방향광을 배치한다.

| 항목 | 값 |
| --- | --- |
| Ambient Mode | Trilight |
| Sky / Equator / Ground RGB | (0.55, 0.60, 0.67) / (0.46, 0.50, 0.56) / (0.25, 0.28, 0.34) |
| Ambient Intensity / Reflection Intensity | 1.0 / 0.7 |
| Directional RGB / Intensity | (0.90, 0.95, 1.0) / 0.35 |
| Directional Rotation | (55, -30, 0) degrees |
| Directional Shadows | Realtime Soft, strength 0.8, bias 0.035, normal bias 0.25 |
| 기존 Point/Spot Intensity | 생성 범위 안에서 원래 값의 0.7배, 별도 최대 강도 제한 없음 |
| Local Shadow Budget | 원래 그림자가 있던 조명 중 카메라에서 25m 이내 가까운 최대 4개, 0.5초마다 갱신 |

`LabLocalShadowBudget`은 원래 `LightShadows.None`인 조명에 그림자를 추가하지 않는다. 활성 URP가 additional-light shadows를 지원하지 않으면 해당 로컬 그림자를 사용하지 않는다. 조명 방향, 색상, range, 렌더링 layer는 변경하지 않는다. 이 budget은 ray-traced contact shadow 기능이 아니다. 근거리 그림자와 SSAO가 함께 바닥 접촉감을 보완한다.

맵은 런타임 생성 구조이므로 lightmap이나 APV bake를 전제로 바꾸지 않았다. 새로운 Reflection Probe도 추가하지 않았다. 현재 환경 반사와 기존 probe 수신 경로를 사용하며, 이후 방별 probe가 필요하면 생성 완료 후 1회 capture와 메모리·시간 비용을 따로 검토한다. 매 프레임 realtime probe capture는 기본 규칙으로 삼지 않는다.

## 시범 카메라와 SSAO

`LabForwardRenderer.asset`은 기존 renderer와 동일한 **Forward+**를 사용하고 기존 opaque/transparent outline renderer feature를 유지한다. 카메라에 SSAO 설정을 따로 적용하기 위한 복제본이며 렌더 모드를 전환한 변경이 아니다. vendor 셰이더 pass나 URP 패키지 코드는 수정하지 않았다.

| SSAO 항목 | 시범 값 |
| --- | --- |
| Source | DepthNormals |
| After Opaque | true |
| Intensity / Radius | 0.85 / 0.35 |
| Downsample | true |
| Samples / Normal Quality | Medium / Medium |
| Direct Lighting Strength | 0.2 |

After Opaque는 완성된 opaque 색상에 AO를 적용하는 방식이다. shader 내부의 AO와 중복해서 과도하게 어둡게 만드는지와 발광 장치의 읽기 쉬움은 동일 카메라 비교로 확인한다. 전용 Screen Space Shadows나 추가 contact-shadow 효과를 새로 도입한 구성은 아니다.

시범 카메라의 `LabCameraRendererOverride`는 지정한 PC 파이프라인에서만 새 renderer를 찾는다. Mobile 등 다른 품질 파이프라인으로 전환하면 renderer index -1, 즉 해당 파이프라인의 기본값을 사용한다. PC 기본 renderer를 통째로 교체하는 방식은 사용하지 않는다.

## Color Grading과 Post Processing

`LabVolumeProfile.asset`의 Global Volume은 priority 10, weight 1이다. 대상 카메라의 post processing을 켜고 해당 Volume layer가 포함되게 설정한다.

| 효과 | 값 |
| --- | --- |
| Tonemapping | Neutral |
| Post Exposure / Contrast / Saturation | +0.1 / +8 / -4 |
| White Balance Temperature / Tint | -4 / 0 |
| Shadows RGB | (0.98, 1.0, 1.025), 추가 exposure 0 |
| Midtones / Highlights | (1, 1, 1), 추가 exposure 0 |
| Shadow Range / Highlight Range | 0-0.3 / 0.6-1.0 |
| Bloom Intensity / Threshold / Scatter | 0.18 / 1.15 / 0.45 |
| Bloom High Quality Filtering | false |
| Vignette Intensity | 0.12 |

차가운 청회색 그림자를 작은 보정으로 만들며 캐릭터의 색을 재칠하지 않는다. Bloom은 발광 영역의 부드러운 번짐에 사용한다. Depth of Field, Motion Blur, Chromatic Aberration 등의 효과는 새로 추가하지 않았다.

## 다른 씬이나 새 오브젝트에 재사용

1. 새 씬 복사본이나 검토할 씬을 열고 대상 머티리얼의 shader, 투명도, 공유 범위를 확인한다. UI, VFX, death fade, 유리·홀로그램에는 환경 프리셋을 일괄 적용하지 않는다.
2. Project에서 명시적으로 선택한 머티리얼에 `Assets > Four Tiger > Rendering > Create Lab Style Variant > 그룹` 메뉴를 사용한다. `Materials` 폴더에 새 파일을 만들며 기존 파일을 덮어쓰지 않는다. 캐릭터는 기존 룩을 먼저 보존한다.
3. `Create > Four Tiger Sword > Rendering > Lab Visual Style`로 스타일을 만들거나 시범 스타일을 복제하고 `materialReplacements`에 정확한 source/replacement 쌍을 등록한다. 같은 source의 중복 항목은 첫 항목을 사용한다.
4. 생성기 오브젝트에 `LabVisualStyleController`를 붙여 generator와 style을 연결한다. 정적 씬은 `explicitRoot`로 적용 범위를 지정한다. generator의 `CurrentRoot`가 있으면 그것을 우선한다. 루트 밖의 캐릭터와 다른 씬 오브젝트는 변경 대상이 아니다.
5. `Tools > Four Tiger Sword > Lab Visuals > Apply Lighting To Active Scene`을 필요할 때 명시적으로 실행한다. 이 메뉴는 현재 씬의 RenderSettings와 카메라 post processing을 수정하고 씬을 dirty 상태로 만든다. `LabVolumeProfile.asset`이 이미 있으면 새로 덮어쓰지 않으므로 이후 값 변경은 해당 profile에서 한다.
6. 필요하면 `LabLocalShadowBudget`에 generator/explicitRoot와 카메라를 연결한다. 시범 씬의 4개·25m·0.5초를 시작점으로 삼되 목표 기기에서 측정한다.
7. PC에서 시범 SSAO 설정이 필요한 카메라에만 `LabCameraRendererOverride`를 붙이고 PC pipeline 및 `LabForwardRenderer`를 지정한다. 다른 pipeline 목록에 같은 index가 있다고 가정하지 않는다.
8. 런타임에서는 생성 완료와 encounter 준비 완료 이벤트에 맞춰 머티리얼과 조명을 적용한다. 임의로 늦게 생성한 별도 renderer는 `ApplyCurrent()`를 명시적으로 다시 호출한다. 매 프레임 전체 renderer를 검색하는 구성은 아니다.
9. Edit Mode의 임시 확인은 컴포넌트 context menu `Apply Current Lab Style (Scoped Preview)`를 사용한다. 정식 scene asset에 preview 참조를 저장하기 전에 `Restore Original Lab Materials And Lights`로 되돌린다. 컴포넌트 비활성화·제거 시에도 추적한 원래 참조와 강도를 복구한다.

이미 적용한 조명에 0.7을 반복 곱하지 않는다. 외부 death fade가 머티리얼을 바꾸거나 게임 효과가 조명 강도를 바꾸면 controller는 그 변경을 덮어쓰지 않는다. 새 에셋을 추가할 때도 원본 색상·텍스처·재질 종류를 먼저 확인하고 그룹을 선택한다. 빨강은 기존 경고·상호작용 강조 용도로 제한하며 공통 shader 설정을 위해 텍스처를 다시 칠하지 않는다.

## 핵심 파일

| 파일 | 역할 |
| --- | --- |
| `Assets/_Project/Scenes/Lab Visual Pilot.unity` | 시범 적용 씬 |
| `Assets/_Project/Scenes/Lab Visual Baseline.unity` | 원래 비주얼 snapshot 기준 씬, 같은 생성 설정과 seed |
| `LabVisualStyle.asset` / `Materials/*.mat` | 51개 정확한 원본-복제 매핑 / 재질 복제본 |
| `MaterialManifest.csv` | 51개 원본·복제본 경로, 그룹, 기존 사용 범위 기록 |
| `LabDungeonSettings.asset` | 시범 생성용 설정 복제본, 고정 seed 12345 |
| `LabForwardRenderer.asset` | 전용 Forward+ renderer, SSAO와 기존 outline feature |
| `LabVolumeProfile.asset` | 절제된 grading·Bloom·Vignette |
| `Runtime/LabVisualStyle.cs` | 매핑과 로컬 조명 강도 규칙 |
| `Runtime/LabVisualStyleController.cs` | 생성 범위 적용, 이벤트 연결, 복원 |
| `Runtime/LabLocalShadowBudget.cs` | 근거리 로컬 그림자 budget |
| `Runtime/LabCameraRendererOverride.cs` | PC 카메라만 renderer 선택, 다른 품질 fallback |
| `Editor/LabMaterialPresets.cs` | 원본을 복제하는 재질 authoring 도구 |
| `Editor/LabSceneTools.cs` | 명시적인 조명·Volume 구성 메뉴 |
| `Editor/LabVisualValidation.cs` | 범위·매핑·복원·외부 override 보존 검증 |
| `Assets/_Project/Settings/PC_RPAsset.asset` | 기존 파일의 renderer 목록에 index 1 추가, 기본값 0 유지 |

## 검증과 성능 기록

검증 완료 항목과 남은 범위를 구분한다. 측정하지 않은 프레임 성능이나 전투 중 시인성을 정지 화면으로 확정하지 않는다.

- [x] Scoped Overrides 검증: 정확한 매핑, 범위 밖 보존, 지연 생성 renderer, 반복 호출, 복원, 외부 death/light override, 원본 머티리얼의 색상·텍스처·UV 보존 PASS.
- [x] 51개 복제본의 불변조건 검사 PASS. 원본 색상·기존 텍스처 연결·UV를 유지하고 의도한 활성 슬롯 복구만 허용했다. normal 16개, metallic/smoothness map 16개 복구 확인.
- [x] 원본 62개 파일의 hash 비교: 61개 동일, 변경된 1개는 renderer 목록만 추가한 `PC_RPAsset.asset`이다.
- [x] Baseline/Pilot 씬을 저장하고 생성기 prefab override의 저장 상태를 확인했다. 두 씬은 동일한 `LabDungeonSettings.asset`의 seed 12345를 사용한다.
- [x] 동일 카메라의 `before_lab.png` / `after_lab.png`, `before_detail.png` / `after_detail.png` 비교. 바닥과 기계 접촉부의 명암 강화, 발광 화면의 내용 보존을 확인했다.
- [x] 로컬 그림자 budget, 원래 그림자가 없는 조명의 보존, 복원 동작 검증 PASS.
- [x] PC에서 전용 renderer 선택, 다른 품질 pipeline의 기본 renderer fallback 검증 PASS. 실제 모바일 기기에서의 성능 검증은 별개다.
- [x] Baseline에서도 seed 12345 맵 생성 성공과 encounter Ready를 확인했다. Pilot에서 본 NavMesh/Animator 오류가 Baseline에서도 재현되어 기존 문제로 분리했다. 이 작업에서 게임플레이 오류를 수정하지 않았다.
- [x] 마지막 authoring 메뉴 수정 후 Unity script compile 완료, console error 0개. Scoped Overrides와 shadow budget 검증 재실행 PASS.
- [x] 최종 Pilot Play Mode: seed 12345, 생성 success=true, encounter Ready=true, 3,855 renderer 중 3,735개에 매핑 적용, 로컬 조명 154개 보정, 로컬 shadow 4개, 전용 renderer index 1 확인. 약 76초 실행 기록은 기능 검증이며 성능 benchmark가 아니다.
- [ ] 캐릭터 얼굴·머리 close-up, 이동·공격·피격·전투 중 시인성, 전체 생성 맵에 대한 검증.
- [ ] 실제 목표 기기의 CPU/GPU frame time, draw/SetPass, overdraw, shadow atlas, SSAO 시간 측정.

비교 자료의 보관 위치는 Unity 프로젝트 루트의 `VisualReview/20261022`이다. 원래 상태는 `Lab Visual Baseline.unity`, 개선 상태는 `Lab Visual Pilot.unity`에서 같은 seed와 카메라 조건으로 다시 확인할 수 있다. 조명과 grading은 변경의 일부이므로 두 상태의 설정은 위 표에 기록한 차이를 포함한다.

캡처는 1920×1080, perspective FOV 60(전체) / 55(장치 상세)이다. 전체 화면 카메라 위치는 (37, 1.4, -48.5), 바라보는 점은 (31.8, -0.1, -55.7)이다. 상세 화면은 위치 (43, 1, -43), 바라보는 점 (47, -0.5, -49)이다. 전체 화면의 캐릭터 임시 포즈는 위치 (34, -1.1, -53), Y 회전 210도이며, 촬영 후 원래 씬의 시작 위치로 복구했다. 두 쌍은 같은 카메라·지오메트리·해상도에서 촬영했으며, 실시간 파티클의 시점은 고정하지 않아 입자 위치가 조금 다르다. 임시 촬영 카메라와 생성된 preview 지오메트리는 씬에 저장하지 않았다.

- [전체 변경 전](../../../../VisualReview/20261022/before_lab.png) / [전체 변경 후](../../../../VisualReview/20261022/after_lab.png)
- [장치 변경 전](../../../../VisualReview/20261022/before_detail.png) / [장치 변경 후](../../../../VisualReview/20261022/after_detail.png)
- `VisualReview/20261022/ChangedFiles.txt`: 이번 작업에서 추가하거나 수정한 Unity 파일의 전체 목록(메타 포함).

Baseline에서도 재현된 기존 런타임 오류는 valid NavMesh 부재, Animator state 부재, layer index -1이다. 최종 컴파일의 error 0개와 런타임 전체 오류 부재를 혼동하지 않는다. 로컬 shadow budget은 생성 완료 시 fixture를 수집한다. 같은 root 안에 조명을 나중에 추가하는 시스템에는 `RestoreOriginals()` 후 `ApplyCurrent()`로 재수집하는 연결이 필요하며, 거리 순위가 바뀔 때 그림자 전환이 눈에 띄는지도 실제 이동 중 확인한다. 카메라 renderer override는 비활성화 시 기본 index -1로 복구하므로 별도 custom renderer를 사용하던 카메라에 추가할 때는 그 설정을 별도로 보존해야 한다.

PBR·normal·metallic map 복원은 Basic보다 연산과 texture sample을 늘린다. 51개 머티리얼 복제본은 기존 texture asset을 참조하므로 원본 텍스처 파일 자체를 복제하지 않는다. 기존과 시범 renderer 모두 Forward+이며 성능 비교에서 렌더 모드 전환의 효과를 주장하지 않는다. SSAO는 downsample과 medium sample을 사용하고 로컬 shadow는 수량을 제한했다. Mobile/저사양용 preset은 실제 기기 측정 후 반사·shadow budget·SSAO를 개별 조정한다.

## 근거

프로퍼티 이름, enum 순서, keyword, SSAO 수신과 normals pass는 설치된 `Plugins/AllIn13DShader/Shaders/Generic Shaders/AllIn13DShader.shader`, `Shaders/ShaderLibrary/AllIn13DShaderLight.hlsl`, `AllIn13DShader_BRDF.hlsl`, `AllIn13DShaderHelper_URP.hlsl`, `AllIn13DShader_URP_DepthNormalsPass.hlsl`을 확인했다. 렌더 모드는 설치된 URP 17.3 `UniversalRenderer.cs`의 `RenderingMode` enum을 확인했으며 SSAO 동작은 `ScreenSpaceAmbientOcclusionPass.cs`와 실제 비교 캡처를 기준으로 기록했다.

vendor의 로컬 `Documentation.pdf`는 온라인 문서 연결 안내다. 참고: [Light Models](https://seasidestudios.gitbook.io/seaside-studios/3d-shader/light-models), [Effects List](https://seasidestudios.gitbook.io/seaside-studios/3d-shader/effects-list). 온라인 문서는 갱신될 수 있으므로 구현의 기준은 프로젝트에 설치된 2.74 소스다.
