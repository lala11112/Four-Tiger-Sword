ARPG 저작형 환경 V3 — 사용법과 검증 범위
Unity 6000.3.21f1 / URP 17.3.0
최종 네이티브 정적 검증: 22개 방 통과.

1. 구성
원본 프리팹 22개를 GUID별로 구분한 방 세트다. V2에서 재구성한 격자·바닥·다리·계단·전체 높이 벽·활성 천장과 기존 LabDoor4x3 V2 문의 배치를 기초 셸로 유지하고, 외관 구성을 새로 저작한다. 원본에 없던 메시를 정확히 복구했다는 뜻은 아니다. 기존 정규화 증거와 건축 가정은 SourceData와 manifest에 남긴다. 원본 패키지와 고정된 V2 배포본은 수정하지 않는다.

방 전체의 경계 슬롯에 같은 소품을 반복하거나 반대편 벽을 그대로 복제하지 않는다. 방마다 1~2개의 큰 내부 작업 조립군과 가변적인 벽측 기능 구역을 명시한다. L/U/각진 배치, 장비 지지 구조, 침대와 갠트리, 연결 배관 랙과 탱크, 선반과 실제 적재물, 냉각 매니폴드 등을 기능 단위로 조합한다. 정확한 방별 기능과 대표 모델은 RoomCatalog22.txt에 기재했다.

실제 자산 경로·앵커·방향·스케일·부품 오프셋은 ArtDirection.json에 기록한다. 작은 셀에 맞추려고 장비를 자동 축소하지 않는다. 승인된 관제_전투실은 기존 장비 14개와 바닥/벽 외관을 보존하는 별도 도우미를 사용하며, 이를 새 V3 배치에 대한 사용자 승인으로 표현하지 않는다.

2. 배치와 보존 기준
장비의 정적 메시 경계를 실제 같은 높이의 바닥과 명시된 다리 면에 대조한다. 기존 문 접근부·계단 착지부·마커와 명시된 약 2m 주 이동 경로를 검사한다. 조립군 전체의 작은 위치 보정과 제한된 부품 보정을 기록한다. 랙 내부 탱크처럼 의도된 결합은 같은 조립군 안에서만 허용하며 선반/받침대의 Y 오프셋을 유지한다. 바닥 표식과 그릴 등 보행 가능한 얇은 표면은 마커 공간을 가로지를 수 있다.

필수 대표 장비가 배치 검사를 통과하지 못하면 미리보기에 남기고 requiredPlacementFailures/retainedForReview로 표시한다. 이 상태는 수정이 필요한 미완성 결과다. 보조 부품의 거부와 부분 조립군은 acceptedParts/rejectedParts 및 부품별 이유로 확인한다. 정적 검증 통과가 모든 저작 부품의 수락이나 시각 품질 승인을 뜻하지 않는다. uniqueAssetCounts, oppositeMirrorPairs, sameRowRuns는 반복/대칭/일렬 배치의 검토 지표이며 의도된 건축 반복의 자동 실패 조건은 아니다.

3. 자산 위치
- Assets/ARPGRoomSetV3/Prefabs/Rooms/: 전체 셸, 문, 천장과 외관이 함께 있는 방.
- Assets/ARPGRoomSetV3/Prefabs/Shells/: 장식을 분리한 건축 기초 셸.
- Assets/ARPGRoomSetV3/Prefabs/Overlays/: V3 외관만 있는 오버레이. 건축·문·게임 시스템을 대신하지 않는다.
- Assets/ARPGRoomSetV3/Scenes/*_Full.unity: 전체 벽과 활성 천장이 기준인 검토 장면.
- Assets/ARPGRoomSetV3/SourceData/ArtDirection.json: 명시적 조립군·기능 구역·주 경로.
- Assets/ARPGRoomSetV3/SourceData/<GUID>.json: 보존한 원본별 정규화 배치.
- Assets/ARPGRoomSetV3/Manifests/: 실제 배치 수락/거부·보정과 네이티브 정적 검증.
- Assets/ARPGRoomSetV3/Models/Assemblies/: 대형 기능 구조 모델.
- Assets/LabDoor4x3/: 기존 사용자 문과 SlidingDoor 런타임 코드.
공통 실험실 모델·재질은 원래의 LabProps29/ARPGEnvironmentArt 등의 경로로 의존 자산이 포함될 수 있다. V3 메시를 압축하여 불필요한 V2 전체 메시 덩어리를 배포하는 방식을 피한다.

4. 사용
배포 파일명: ARPG_Rooms_AuthoredV3_FullShell_URP.unitypackage
새 URP 테스트 프로젝트에 먼저 가져와 Rooms 프리팹 또는 *_Full 장면을 확인한다. 기존 사용자 자산과 같은 GUID가 있는 경우 Import 경로를 확인하고 실제 게임 프로젝트에 통합한다. 오버레이를 원본 방에 적용할 때는 건축 중복과 장비 겹침을 별도로 검토한다.

Layout 이미지는 일부 벽/천장을 일시적으로 숨긴 배치 검토용이다. 절개 이미지가 배포 기본 상태를 뜻하지 않는다. Rooms/Shells 프리팹과 *_Full 장면의 전체 벽·활성 천장이 기준이다. 콘셉트 이미지는 미술 방향 자료이며 실제 Unity 모델과 렌더의 검증 증거를 대신하지 않는다.

제작 C# 도구는 Editor 폴더에 둔다. SlidingDoor.cs는 런타임 코드이므로 Editor로 옮기지 않는다. 완성 프리팹을 사용하는 데 제작 메뉴 실행은 필요하지 않다. 공동 작업 프로젝트는 ../arpg-environment-phase1-3-v1/PreviewProject이며 V3 출력은 Assets/ARPGRoomSetV3다. 다른 개발 환경에서 이미지를 재생성할 때는 검토 도구의 출력 경로를 조정한다.

5. 원본과 게임 코드
다운로드 원본 Prefabs.unitypackage 및 V1 SourcePackage의 정확한 백업/추출본을 유지한다. 감사는 원본 asset/meta 137개를 바이트 비교하며 V3 정규화 SourceData도 고정된 V2 파생 데이터와 별도로 비교한다. 정규화 추론은 과밀_표본실 44개, 관측고지_전투실 1개, raised_arena_cutaway 502개다. 원시 Transform과 추론 이유는 SourceData에 보존한다.

원본 패키지에는 일부 Baekho 게임 모듈·마커·연결·fixture·StageMapAnchors 구현과 원본 메시가 없었다. V3가 이 게임 구현을 복원하거나 원본 적·보상·순간이동·방 연결·문 잠금·플레이어 이동·NavMesh·전투 회귀를 통합 완료한 것은 아니다. 배포된 V3 자산의 참조 폐쇄성 검사와 원본 게임 코드의 미포함은 구분한다. 원본 게임 코드가 없다는 이유로 깨진 스크립트가 붙은 프리팹을 배포하는 방식은 허용하지 않는다.

6. 검증 상태
최종 네이티브 정적 검증: 22개 방 통과.
기록 중인 네이티브 manifest: 22개 방, 수락 부품 654개, 거부 부품 0개, 필수 배치 실패 0개. 초안 모드에서는 이 숫자를 최종 승인 수치로 읽지 않는다.
최종 포함 목표: Rooms/Overlays/Shells/*_Full 장면 각각 22개.
원본 게임 통합·동적 플레이어 이동·NavMesh·전투 회귀는 미실시다.
최종 네이티브 증거: Assets/ARPGRoomSetV3/Manifests/validation.json
최종 배포 참조·포함 수·SHA256·원본 보존 증거: Analysis/final-package-audit.json
