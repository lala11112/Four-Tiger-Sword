Asset Creator - Vladyslav Horobets (Hovl).
All that is in the folder "AAA Projectiles" can be used in commerce, even demo scene files.
-----------------------------------------------------

If you want to use post-effects like in the demo video:
https://youtu.be/hZSZ2Q8MF3k

Using:

1) Shaders
1.1)The "Use depth" on the material from the custom shaders is the Soft Particle Factor.
1.2)Use "Center glow"[MaterialToggle] only with particle system. This option is used to darken the main texture with a white texture (white is visible, black is invisible).
    If you turn on this feature, you need to use "Custom vertex stream" (Uv0.Custom.xy) in tab "Render". And don't forget to use "Custom data" parameters in your PS.
1.3)The distortion shader only works with standard rendering. Delete (if exist) distortion particles from effects if you use LWRP or HDRP!
1.4)You can change the cutoff in all shaders (except Add_CenterGlow and Blend_CenterGlow ) using (Uv0.Custom.xy) in particle system.

2)Light.
2.1)You can disable light in the main effect component (delete light and disable light in PS). 
    Light strongly loads the game if you don't use light probes or something else.

3)Scripts
HS_ProjectileMover — Documentation

Description
HS_ProjectileMover controls the movement, collision behavior, and visual effects of projectile objects.
It handles projectile speed, hit effects, particle systems, detached VFX elements, and supports both destruction and pooling workflows.

The script is designed for VFX projectiles used in spells, bullets, energy blasts, or similar effects.

Main Features:

Moves projectile forward using Rigidbody velocity
Spawns hit effects on collision
Supports pooled projectiles (reuse instead of destroy)
Handles particle systems properly on impact
Allows detached particle effects to continue playing after collision
Automatically restores detached objects when projectile is reused

Key Parameters:

Speed -
Controls the forward velocity of the projectile.
Hit Offset -
Moves the hit effect slightly away from the surface normal to avoid clipping.
Use Fire Point Rotation -
If enabled, the hit effect rotation will match the fire point orientation.
Rotation Offset -
Optional rotation override applied to the hit effect.
Hit -
GameObject used as the hit effect container.
Hit PS -
Particle system played when the projectile collides.
Flash -
Optional muzzle flash object that detaches on spawn.
Projectile PS -
Main projectile particle system.
Detached -
Array of objects that contain particle systems (such as trails or smoke).
These objects detach on impact so their particles can finish playing naturally.

Components:

RB -
Rigidbody used for projectile movement.
Col -
Collider used for collision detection.
Light Source -
Optional light attached to the projectile.

Lifetime Settings

Not Destroy
If enabled, the projectile will be disabled instead of destroyed.
This allows it to be reused with an object pool.

Life Time
Maximum lifetime of the projectile if it does not hit anything.

Detached Life Time
How long detached particle objects remain alive after impact.

Collision Behavior

When the projectile collides:

Rigidbody movement is stopped
Light and collider are disabled
Projectile particle emission stops
Hit effect is positioned and played
Detached objects are unparented
Detached particle systems stop emitting but existing particles finish their lifetime

If Not Destroy is enabled:
The projectile will be disabled after the hit effect finishes
Detached objects will be restored when the projectile is reused

If Not Destroy is disabled:
The projectile will be destroyed after the hit effect duration
Detached objects will be destroyed after Detached Life Time

Detached Objects Logic:
Detached objects must be child objects of the projectile.
Each detached object can contain multiple particle systems.

On collision:
The object is unparented
Emission stops
Existing particles finish their lifetime
If pooling is enabled, the objects are restored to their original parent when the projectile is reactivated.

Typical Use Case:
Projectile Prefab Structure Example

Projectile
├── Mesh
├── Collider
├── Rigidbody
├── Projectile_PS
├── Flash
└── Detached_Trail
├── Smoke
└── Sparks

Pooling Support

When using an object pool:

Set:
Not Destroy = true
The projectile will be disabled instead of destroyed and can be reused safely.
Detached particle objects will automatically return to their original positions when the projectile is activated again.

Notes:
Detached objects should only contain particle systems.
Ensure Rigidbody and Collider references are assigned.
Projectile should face forward in the Z direction for correct movement.


4)Quality
4.1) For better sparks quality enable "Anisotropic textures: Forced On" in quality settings.

SUPPORT ASSET FOR BiRP, URP or HDRP is here --> Tools > RP changer for Hovl Studio Assets

Contact me if you have any questions.
My email: hovlstudio1@gmail.com