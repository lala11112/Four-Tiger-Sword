캐릭터 Unity 패키지 사용 안내

확인한 Unity 버전: 6000.3.21f1 (Unity 6.3)
재질: Built-in Render Pipeline의 Standard 셰이더

가져오기
1. Unity 프로젝트에서 character_unity_humanoid.unitypackage를 열고 전체 항목을 Import합니다.
2. Assets/Character_Humanoid.prefab을 씬에 배치합니다.
3. 프리팹의 Animator에 사용할 Humanoid 애니메이션 컨트롤러를 연결합니다.

포함된 동작 확인하기
Animator의 Controller에 Assets/FullBody_Rig_Check.controller를 연결한 뒤 Play를 누릅니다.
약 34초 동안 팔·다리·몸통·목 동작을 확인할 수 있습니다. 시작과 끝은 A 자세입니다.

포함 파일
- Character.fbx: Humanoid Avatar와 스킨 메시가 포함된 캐릭터 모델
- Character_Humanoid.prefab: 캐릭터와 상완 비틀림 보정용 Unity 기본 제약
- Unity_Import_Materials, Unity_Import_Textures: 재질과 텍스처
- FullBody_Rig_Check.anim, FullBody_Rig_Check.controller: 전신 확인 동작
- Character_Rig_Check.fbx: 동작 교환용 원본

사용 메모
- 실제 사용에는 Character_Humanoid.prefab을 배치하세요. 모델 FBX만 배치하면 프리팹에 포함된 상완 비틀림 보정이 적용되지 않습니다.
- URP/HDRP 프로젝트에서는 해당 렌더링 방식에 맞게 재질을 변환해야 합니다.
- 옷·토시·리본에는 기본 스킨 가중치가 있습니다. Magica Cloth 2 플러그인과 물리 설정은 포함되어 있지 않습니다. 사용할 경우 받는 프로젝트에서 별도로 설치하고 설정하세요.
- Blender 편집 파일은 이 Unity 패키지에 포함되지 않습니다.
