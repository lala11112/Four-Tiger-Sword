using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace StudioBaekho.CombatLab.FourTigerSword
{
    // Lives in Assets/Editor so it can reference the game's Assembly-CSharp types.
    // A fresh Play Mode session is required for every candidate; this is not a world resetter.
    public sealed class FourTigerCombatSession : ICombatLabSession
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<FieldInfo, InputActionReference> _originalReferences = new Dictionary<FieldInfo, InputActionReference>();
        private readonly Dictionary<InputAction, bool> _originalActionStates = new Dictionary<InputAction, bool>();
        private readonly HashSet<Enemy> _enemies = new HashSet<Enemy>();
        private readonly CombatMetrics _metrics = new CombatMetrics();
        private readonly float _duration;
        private readonly float _startedAt;
        private readonly float _startedGameTime;
        private readonly float _oldTimeScale;
        private readonly bool _oldRunInBackground;
        private readonly UnityEngine.Random.State _oldRandomState;
        private readonly InputSettings _originalInputSettings;
        private PlayerController _player;
        private PlayerInputHandler _input;
        private IForm _originalForm;
        private BaseForm _form;
        private WeaponActionDataSO _data;
        private List<WeaponActionData> _selectedSteps;
        private readonly string _actionId;
        private readonly bool _isNormal;
        private readonly bool _isHeavy;
        private readonly bool _isSkill;
        private readonly bool _isUltimate;
        private bool _ultimateFollowUp;
        private bool _heavyHolding;
        private bool _resourcesPrepared;
        private bool _resourceExhaustionRecorded;
        private float _oldSp, _oldSpRegenTimer, _oldUltimateGauge;
        private FieldInfo _spField, _spRegenField, _ultimateGaugeField;
        private FieldInfo _formTimerField, _formStepField;
        private bool _wasExecuting;
        private int _lastActionStep = -1;
        private float _lastActionTimer = -1f;
        private int _actionExecutions;
        private string _executionError;
        private HashSet<IForm> _registeredForms;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private FieldInfo _hitStopField;
        private bool _inputChanged;
        private bool _inputSettingsChanged;
        private bool _oldInputEnabled;
        private bool _disposed;
        private bool _complete;
        private int _lastInputFrame = -1;
        private int _pulsePhase;
        private float _nextPressAt;
        private float _attackDistance;
        private string _lastState;

        public bool Complete => _complete;
        public CombatMetrics Metrics => _metrics;

        public FourTigerCombatSession(ExperimentRequest request, CombatCandidate candidate)
        {
            _actionId = FourTigerCombatAdapter.NormalizeActionId(request.actionId);
            _isNormal = _actionId.StartsWith("normal-", StringComparison.Ordinal);
            _isHeavy = _actionId == "heavy";
            _isSkill = _actionId == "skill";
            _isUltimate = _actionId == "ultimate";
            _duration = request.durationSeconds;
            _startedAt = Time.realtimeSinceStartup;
            _startedGameTime = Time.time;
            _oldTimeScale = Time.timeScale;
            _oldRunInBackground = Application.runInBackground;
            _oldRandomState = UnityEngine.Random.state;
            _originalInputSettings = InputSystem.settings;
            try
            {
                PreparePlayer();
                var source = FourTigerCombatAdapter.ResolveSource(request.sourceGuid);
                var data = FourTigerCombatAdapter.CloneAndApply(source, candidate, _owned, _actionId);
                _data = data;
                _selectedSteps = FourTigerCombatAdapter.SelectedSteps(data, _actionId) ?? new List<WeaponActionData>();
                // Isolated-step scenarios retain the selected animation and hit data, but start it
                // as a one-step runtime combo. Export still preserves the source's complete combo.
                if (_isNormal && _actionId != "normal-combo") data.Actions.ComboSteps = _selectedSteps;
                // Even a single pressed frame can exceed the normal hold threshold during a slow capture.
                // This runtime-only policy override keeps comparisons on the intended basic combo.
                if (_isNormal && data is WaterFormActionDataSO water)
                    water.FormExecution.HeavyAttackHoldTime = Mathf.Max(water.FormExecution.HeavyAttackHoldTime, request.durationSeconds + 1f);
                _form = FourTigerCombatAdapter.CreateForm(data);
                _ultimateFollowUp = _isUltimate && (_form is FireForm || _form is EarthForm || _form is WoodForm);
                var first = _selectedSteps.Count > 0 ? _selectedSteps[0]
                    : (data.Actions.ComboSteps != null && data.Actions.ComboSteps.Count > 0 ? data.Actions.ComboSteps[0] : null);
                _attackDistance = EstimateAttackDistance(first);
                _formTimerField = typeof(BaseForm).GetField("_timer", Hidden);
                _formStepField = typeof(BaseForm).GetField(_isSkill ? "_skillStep" : _isUltimate ? "_ultimateStep" : "_comboStep", Hidden);
                if (_formTimerField == null || _formStepField == null)
                    throw new InvalidOperationException("The adapter can no longer observe the selected action's runtime steps.");
                _originalForm = _player.FormManager.CurrentForm;
                _player.FormManager.CleanupTransientEffects();
                _player.FormManager.ChangeForm(_form);
                PrepareResources();
                InstallVirtualInput();
                _player.StatManager.OnDamageTaken += OnPlayerDamaged;
                SubscribeEnemies();
                Application.runInBackground = true;
                UnityEngine.Random.InitState(request.seed);
                foreach (var parameter in candidate.values)
                    _metrics.appliedValues.Add(new ParameterValue(parameter.id, parameter.value));
                RecordResolvedValues(data, request.sourceGuid);
                _metrics.notes.Add("Selected action: " + FourTigerCombatAdapter.ActionLabel(source, _actionId) + " (" + _actionId + "). Virtual input passes through the original PlayerInputHandler and combat state machine.");
                _metrics.notes.Add("A temporary InputSettings copy allows virtual input while the desktop app has focus. The original settings object is restored when the session ends.");
                if (_isNormal && data is WaterFormActionDataSO)
                    _metrics.notes.Add("The runtime copy's heavy-attack hold threshold is temporarily raised above the experiment duration; this policy override is not exported to the game asset.");
                if (_isNormal && _actionId != "normal-combo")
                    _metrics.notes.Add("This isolated-step scenario places only the selected normal attack in the runtime combo. It does not measure the timing or resource effects of preceding combo steps.");
                if (_ultimateFollowUp)
                    _metrics.notes.Add("This ultimate activates a buff rather than a direct attack. The policy presses R once, then plays the unchanged normal combo to show its effect. Follow-up attacks are identified separately in events.");
                if (_isSkill)
                    _metrics.notes.Add("Only E skills are requested. Their original cooldown and SP cost remain active; no basic attacks are substituted while waiting.");
                if (candidate.values.Exists(value => value.id == FourTigerCombatAdapter.HitStopId))
                    _metrics.notes.Add("The selected action's Feedback.HitStop duration is applied to every effective hit, including hit overrides. Each hit's original effective time scale is preserved. Player defaults, parries and other actions remain unchanged; exported form copies retain the action overrides.");
                _metrics.notes.Add("The whole scene's enemy AI remains active. Scene startup, physics, navigation and frame timing are not deterministic; the seed does not guarantee identical encounters.");
                _metrics.notes.Add("Enemy.OnDamaged counts all observed enemy damage, including secondary effects. It does not identify attack steps or prove a combo success rate.");
                _metrics.notes.Add("Movement uses a simple nearest-enemy approach and estimated attack distance. No pathfinding or obstacle avoidance is provided by this player policy.");
                InputSystem.onBeforeUpdate += BeforeInputUpdate;
                AddEvent("session-start", candidate.label);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static float EstimateAttackDistance(WeaponActionData step)
        {
            if (step == null) return 1.8f;
            float extent = step.Execution.Hitbox.HitBoxShape == HitBoxShape.Box ? step.Execution.Hitbox.HitBoxSize.z : step.Execution.Hitbox.HitBoxRadius;
            return Mathf.Clamp(step.Execution.Hitbox.HitBoxOffset.z + extent - 0.35f, 0.7f, 3f);
        }

        private void PrepareResources()
        {
            if (_isHeavy)
            {
                var water = _form as WaterForm;
                var flow = typeof(WaterForm).GetField("_flow", Hidden)?.GetValue(water) as WaterFlowGauge;
                if (water == null || flow == null)
                    throw new InvalidOperationException("This form does not expose a supported heavy-attack resource.");
                flow.Charge(flow.MaxGauge);
                Value("runtime.policy.initialHeavyCharges", flow.AvailableCharges);
                _metrics.notes.Add("The temporary Water form starts with three heavy-attack charges. Charges are consumed normally and are not refilled; no normal attacks are substituted after exhaustion.");
            }
            if (!_isSkill && !_isUltimate) return;
            var stats = _player.StatManager;
            _spField = typeof(PlayerStatManager).GetField("_currentSp", Hidden);
            _spRegenField = typeof(PlayerStatManager).GetField("_spRegenTimer", Hidden);
            _ultimateGaugeField = typeof(PlayerStatManager).GetField("_currentUltimateGauge", Hidden);
            if (_spField == null || _spRegenField == null || _ultimateGaugeField == null)
                throw new InvalidOperationException("The adapter can no longer snapshot and restore skill/ultimate resources.");
            _oldSp = stats.CurrentSp;
            _oldSpRegenTimer = (float)_spRegenField.GetValue(stats);
            _oldUltimateGauge = stats.CurrentUltimateGauge;
            _resourcesPrepared = true;
            if (_isSkill)
            {
                if (stats.MaxSp < _form.SkillSpCost)
                    throw new InvalidOperationException("The player's maximum SP is below this skill's cost. Increase the test player's SP before running this action.");
                stats.AddSp(stats.MaxSp);
                _metrics.notes.Add("Scenario setup fills SP once before E is requested. Original skill cost, cooldown and regeneration remain active. Pre-test SP and regeneration state are restored on disposal.");
            }
            if (_isUltimate)
            {
                stats.AddUltimateGauge(stats.MaxUltimateGauge);
                _metrics.notes.Add("Scenario setup fills the ultimate gauge once. R is requested until its execution is observed, then never toggled off or refilled by the policy. The original gauge is restored on disposal.");
            }
            Value("runtime.policy.initialSP", stats.CurrentSp);
            Value("runtime.policy.initialUltimateGauge", stats.CurrentUltimateGauge);
        }

        private void PreparePlayer()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Combat experiments require Play Mode.");
            if (Time.timeScale <= 0f) throw new InvalidOperationException("Start the experiment while the game is unpaused, outside a hit stop.");
            if (InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                throw new InvalidOperationException("Four Tiger experiments require Input System 'Process Events In Dynamic Update'.");
            foreach (var player in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (!player.isActiveAndEnabled) continue;
                if (_player != null) throw new InvalidOperationException("The experiment requires exactly one active PlayerController.");
                _player = player;
            }
            if (_player == null || _player.FormManager == null || _player.StateMachine == null || _player.StatManager == null)
                throw new InvalidOperationException("No initialized active PlayerController was found.");
            if (!(_player.StateMachine.CurrentState is PlayerIdleState) && !(_player.StateMachine.CurrentState is PlayerRunState))
                throw new InvalidOperationException("Start the experiment with the player idle or running, outside an attack or hit reaction.");
            if (_player.StatManager.CurrentHp <= 0f || !_player.IsGround())
                throw new InvalidOperationException("The player must be alive and grounded before the experiment begins.");
            if (_player.Animator == null || _player.Animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("The experiment player requires its configured Animator controller.");
            if (CombatSettings.Active == null || CombatSettings.Active.ElementTable == null)
                throw new InvalidOperationException("The active scene requires CombatSettings with an Element Table.");
            _input = _player.Input;
            if (_input == null || !_input.enabled)
                throw new InvalidOperationException("The player's PlayerInputHandler must be enabled.");
            _hitStopField = typeof(PlayerController).GetField("_hitStopDuration", Hidden);
            var registeredField = typeof(FormManager).GetField("_registeredForms", Hidden);
            _registeredForms = registeredField?.GetValue(_player.FormManager) as HashSet<IForm>;
            if (_hitStopField == null || _hitStopField.FieldType != typeof(float) || _registeredForms == null)
                throw new InvalidOperationException("The Four Tiger integration no longer matches PlayerController/FormManager. Update the adapter before running.");
            bool validEnemy = false;
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (!IsLiving(enemy)) continue;
                var agent = enemy.GetComponent<NavMeshAgent>();
                if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                    throw new InvalidOperationException("Every active enemy needs an enabled NavMeshAgent on a baked NavMesh for meaningful knockback comparisons: " + enemy.name);
                validEnemy = true;
            }
            if (!validEnemy) throw new InvalidOperationException("The scene needs at least one living active Enemy on a baked NavMesh.");
        }

        private void InstallVirtualInput()
        {
            // runInBackground alone does not route input to an unfocused Game View.
            // Use a temporary settings object so desktop-driven runs keep receiving
            // Dynamic input updates without changing the project's settings asset.
            var inputSettings = Object.Instantiate(_originalInputSettings);
            inputSettings.name = _originalInputSettings.name + " (Combat Lab Runtime)";
            inputSettings.hideFlags = HideFlags.HideAndDontSave;
            _owned.Add(inputSettings);
            inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _inputSettingsChanged = true;
            InputSystem.settings = inputSettings;
            var clonedAssets = new Dictionary<InputActionAsset, InputActionAsset>();
            var replacements = new Dictionary<FieldInfo, InputActionReference>();
            _keyboard = InputSystem.AddDevice<Keyboard>("CombatLabKeyboard");
            _mouse = InputSystem.AddDevice<Mouse>("CombatLabMouse");
            foreach (var field in typeof(PlayerInputHandler).GetFields(Hidden))
            {
                if (field.FieldType != typeof(InputActionReference)) continue;
                var original = field.GetValue(_input) as InputActionReference;
                _originalReferences.Add(field, original);
                if (original == null || original.action == null) continue;
                var action = original.action;
                var source = action.actionMap?.asset;
                if (source == null) throw new InvalidOperationException("Input reference has no owning action asset: " + field.Name);
                if (!clonedAssets.TryGetValue(source, out var copy))
                {
                    copy = Object.Instantiate(source);
                    copy.name = source.name + " (Combat Lab Runtime)";
                    copy.Disable();
                    copy.devices = new InputDevice[] { _keyboard, _mouse };
                    foreach (var map in copy.actionMaps) map.devices = new InputDevice[] { _keyboard, _mouse };
                    // A project's remapped form/parry bindings must not accidentally respond to
                    // the policy's WASD/E/R keys. Enable only the explicit virtual bindings below.
                    foreach (var clonedAction in copy)
                        for (int binding = 0; binding < clonedAction.bindings.Count; binding++)
                            clonedAction.ApplyBindingOverride(binding, new InputBinding { overridePath = string.Empty });
                    _owned.Add(copy);
                    clonedAssets.Add(source, copy);
                    // Camera look must not be controlled by real mouse motion during a comparison.
                    var look = source.FindAction("Look");
                    if (look != null) _originalActionStates[look] = look.enabled;
                }
                _originalActionStates[action] = action.enabled;
                var replacement = InputActionReference.Create(copy.FindAction(action.id.ToString(), true));
                _owned.Add(replacement);
                replacements.Add(field, replacement);
            }
            InputAction move = null, attack = null, skill = null, ultimate = null;
            foreach (var item in replacements)
            {
                if (item.Key.Name == "_moveAction") move = item.Value.action;
                if (item.Key.Name == "_attackAction") attack = item.Value.action;
                if (item.Key.Name == "_skillAction") skill = item.Value.action;
                if (item.Key.Name == "_ultimateAction") ultimate = item.Value.action;
            }
            if (move == null || attack == null)
                throw new InvalidOperationException("PlayerInputHandler requires assigned _moveAction and _attackAction references.");
            if (_isSkill && skill == null) throw new InvalidOperationException("Assign PlayerInputHandler._skillAction before testing E skills.");
            if (_isUltimate && ultimate == null) throw new InvalidOperationException("Assign PlayerInputHandler._ultimateAction before testing R ultimates.");
            // Binding edits affect runtime copies only, including when a developer remaps the original inputs.
            for (int i = 0; i < move.bindings.Count; i++) move.ApplyBindingOverride(i, new InputBinding { overridePath = string.Empty });
            for (int i = 0; i < attack.bindings.Count; i++) attack.ApplyBindingOverride(i, new InputBinding { overridePath = string.Empty });
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            attack.AddBinding("<Mouse>/leftButton");
            skill?.AddBinding("<Keyboard>/e");
            ultimate?.AddBinding("<Keyboard>/r");
            _oldInputEnabled = _input.enabled;
            _inputChanged = true;
            _input.enabled = false;
            foreach (var item in _originalActionStates) item.Key.Disable();
            foreach (var replacement in replacements) replacement.Key.SetValue(_input, replacement.Value);
            _input.enabled = true;
        }

        private void RecordResolvedValues(WeaponActionDataSO data, string sourceGuid)
        {
            _metrics.notes.Add("Runtime source GUID: " + sourceGuid + "; starting form: " + (_originalForm?.GetType().Name ?? "None") + "; observed active enemy count: " + _enemies.Count + ".");
            var position = _player.transform.position;
            Value("runtime.player.start.x", position.x);
            Value("runtime.player.start.y", position.y);
            Value("runtime.player.start.z", position.z);
            Value("runtime.enemy.startCount", _enemies.Count);
            float playerHitStop = (float)_hitStopField.GetValue(_player);
            Value("runtime.player.hitStopDuration", playerHitStop);
            var timeScaleField = typeof(PlayerController).GetField("_hitStopTimeScale", Hidden);
            if (timeScaleField == null || timeScaleField.FieldType != typeof(float))
                throw new InvalidOperationException("The adapter cannot read PlayerController._hitStopTimeScale for the result snapshot.");
            float playerHitStopTimeScale = (float)timeScaleField.GetValue(_player);
            Value("runtime.player.hitStopTimeScale", playerHitStopTimeScale);
            Value("runtime.player.attackSpeed", _player.StatManager.GetStat(StatType.ST_ATK_SPD));
            if (_form is IHeavyAttackForm heavy) Value("runtime.policy.heavyHoldThreshold", heavy.HeavyAttackHoldTime);
            Value("runtime.policy.attackDistance", _attackDistance);
            Value("runtime.action.selectedStepCount", _selectedSteps.Count);
            if (_isSkill)
            {
                Value("runtime.action.skillSpCost", _form.SkillSpCost);
                Value("runtime.action.skillCooldown", _form.SkillCooldown);
            }
            if (_isUltimate) Value("runtime.action.ultimateCooldown", data.CommonBalance.UltimateCooldown);
            RecordSpecialResolvedValues(data);
            var snapshotSteps = _selectedSteps;
            if (_ultimateFollowUp)
            {
                snapshotSteps = data.Actions.ComboSteps ?? new List<WeaponActionData>();
                _metrics.notes.Add("runtime.followup.* snapshots describe unchanged normal attacks after the buff ultimate; they are not ultimate hit steps.");
            }
            for (int i = 0; i < snapshotSteps.Count; i++)
            {
                var step = snapshotSteps[i];
                string prefix = "runtime." + (_ultimateFollowUp ? "followup" : _isNormal ? "combo" : _actionId) + "." + i + ".";
                Value(prefix + "duration", step.Execution.Duration);
                Value(prefix + "knockbackDefault", step.Balance.KnockbackForce);
                Value(prefix + "cameraShakeFallback", step.Feedback.Camera.CameraShakeStrength);
                Value(prefix + "cameraProfileAssigned", step.Feedback.Camera.CameraProfile == null ? 0f : 1f);
                if (step.Feedback.Camera.CameraProfile != null) Value(prefix + "cameraProfileHitImpulse", step.Feedback.Camera.CameraProfile.HitImpulse);
                if (_isSkill && data is WoodFormActionDataSO) continue; // Gather damage bypasses the common feedback path.
                RecordHitStop(prefix, step, null, playerHitStop, playerHitStopTimeScale);
                if (_isUltimate && data is WaterFormActionDataSO)
                {
                    // The fixed ten-hit loop reads only HitStop settings by index; it ignores authored windows.
                    for (int hitIndex = 0; hitIndex < 10; hitIndex++)
                    {
                        var hit = step.HitEvents != null && hitIndex < step.HitEvents.Count ? step.HitEvents[hitIndex] : null;
                        RecordHitStop(prefix + "hit." + hitIndex + ".", step, hit, playerHitStop, playerHitStopTimeScale);
                    }
                    continue;
                }
                if (FourTigerCombatAdapter.UsesHitEvents(data, _actionId) && step.HitEvents != null && step.HitEvents.Count > 0)
                {
                    for (int hitIndex = 0; hitIndex < step.HitEvents.Count; hitIndex++)
                    {
                        var hit = step.HitEvents[hitIndex];
                        if (hit == null) continue;
                        string hitPrefix = prefix + "hit." + hitIndex + ".";
                        Value(hitPrefix + "start", hit.Execution.StartTime);
                        Value(hitPrefix + "duration", hit.Execution.Duration);
                        Value(hitPrefix + "knockbackEffective", hit.Balance.OverrideKnockbackForce ? hit.Balance.KnockbackForce : step.Balance.KnockbackForce);
                        RecordHitStop(hitPrefix, step, hit, playerHitStop, playerHitStopTimeScale);
                    }
                }
                else
                {
                    Value(prefix + "hitStart", step.Execution.HitStartTime);
                    Value(prefix + "hitDuration", step.Execution.HitDuration);
                    Value(prefix + "knockbackEffective", step.Balance.KnockbackForce);
                }
            }
            _metrics.notes.Add("runtime.* values are read from the installed runtime copy and player, not inferred from candidate multipliers. Camera profile impulse and fallback strength are both retained because the actual route depends on the combat camera being active at impact.");
            if (_isUltimate && data is WaterFormActionDataSO)
                _metrics.notes.Add("Water ultimate uses only Actions.UltimateSteps[0], its camera fallback and ten indexed HitStop settings; missing hit entries inherit the step. Hit windows, camera profiles and knockback are unused by this custom loop.");
        }

        private void RecordHitStop(string prefix, WeaponActionData step, HitEvent hit, float playerDuration, float playerTimeScale)
        {
            var effective = FourTigerCombatAdapter.EffectiveHitStop(step, hit, playerDuration, playerTimeScale);
            Value(prefix + "hitStopOverride", (hit == null ? step.Feedback.HitStop.OverrideHitStop : hit.Feedback.HitStop.OverrideHitStop) ? 1f : 0f);
            Value(prefix + "hitStopDurationEffective", effective.HitStopDuration);
            Value(prefix + "hitStopTimeScaleEffective", effective.HitStopTimeScale);
        }

        private void RecordSpecialResolvedValues(WeaponActionDataSO data)
        {
            // Form-specific implementations sometimes bypass WeaponActionData hit feedback.
            // Keep their actual configuration visible beside the generated multipliers.
            if (_isSkill && data is WoodFormActionDataSO woodSkill)
            {
                Value("runtime.action.SkillRadius", woodSkill.FormExecution.SkillRadius);
                Value("runtime.action.PullSpeed", woodSkill.FormExecution.PullSpeed);
                Value("runtime.action.SkillDamageMultiplier", woodSkill.FormBalance.SkillDamageMultiplier);
            }
            else if (_isUltimate && data is WaterFormActionDataSO water)
                Value("runtime.action.AreaRadius", water.FormExecution.AreaRadius);
            else if (_isUltimate && data is FireFormActionDataSO fire)
            {
                Value("runtime.action.FireUltimateDamageDealtBonusPercent", fire.FormBalance.FireUltimateDamageDealtBonusPercent);
                Value("runtime.action.FireUltimateAttackSpeedBonusPercent", fire.FormBalance.FireUltimateAttackSpeedBonusPercent);
                Value("runtime.action.FireUltimateDamageTakenBonusPercent", fire.FormBalance.FireUltimateDamageTakenBonusPercent);
            }
            else if (_isUltimate && data is EarthFormActionDataSO earth)
            {
                Value("runtime.action.UltimateShieldAmount", earth.FormBalance.UltimateShieldAmount);
                Value("runtime.action.UltimateBuffDuration", earth.FormBalance.UltimateBuffDuration);
                Value("runtime.action.UltimateShieldDamageBonusPercent", earth.FormBalance.UltimateShieldDamageBonusPercent);
            }
            else if (_isUltimate && data is WoodFormActionDataSO wood)
            {
                Value("runtime.action.UltimateRadius", wood.FormExecution.UltimateRadius);
                Value("runtime.action.UltimateFieldDuration", wood.FormBalance.UltimateFieldDuration);
                Value("runtime.action.ElementDamageBonusPercent", wood.FormBalance.ElementDamageBonusPercent);
            }
        }

        private void Value(string id, float value) => _metrics.appliedValues.Add(new ParameterValue(id, value));

        public void Tick(float unscaledDeltaTime)
        {
            if (_disposed) return;
            if (_executionError != null) throw new InvalidOperationException(_executionError);
            if (_complete) return;
            _metrics.elapsedSeconds = Time.realtimeSinceStartup - _startedAt;
            SubscribeEnemies();
            if (_player == null || !_player.isActiveAndEnabled || _player.StatManager.CurrentHp <= 0f)
            { Finish("Player unavailable or defeated."); return; }
            string state = _player.StateMachine.CurrentState?.GetType().Name ?? "None";
            if (state != _lastState) { _lastState = state; AddEvent("player-state", state); }
            ObserveActionExecution();
            if (_metrics.elapsedSeconds >= _duration) Finish("Requested real-time duration reached.");
            if (_executionError != null) throw new InvalidOperationException(_executionError);
        }

        private void BeforeInputUpdate()
        {
            if (_disposed || !Application.isPlaying || InputState.currentUpdateType != InputUpdateType.Dynamic
                || _keyboard == null || _mouse == null || _lastInputFrame == Time.frameCount) return;
            _lastInputFrame = Time.frameCount;
            ObserveActionExecution();
            if (!_complete && Time.realtimeSinceStartup - _startedAt >= _duration)
                Finish("Requested real-time duration reached.");
            var keys = new List<Key>();
            bool pressed = false;
            if (!_complete && _player != null && _player.isActiveAndEnabled && _player.StatManager.CurrentHp > 0f)
            {
                var target = NearestEnemy();
                bool locomotion = _player.StateMachine.CurrentState is PlayerIdleState || _player.StateMachine.CurrentState is PlayerRunState;
                bool nearTarget = false;
                if (target != null)
                {
                    Vector3 offset = target.transform.position - _player.transform.position;
                    offset.y = 0f;
                    nearTarget = offset.magnitude <= _attackDistance;
                    if (locomotion && !nearTarget) AddMovementKeys(offset.normalized, keys);
                    if (_player.StateMachine.CurrentState is PlayerIdleState && offset.sqrMagnitude > 0.001f)
                        _player.transform.rotation = Quaternion.LookRotation(offset);
                }
                if (_isHeavy && _heavyHolding)
                {
                    var attackState = _player.StateMachine.CurrentState as PlayerAttackState;
                    if (attackState?.IsHeavyAttack == true || (!locomotion && attackState == null))
                    {
                        _heavyHolding = false;
                        _nextPressAt = Time.realtimeSinceStartup + 0.15f;
                    }
                    else pressed = true;
                }
                else if (_pulsePhase == 1)
                {
                    // One complete Dynamic input update is released before another press is possible.
                    _pulsePhase = 0;
                    _nextPressAt = Time.realtimeSinceStartup + 0.08f;
                }
                else if (target != null && nearTarget && Time.realtimeSinceStartup >= _nextPressAt)
                {
                    bool normalPolicy = _isNormal || (_ultimateFollowUp && _actionExecutions > 0);
                    if (normalPolicy && (locomotion || (_actionId == "normal-combo" || _ultimateFollowUp)
                        && _player.StateMachine.CurrentState is PlayerAttackState attack && !attack.IsHeavyAttack && _form.CanChainAttack))
                    {
                        pressed = true;
                        RequestInput("Virtual mouse tap", _ultimateFollowUp ? "follow-up-request" : "attack-request");
                    }
                    else if (_isHeavy && locomotion)
                    {
                        if ((_form as IHeavyAttackForm)?.CanHeavyAttack == true)
                        {
                            pressed = _heavyHolding = true;
                            _metrics.attackRequests++;
                            AddEvent("attack-request", _actionId + ": virtual mouse hold");
                        }
                        else if (!_resourceExhaustionRecorded)
                        {
                            _resourceExhaustionRecorded = true;
                            AddEvent("resource-wait", "Heavy-attack charges exhausted; no normal-attack fallback.");
                        }
                    }
                    else if (_isSkill && locomotion && _form.CanSkill)
                    {
                        keys.Add(Key.E);
                        RequestInput("Virtual E press", "skill-request");
                    }
                    else if (_isUltimate && _actionExecutions == 0 && locomotion && _form.CanUltimate)
                    {
                        keys.Add(Key.R);
                        RequestInput("Virtual R press", "ultimate-request");
                    }
                }
            }
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.ToArray()));
            InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left, pressed));
        }

        private void RequestInput(string detail, string kind)
        {
            _pulsePhase = 1;
            _metrics.attackRequests++;
            AddEvent(kind, _actionId + ": " + detail);
        }

        private void ObserveActionExecution()
        {
            if (_complete || _player == null || _form == null || _player.FormManager?.CurrentForm != _form) return;
            var state = _player.StateMachine?.CurrentState;
            var attack = state as PlayerAttackState;
            bool selected = _isNormal ? attack != null && !attack.IsCharging && !attack.IsHeavyAttack
                : _isHeavy ? attack?.IsHeavyAttack == true
                : _isSkill ? state is PlayerSkillState
                : _isUltimate && state is PlayerUltimateState;
            bool followUp = _ultimateFollowUp && _actionExecutions > 0 && attack != null && !attack.IsCharging && !attack.IsHeavyAttack;
            if (!selected && !followUp)
            {
                _wasExecuting = false;
                _lastActionStep = -1;
                _lastActionTimer = -1f;
                return;
            }
            var stepField = followUp ? typeof(BaseForm).GetField("_comboStep", Hidden) : _formStepField;
            int step = (int)stepField.GetValue(_form);
            float timer = (float)_formTimerField.GetValue(_form);
            if (!_wasExecuting || step != _lastActionStep || timer + 0.0001f < _lastActionTimer)
            {
                var steps = followUp ? _data.Actions.ComboSteps : _selectedSteps;
                string animation = !followUp && _ultimateFollowUp ? "instant buff"
                    : steps != null && step >= 0 && step < steps.Count ? steps[step].Execution.AnimationName : "none";
                int originalStep = step + 1;
                if (_isNormal && _actionId != "normal-combo") int.TryParse(_actionId.Substring("normal-".Length), out originalStep);
                string detail = _actionId + "; " + _form.GetType().Name + "; step=" + originalStep + "; animation=" + animation;
                if (followUp) AddEvent("follow-up-attack", detail, originalStep);
                else
                {
                    _actionExecutions++;
                    AddEvent("action-executed", detail, originalStep);
                    if (_isUltimate && _ultimateFollowUp)
                        _attackDistance = EstimateAttackDistance(_data.Actions.ComboSteps != null && _data.Actions.ComboSteps.Count > 0 ? _data.Actions.ComboSteps[0] : null);
                }
            }
            _wasExecuting = true;
            _lastActionStep = step;
            _lastActionTimer = timer;
        }

        private void AddMovementKeys(Vector3 direction, List<Key> keys)
        {
            Transform view = _player.CameraTransform != null ? _player.CameraTransform : (Camera.main != null ? Camera.main.transform : _player.transform);
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float x = Vector3.Dot(direction, right), y = Vector3.Dot(direction, forward);
            if (x > 0.35f) keys.Add(Key.D); else if (x < -0.35f) keys.Add(Key.A);
            if (y > 0.35f) keys.Add(Key.W); else if (y < -0.35f) keys.Add(Key.S);
        }

        private static bool IsLiving(Enemy enemy) => enemy != null && enemy.isActiveAndEnabled
            && enemy.EnemyStat != null && enemy.EnemyStat.CurrentHp > 0f;

        private Enemy NearestEnemy()
        {
            Enemy nearest = null;
            float best = float.PositiveInfinity;
            foreach (var enemy in _enemies)
            {
                if (!IsLiving(enemy)) continue;
                float distance = (enemy.transform.position - _player.transform.position).sqrMagnitude;
                if (distance < best) { nearest = enemy; best = distance; }
            }
            return nearest;
        }

        private void SubscribeEnemies()
        {
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                if (IsLiving(enemy) && _enemies.Add(enemy)) enemy.OnDamaged += OnEnemyDamaged;
        }

        private void OnEnemyDamaged(float damage, ElementType element, bool critical)
        {
            if (_disposed || _complete) return;
            _metrics.hits++;
            _metrics.damageDealt += damage;
            if (_metrics.firstHitSeconds < 0f) _metrics.firstHitSeconds = Time.realtimeSinceStartup - _startedAt;
            AddEvent("enemy-damage", element + (critical ? " critical" : ""), damage);
        }

        private void OnPlayerDamaged(float damage, ElementType element, bool critical)
        {
            if (_disposed || _complete) return;
            _metrics.damageTaken += damage;
            AddEvent("player-damage", element.ToString(), damage);
        }

        private void AddEvent(string kind, string detail, float value = 0f)
        {
            _metrics.events.Add(new CombatEvent { realTime = Time.realtimeSinceStartup - _startedAt,
                gameTime = Time.time - _startedGameTime, kind = kind, detail = detail, value = value });
        }

        private void Finish(string reason)
        {
            if (_complete) return;
            _complete = true;
            _metrics.elapsedSeconds = Time.realtimeSinceStartup - _startedAt;
            Value("runtime.action.executions", _actionExecutions);
            if (_actionExecutions == 0)
            {
                _executionError = "The requested action '" + _actionId + "' did not execute. Check scene approach distance, action input bindings, resource requirements and test duration. " + reason;
                _metrics.notes.Add(_executionError);
                AddEvent("action-not-executed", _actionId);
            }
            AddEvent("session-end", reason);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            InputSystem.onBeforeUpdate -= BeforeInputUpdate;
            foreach (var enemy in _enemies) if (enemy != null) enemy.OnDamaged -= OnEnemyDamaged;
            if (_player != null && _player.StatManager != null) _player.StatManager.OnDamageTaken -= OnPlayerDamaged;
            Cleanup(() =>
            {
                if (_player != null && _form != null && _player.FormManager != null)
                {
                    if (_player.StateMachine?.CurrentState is PlayerAttackState
                        || _player.StateMachine?.CurrentState is PlayerSkillState
                        || _player.StateMachine?.CurrentState is PlayerUltimateState)
                        _player.StateMachine.ChangeState(new PlayerIdleState(_player));
                    _form.CleanupTransientEffects();
                    if (_player.FormManager.CurrentForm == _form) _player.FormManager.ChangeForm(_originalForm);
                    _registeredForms?.Remove(_form);
                }
            });
            Cleanup(() =>
            {
                if (!_inputChanged || _input == null) return;
                _input.enabled = false;
                foreach (var original in _originalReferences) original.Key.SetValue(_input, original.Value);
                _input.enabled = _oldInputEnabled;
                foreach (var original in _originalActionStates)
                { if (original.Value) original.Key.Enable(); else original.Key.Disable(); }
            });
            Cleanup(() =>
            {
                if (!_resourcesPrepared || _player == null || _player.StatManager == null) return;
                var stats = _player.StatManager;
                _spField.SetValue(stats, _oldSp);
                _spRegenField.SetValue(stats, _oldSpRegenTimer);
                _ultimateGaugeField.SetValue(stats, _oldUltimateGauge);
                // Public zero-delta notifications refresh the HUD after resource restoration.
                stats.AddSp(0f);
                stats.AddUltimateGauge(0f);
            });
            Cleanup(() => { if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard); });
            Cleanup(() => { if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse); });
            Cleanup(() => { if (_inputSettingsChanged && _originalInputSettings != null) InputSystem.settings = _originalInputSettings; });
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                var owned = _owned[i];
                Cleanup(() => { if (owned != null) Object.DestroyImmediate(owned); });
            }
            Time.timeScale = _oldTimeScale;
            Application.runInBackground = _oldRunInBackground;
            UnityEngine.Random.state = _oldRandomState;
        }

        private void Cleanup(Action operation)
        {
            try { operation(); }
            catch (Exception exception) { _metrics.notes.Add("Cleanup warning: " + exception.Message); }
        }
    }
}
