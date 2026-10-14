# AutoHitTimer Unity Editor 패키지

Unity 6000.0 이상을 대상으로 하는 Editor 전용 패키지입니다. Unity 6000.3.10f1에서 설치·컴파일·EditMode 테스트·실제 FBX 촬영을 확인했습니다. 게임 빌드에 런타임 코드를 추가하지 않습니다.

## 연결 및 촬영

- 데스크톱 앱 설치 스크립트가 Unity Package Manager `Client.Add`로 이 패키지와 Newtonsoft JSON 3.2.2를 설치합니다.
- 열린 **저장된 씬**의 Animator를 GlobalObjectId로 선택합니다. 캐릭터 계층의 메시 무기를 선택하면 그 분기를 미리보기에서 활성화합니다. 선택하지 않으면 기존 활성 렌더러를 사용합니다.
- 검색 결과는 클립 GUID, 하위 에셋 ID 문자열, 이름, 길이, 프레임률, 호환 여부, 클립 변경 지문을 제공합니다. 같은 클립을 사용하는 서로 다른 Animator 상태는 각각 표시합니다.
- 외부 `.fbx`/`.anim`은 내용 해시를 붙인 이름으로 `Assets/AutoHitTimer/Imports`에 복사한 뒤 실제 임포트합니다. 단일 입력은 최대 512 MiB입니다.
- 촬영은 Transform·MeshFilter·MeshRenderer·SkinnedMeshRenderer만 새로 구성하고, 스크립트·물리·애니메이션 이벤트를 실행하지 않습니다. 별도 PreviewRenderUtility 장면에서 단일 클립 Playable을 평가합니다.
- 프레임마다 사선·측면 512×512 영상을 나란히 저장합니다. PNG와 `frames.json`은 `Library/AutoHitTimerBridge/media/<session>/<batch>`에 남습니다. 프레임 ID에 배치 UUID가 포함되어 24/60fps 결과를 안전하게 합칠 수 있습니다.
- Generic 본 경로 불일치, Humanoid Avatar 불일치, Legacy 클립과 렌더러 없는 캐릭터는 사유를 반환합니다. 촬영 시간은 속도 배율 적용 전 클립 초 단위입니다.

## 파일 프로토콜

`Library/AutoHitTimerBridge` 아래 `requests`, `processing`, `responses`, `receipts` 폴더를 사용합니다. 요청·응답 최대 크기는 각각 1 MiB입니다. 클라이언트는 임시 파일을 쓴 후 `<UUID>.json`으로 원자적으로 이동해야 합니다.

```json
{
  "protocolVersion": 1,
  "id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "command": "capture",
  "payloadJson": "{...}",
  "expiresUtcMs": 1800000000000
}
```

응답은 `{protocolVersion,id,ok,resultJson,error:{code,message}}`입니다. `status.json`에는 프로젝트 경로, Unity 버전, `ready`, `capturing`, `message`, `timestampUtcMs`가 기록됩니다.

| 명령 | payload |
|---|---|
| `status`, `actors` | `{}` |
| `search` | `{query,actorId}` |
| `import` | `{path,actorId}` |
| `capture` | `{actorId,clip:{guid,localId},fps:24 또는 60,times?:number[],sessionId?:UUID,weaponId?,weaponBone?}` |
| `cancel` | `{sessionId?}` 또는 `{requestId?}`; 생략하면 현재 촬영 취소 |
| `targets` | `{actorId,adapterId?}` |
| `previewChange`, `apply` | 프로젝트 어댑터 계약; `adapterId` 기본 `four-tiger-sword` |
| `saveStatus` | `{changeId,adapterId?}`; 저장 저널의 완료/미실행/롤백/복구 필요 상태를 읽기 전용 조회 |

캡처 응답은 `{sessionId,clip,duration,fps,frames:[{id,time,path}],mediaRoot}`입니다. 긴 클립은 클라이언트가 `times`를 나누어 요청하고 같은 세션 ID를 재사용합니다. 촬영당 7200프레임 및 메타데이터 크기 제한을 사전 검사합니다.

처리 전에 영수증을 저장합니다. 완료 요청은 같은 ID로 다시 받아도 기존 응답만 반환합니다. 재시작 당시 실행 중이던 요청은 `INTERRUPTED`로 표시하며 자동 재실행하지 않습니다. 촬영은 에디터 업데이트마다 한 프레임씩 처리하고 취소·컴파일·Play Mode 전환·종료 시 Playable과 미리보기 장면을 정리합니다. Windows 파일 교체는 같은 볼륨의 `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)`와 짧은 재시도를 사용합니다.

## 어댑터 확장

`IAutoHitTimerAdapter`의 `Id`, `Targets(actorId)`, `PreviewChange(request)`, `Apply(request)`, `SaveStatus(request)`를 구현하고 `AutoHitTimerAdapters.Register`로 등록합니다. `AutoHitTimerAssets.ResolveClip`, `ClipRef`, `StateReason`, `EnumerateStates`와 `AutoHitTimerScene.ResolveAnimator`를 공용으로 사용할 수 있습니다. 저장 트랜잭션·대상 충돌 검사·Undo는 프로젝트 어댑터의 책임입니다.

## 검증 기록과 재현

2026-10-09, Windows/Unity **6000.3.10f1**의 별도 `.test-unity/AutoHitTimerValidation` 프로젝트에서 검증했습니다. 원본 게임 프로젝트는 수정하지 않았습니다.

- 공용 NUnit **7/7 통과**: 시간 샘플링·범위 제한, 잘못된 envelope, 중복 적용 영수증, 상태 시간 호환성, 스크립트/물리 제외, 실제 두 시점의 1024×512 렌더·배치 ID·원본 Transform 보존.
- 실제 설치 스크립트 → `Client.Add` → Newtonsoft 의존성 → 프로젝트 어댑터 컴파일을 검증했습니다. 테스트 프레임워크도 Package Manager API로 설치했습니다.
- 원본 게임의 실제 공격 데이터 타입 9개를 복사한 최소 프로젝트에서 테스트했습니다. 전체 게임·플레이어 씬 통합 테스트와는 구분합니다.
- 사인검 어댑터 회귀 실행도 통과했습니다. 단타/다단타 매핑, 값 상속, 지원 범위, 오래된 미리보기 거부, 원본 저장·Undo, 없는 폴더로 복사, Animator 상태 등록, 중복 적용, 주입한 저장 실패의 롤백을 실제 Unity 에셋 API로 확인했습니다.
- 원본 `straight_sword_th_light_attack_01` FBX를 원본 애니메이션 Avatar 모델 `Md_Char_Low_Poly_Man` 및 검 메시와 함께 **24fps, 42프레임** 촬영했습니다. 모델/검에 검증용 단색 머티리얼과 기본 부착 회전을 사용했습니다. 이 검증은 렌더와 시간 대응을 확인하며 사람 기준 히트 경계 정확도를 평가하지 않습니다.
- 같은 저장된 배우/무기/세션에서 0.30~0.816667초를 **60fps, 32프레임**으로 재촬영했습니다. 24fps와 60fps 프레임의 고유 ID 및 원본 클립의 시간 기준을 유지합니다.
- 두 실제 FBX 촬영은 검증 helper가 정상 `capture` 요청 JSON을 메일박스에 넣고 응답을 기다리는 방식으로 실행했습니다. 공용 캡처 내부 메서드를 직접 호출한 것은 NUnit의 합성 메시 테스트뿐입니다.
- 원본 `Character_AllInOne.fbx`의 메타를 그대로 재임포트했을 때 `Hips`가 없는 잘못된 Humanoid Avatar를 관찰했습니다. 미리보기는 이를 허용하지 않았습니다. 그 에셋이 실제 게임에서 사용되는 플레이어라고 가정하지 않았습니다.

설치 배포본은 테스트를 제외합니다. 재현할 때는 별도 Unity 프로젝트에 이 패키지와 `com.unity.test-framework`를 설치하고, 저장소의 `Tests/Editor`를 그 프로젝트의 `Assets/AutoHitTimerTests/Editor`로 복사한 뒤 EditMode에서 `StudioBaekho.AutoHitTimer.Tests`를 실행하세요. Graphics 테스트에는 실제 그래픽 장치가 필요하므로 `-nographics`를 사용하지 마세요. 테스트 러너가 관리하는 임시 씬에서 테스트를 실행합니다.

작업 중 생성된 `core-tests.xml`, `core-tests-atomic.log`, 설치 receipt, 실제 캡처 `real-capture.json`과 PNG는 위 별도 검증 프로젝트에 보존됩니다. `.test-unity`는 Git에 포함하지 않습니다.
