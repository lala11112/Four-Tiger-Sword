# Form combat implementation

Source: 플레이어 무기 폼(Form) 스킬 상세 명세 및 모션, 5 pages.

## Scope

This update fills missing mechanics and empty attack data. Existing Fire/Wood basic attack timings, damage and thrust curves, Fire skill's authored three-hit sequence, and Water's existing first combo step and dash initial damage are retained. They are not a full rebalance to the PDF's numerical tables.

- Fire: per-target six-hit stack explosion (2.5 ATK, configurable `FireExplosionDelay`, default 0.15 seconds realtime). Stacks reset immediately; independent pending explosions are cancelled on target death/disable. Delayed ground explosion remains 3.5 ATK, one second after impact.
- Water: existing WaterFlowGauge now drives the 100-point, five-second attack/movement-speed buff. Skill cost/cooldown 150/6, delayed five hits of 0.5 ATK. Two missing combo inputs added. Ultimate ten hits of 1.2 ATK, invulnerability and renderer restoration, collision-aware attempt to move behind target.
- Wood: existing five-stack bind duration corrected to 1.5 seconds; missing 1.5 ATK skill and poison field (0.5 ATK/second); 4 ATK ultimate, airborne and healing field.
- Iron: missing three-hit basic attack (1/1.2/1.8 ATK), 3 ATK skill, five-hit 2 ATK laser. Independent 15%/5-second basic and 30%/10-second skill debuffs, refreshed rather than stacked.
- Earth: missing shield bash/slash (0.8/1.2 ATK), frontal guard window, 3.5 ATK counter, 2.5 ATK shockwave/shield, five-second full absorption and 4 ATK + 80% absorbed-damage release.
- Ultimate cooldowns: Fire 30, Water 25, Wood 35, Iron 30, Earth 40 seconds, begin on activation and continue while unequipped.

## Inspector and provisional values

New tuning fields live on each WeaponActionDataSO in Assets/_Project/Scripts/FormActions.

The PDF does not specify field radius, poison/healing duration, healing amount, shield amount, movement-buff amount, many recovery times, or defense formula. Current provisional settings:

- AreaRadius 4 m; FieldDuration 5 seconds.
- HealMaxHpPerSecond 0.05 (5% of max HP each second); only PlayerController allies exist in this project.
- ShieldAmount 300 HP; ShieldDuration 5 seconds; reapplication refreshes, does not stack.
- Water move/run speed multiplier 1.3; charge 12 per hit (retained from existing code).
- Defense mitigation: damage * 100 / (100 + max(0, effective DEF)). This activates previously unused enemy DEF and changes combat balance. Shred subtracts a percentage of base DEF, leaving defense buffs intact. Iron ultimate bypasses DEF, elemental multipliers and critical RNG.
- Frames convert at 60 FPS. New animation names are empty and future animation calls are commented TODOs. Existing valid animation bindings stay enabled.
- Earth prose mentions 2nd/3rd slashes but its damage table only specifies bash + slash; the two-hit numerical table is used.

## Lifecycle

Damage/healing fields and delayed hits finish after form changes, but stop when the caster dies or the effect runner is disabled. Fields are anchored in world space at impact. Water renderer/collision overrides and Earth absorption are restored on state exit/owner disable. Collider hits are deduplicated by damage receiver.

Missing motion/VFX assets are not generated. The water ultimate has functional disappearance/hits/reappearance, but no new cinematic slash animation; requested new animations remain comments.
