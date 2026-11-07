Eastern Cyber Event Props V2 — Unity 6000.3.21f1 / URP 17.3

Import EventProps.unitypackage into a Unity 6 URP project. Assets live under
Assets/EventPropsV2; native prefabs, materials, editable clips and Animator
controllers live under Generated/<DeviceId>. Open Generated/EventPropsDemo.unity
for all seven props and a 155 cm reference capsule.

The prefabs have a metre-scale root at floor level. Movable rigid parts have
identity-rotation wrappers and preserve imported mesh transforms beneath them.
AnimationClips are independent native .anim assets, editable in Animation window.

EventPropController.Use() plays the mechanical use cycle, rejecting repeated
calls while busy. ResetDevice() immediately returns to Idle. Subscribe to
onUseStarted, onRewardReady, onUseCompleted and onDeviceReset to connect your
own interaction/economy logic. This package never changes health or currencies,
creates inventory items, deducts costs or chooses random rewards.

For Roulette call UseRoulette(RouletteOutcome.Miss / Gold / Item). Use() on the
roulette is a deterministic Gold demonstration. LastOutcome exposes the selected
presentation result. Your game must choose the outcome before requesting it.
onRewardReady signals the presentation moment for all outcomes, including Miss;
check LastOutcome in your game logic before granting any roulette reward.

The example scene includes EventPropRuntimeProbe, which automatically exercises
all devices in Play mode and writes Unity/runtime-validation.json. Disable
autoRun or remove this optional component for gameplay use. The probe checks
animation motion, reentrancy, reset and lifecycle events; it grants no rewards.

Static-body mesh colliders preserve openings; moving parts use box colliders. Review their fit
for your game's character collider, camera and interaction raycasts.

Rebuild: run Scripts/sync_assets.py after updating Models/*.fbx and manifests,
refresh the isolated validation project, then Tools > Eastern Cyber V2 > Build All
Event Props. Builder output includes asset-validation.json, native assets,
example scene and EventProps.unitypackage. The project uses URP only; Built-in
and HDRP require material conversion. Do not change unrelated projects.

V2 imports into Assets/EventPropsV2 and uses namespace EasternCyber.EventPropsV2
to coexist with V1. BaseColor maps use sRGB; tangent normals use NormalMap
import, with linear roughness and Metallic(R)/Smoothness(A) maps. BaseColor
textures keep a white RGB tint; transparent alpha is preserved independently.
The preview uses the same camera, lighting and background as V1.
