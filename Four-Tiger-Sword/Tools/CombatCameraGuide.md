# Attack camera authoring

The first configured profile is `Assets/_Project/CameraProfiles/Water_HeavyDash.asset`.
It is assigned to WaterFromAction > Heavy Attack Step > Camera Profile. PlayerCam in
Game Scene has CombatCameraDirector, assigned to the scene player's Combat Camera.
Other scenes can resolve/add a director on the Cinemachine camera following that player.

Create a profile with Create > Combat > Attack Camera Profile, then assign it to an
attack's Camera Profile field. Null profiles retain the existing camera/shake behavior.
Common combo, skill, ultimate, counter, landing and heavy attack paths forward camera
events. Custom forms that override the entire execution loop must forward
UpdateAttackCamera and NotifyCameraHit themselves (as WaterForm heavy attack does).

Fov Offset and Framing Weight use normalized attack progress (0..1), so attack speed
and hit stop follow the combat clock. FOV is additive in the Cinemachine pipeline;
CameraMove can continue changing the base lens without competing with this effect.
Camera Local Offset is relative to the captured view, not the sword/player rotation.
Target Weight blends the focus toward the enemy and Max Framing Offset bounds it.
Direction Look Ahead is the fallback without a living, nearby target. Target loss
blends toward that fallback. Preserve Chain View Direction retains the correction
basis across follow-ups without disabling mouse/controller camera input.

Hidden runtime tracking targets feed the existing OrbitalFollow, RotationComposer,
Deoccluder and ImpulseListener. Their references are restored on completion, cancel,
disable and death. Profiles never hold scene references or mutable playback state.
Return uses scaled time so pause/hit stop also pause the camera release. Hit Impulse
replaces the legacy attack shake only while the profile is active, once per attack.
Water heavy hit stop is likewise limited to once per dash, including multi-enemy hits.

Timeline is not required for these reactive attacks. Use a separate explicitly owned
camera/Timeline sequence for a future authored ultimate shot, canceling combat camera
playback when handing control over.

Validation: Unity Pipeline `run_script`, file `Tools/CombatCameraRegression.cs`, entry
`CombatCameraRegression.Run` (Edit Mode, isolated preview scene).
Visual tuning still needs real combat: test misses, close targets, three passes,
walls, target death, interruptions and different attack speeds.
