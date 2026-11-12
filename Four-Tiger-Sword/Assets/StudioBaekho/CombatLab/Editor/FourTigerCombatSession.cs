using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace StudioBaekho.CombatLab.FourTigerSword
{
    // Lives in Assets/Editor so it can reference the game's Assembly-CSharp types.
    // A fresh Play Mode session is required for every candidate; this is not a world resetter.
    public sealed class FourTigerCombatSession : ICombatLabArmedSession, ICombatLabReadySession
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<FieldInfo, InputActionReference> _originalReferences = new Dictionary<FieldInfo, InputActionReference>();
        private readonly Dictionary<InputAction, bool> _originalActionStates = new Dictionary<InputAction, bool>();
        private readonly HashSet<Enemy> _enemies = new HashSet<Enemy>();
        private readonly CombatMetrics _metrics = new CombatMetrics();
        private readonly float _duration;
        private float _startedAt;
        private float _startedGameTime;
        private readonly bool _fixedScenario;
        private readonly bool _v2;
        private bool _armed;
        private int _followUpExecutions;
        private Enemy _fixedTarget;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private float _targetHp;
        private bool _targetPassive;
        private string _cameraTargetId;
        private readonly HashSet<string> _confirmedHitIds = new HashSet<string>();
        private readonly List<GameObject> _hiddenEnemies = new List<GameObject>();
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
        private float _oldUltimateGauge;
        private FieldInfo _ultimateGaugeField;
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
        private int _lastReadyFrame = -1, _stableReadyFrames;
        private Vector3 _readyPlayerPosition, _readyTargetPosition, _readyCameraPosition;
        private Quaternion _readyCameraRotation;
        private float _readyCameraFov;

        public bool Complete => _complete;
        public CombatMetrics Metrics => _metrics;

        public FourTigerCombatSession(ExperimentRequest request, CombatCandidate candidate)
        {
            _actionId = FourTigerCombatAdapter.NormalizeActionId(request.actionId);
            _v2 = request.schemaVersion == 2;
            _fixedScenario = _v2 && request.scenarioMode == "fixed";
            _armed = !_v2;
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
                var data = FourTigerCombatAdapter.CloneAndApply(source, candidate, _owned, _actionId, request.schemaVersion);
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
                // Teleport before binding the temporary form or filling its resources.
                if (_fixedScenario) PrepareFixedTarget();
                _originalForm = _player.FormManager.CurrentForm;
                _player.FormManager.CleanupTransientEffects();
                _player.FormManager.ChangeForm(_form);
                PrepareResources();
                InstallVirtualInput();
                _player.StatManager.OnDamageTaken += OnPlayerDamaged;
                SubscribeEnemies();
                CombatFeedbackTelemetry.Recorded += OnTelemetry;
                Application.runInBackground = true;
                UnityEngine.Random.InitState(request.seed);
                var actualValues = _v2 ? FourTigerCombatAdapter.AppliedFeedbackValues(data, _isNormal ? "normal-combo" : _actionId) : null;
                foreach (var parameter in candidate.values)
                {
                    float actual = actualValues != null && actualValues.TryGetValue(parameter.id, out float observed) ? observed : parameter.value;
                    _metrics.appliedValues.Add(new ParameterValue(parameter.id, actual));
                    if (actual != parameter.value) _metrics.notes.Add(parameter.id + ": 요청 " + parameter.value + ", 적용 " + actual + ". 채널 모드 및 상속 우선순위에 따른 유효 필드 값입니다.");
                }
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
                    _metrics.notes.Add("Only E skills are requested. Their original cooldown remains active; no basic attacks are substituted while waiting.");
                if (candidate.values.Exists(value => value.id == FourTigerCombatAdapter.HitStopId))
                    _metrics.notes.Add("The selected action's Feedback.HitStop duration is applied to every effective hit, including hit overrides. Each hit's original effective time scale is preserved. Player defaults, parries and other actions remain unchanged; exported form copies retain the action overrides.");
                _metrics.notes.Add(_fixedScenario ? "고정 표적: 같은 초기 방향·거리에서 선택 동작을 한 번 실행합니다. 표적 AI는 수동 상태이며 피격·넉백은 유지됩니다. 표적 런타임 HP만 1,000,000으로 높여 연타 도중 사망을 방지합니다. 프레임과 물리는 완전 결정적이지 않습니다."
                    : "The whole scene's enemy AI remains active. Scene startup, physics, navigation and frame timing are not deterministic; the seed does not guarantee identical encounters.");
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

        public void Arm()
        {
            if (_armed) return;
            if (_fixedScenario)
            {
                if (!IsReadyToArm(out string reason))
                    throw new InvalidOperationException("고정 시작 조건이 아직 안정되지 않았습니다: " + reason);
            }
            _startedAt = Time.realtimeSinceStartup;
            _startedGameTime = Time.time;
            if (_fixedScenario) RecordFixtureConditions();
            _armed = true;
            _nextPressAt = _startedAt + .3f; // Preserve actual pre-impact capture samples.
            AddEvent("recording-armed", "녹화 준비 이후 입력 시작");
        }

        public bool IsReadyToArm(out string reason)
        {
            reason = null;
            if (!_fixedScenario) return true;
            SubscribeEnemies();
            var camera = Camera.main;
            if (_disposed || _player == null || !_player.IsGround() || !FourTigerCombatAdapter.IsFixedTargetReady(_fixedTarget) || camera == null)
            { _stableReadyFrames = 0; reason = "살아 있는 표적, 접지된 플레이어와 Camera.main이 필요합니다."; return false; }
            if (_lastReadyFrame != Time.frameCount)
            {
                bool stable = _lastReadyFrame >= 0
                    && Vector3.Distance(_readyPlayerPosition, _player.transform.position) <= .002f
                    && Vector3.Distance(_readyTargetPosition, _fixedTarget.transform.position) <= .002f
                    && Vector3.Distance(_readyCameraPosition, camera.transform.position) <= .002f
                    && Quaternion.Angle(_readyCameraRotation, camera.transform.rotation) <= .02f
                    && Mathf.Abs(_readyCameraFov - camera.fieldOfView) <= .01f;
                _stableReadyFrames = stable ? _stableReadyFrames + 1 : 0;
                _lastReadyFrame = Time.frameCount;
                _readyPlayerPosition = _player.transform.position; _readyTargetPosition = _fixedTarget.transform.position;
                _readyCameraPosition = camera.transform.position; _readyCameraRotation = camera.transform.rotation; _readyCameraFov = camera.fieldOfView;
            }
            reason = "플레이어·표적·렌더 카메라의 연속 안정 프레임=" + _stableReadyFrames + "/3";
            return _stableReadyFrames >= 3;
        }

        private void PrepareFixedTarget()
        {
            _fixedTarget = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None)
                .Where(FourTigerCombatAdapter.IsFixedTargetReady)
                .OrderBy(e => FixedTargetKey(e.transform), StringComparer.Ordinal).FirstOrDefault();
            if (_fixedTarget == null)
                throw new InvalidOperationException("고정 표적 준비 실패: 활성 Enemy 콜라이더·설정된 EnemyStat·NavMesh 위의 Agent를 갖춘 살아 있는 적이 없습니다.");
            _targetPosition = _fixedTarget.transform.position; _targetRotation = _fixedTarget.transform.rotation;
            _targetHp = _fixedTarget.EnemyStat.CurrentHp; _targetPassive = _fixedTarget.CombatLabPassive;
            _fixedTarget.CombatLabPassive = true;
            SubscribeEnemies(); // Isolate invalid pool entries as well as other configured enemies.
            var agent = _fixedTarget.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            PlacePlayerForFixedTarget(filter);
            Vector3 desired = _player.transform.position + _player.transform.forward * (_attackDistance * .9f);
            var playerController = _player.Controller;
            var playerScale = _player.transform.lossyScale;
            desired.y += _player.transform.TransformVector(playerController.center).y
                - playerController.height * Mathf.Abs(playerScale.y) * .5f
                - playerController.skinWidth * Mathf.Abs(playerScale.y);
            if (!NavMesh.SamplePosition(desired, out var sample, .75f, filter))
                throw new InvalidOperationException("고정 표적 위치에 NavMesh가 없습니다. 열린 평지에서 실험하세요.");
            if (!agent.Warp(sample.position))
                throw new InvalidOperationException("고정 표적 위치 이동에 실패했습니다.");
            _fixedTarget.transform.rotation = Quaternion.LookRotation(-_player.transform.forward);
            _fixedTarget.EnemyStat.CurrentHp = 1000000f;
            Physics.SyncTransforms();
            foreach (var camera in Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                bool tracksPlayer = camera.Follow != null && (camera.Follow == _player.transform || camera.Follow.IsChildOf(_player.transform));
                tracksPlayer |= camera.LookAt != null && (camera.LookAt == _player.transform || camera.LookAt.IsChildOf(_player.transform));
                if (!tracksPlayer) continue;
                camera.GetComponent<CombatCameraDirector>()?.Cancel(_player, true);
                camera.PreviousStateIsValid = false;
                camera.UpdateCameraState(Vector3.up, -1f);
            }
            _metrics.notes.Add("고정 표적 선택 키: " + FixedTargetKey(_fixedTarget.transform)
                + ". NavMesh 안전 위치로 플레이어를 배치하고 카메라 이력을 초기화한 뒤, 접지와 연속 3프레임 안정 후 Arm 직전 조건을 관측합니다.");
        }

        private void PlacePlayerForFixedTarget(NavMeshQueryFilter filter)
        {
            var controller = _player.Controller;
            var scale = _player.transform.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(controller.height * Mathf.Abs(scale.y), radius * 2f);
            Vector3 center = _player.transform.TransformVector(controller.center);
            float skin = controller.skinWidth * Mathf.Abs(scale.y);
            var targetAgent = _fixedTarget.GetComponent<NavMeshAgent>();
            Vector3 floorProbe = _fixedTarget.transform.position
                - Vector3.up * (targetAgent.baseOffset * Mathf.Abs(_fixedTarget.transform.lossyScale.y));
            if (!NavMesh.SamplePosition(floorProbe, out var anchorHit, .75f, filter))
                throw new InvalidOperationException("표적 NavMeshAgent baseOffset을 제외한 바닥 기준 위치를 찾지 못했습니다.");
            Vector3 anchor = anchorHit.position;
            int groundLayer = LayerMask.NameToLayer("Ground");
            for (int ring = 0; ring < 3; ring++)
            for (int direction = 0; direction < 8; direction++)
            {
                Vector3 forward = Quaternion.Euler(0, direction * 45f, 0) * Vector3.forward;
                Vector3 desired = anchor - forward * (3f + ring * 1.5f);
                if (!NavMesh.SamplePosition(desired, out var sample, .5f, filter)
                    || NavMesh.Raycast(sample.position, anchor, out _, filter)) continue;
                Vector3 position = sample.position;
                position.y += height * .5f + skin - center.y;
                Vector3 worldCenter = position + center;
                Vector3 cap = Vector3.up * (height * .5f - radius);
                bool blocked = Physics.OverlapCapsule(worldCenter - cap, worldCenter + cap, radius,
                        ~(1 << groundLayer), QueryTriggerInteraction.Ignore)
                    .Any(c => c != null && c.GetComponentInParent<PlayerController>() != _player
                        && c.GetComponentInParent<Enemy>() != _fixedTarget);
                Vector3 targetDesired = sample.position + forward * (_attackDistance * .9f);
                if (blocked || !NavMesh.SamplePosition(targetDesired, out _, .3f, filter)
                    || NavMesh.Raycast(sample.position, targetDesired, out _, filter)) continue;
                _player.BeginStageTransition();
                _player.CompleteStageTransition(position, Quaternion.LookRotation(forward));
                _metrics.notes.Add("고정 실험 플레이어 배치: 표적 주변 NavMesh와 capsule 여유 공간을 검사했습니다. 높이=NavMesh.y + capsule높이/2 + skin - center.y, 위치=" + position + ".");
                return;
            }
            throw new InvalidOperationException("표적 주변에서 플레이어 capsule과 타격 방향을 확보할 NavMesh 안전 위치를 찾지 못했습니다.");
        }

        private void RecordFixtureConditions()
        {
            Vector3 player = _player.transform.position;
            Value("runtime.player.start.x", player.x); Value("runtime.player.start.y", player.y); Value("runtime.player.start.z", player.z);
            Value("runtime.fixture.distance", Vector3.Distance(_fixedTarget.transform.position, _player.transform.position));
            Vector3 actual = _fixedTarget.transform.position;
            Value("runtime.fixture.target.x", actual.x); Value("runtime.fixture.target.y", actual.y); Value("runtime.fixture.target.z", actual.z);
            Value("runtime.fixture.playerYaw", _player.transform.eulerAngles.y);
            var renderCamera = Camera.main;
            if (renderCamera != null)
            {
                Vector3 position = renderCamera.transform.position;
                Value("runtime.fixture.camera.x", position.x); Value("runtime.fixture.camera.y", position.y); Value("runtime.fixture.camera.z", position.z);
                Value("runtime.fixture.cameraPitch", renderCamera.transform.eulerAngles.x);
                Value("runtime.fixture.cameraYaw", renderCamera.transform.eulerAngles.y);
                Value("runtime.fixture.cameraRoll", renderCamera.transform.eulerAngles.z);
                Value("runtime.fixture.cameraFov", renderCamera.fieldOfView);
            }
            else _metrics.notes.Add("고정 표적 Arm 시 Camera.main을 찾지 못해 렌더 카메라 조건을 기록하지 못했습니다.");
            _metrics.notes.Add("fixture 관측 시점: 최소 3개의 실제 게임 프레임 동안 접지·위치·카메라 안정 확인 후 Arm 직전, 0.3초 입력 지연 이전입니다.");
            AddEvent("fixed-target-ready", FixedTargetKey(_fixedTarget.transform));
        }

        private static string FixedTargetKey(Transform value)
        {
            var segments = new List<string>();
            for (var current = value; current != null; current = current.parent)
                segments.Add(current.name + "[" + current.GetSiblingIndex().ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + "]");
            segments.Reverse();
            return value.gameObject.scene.path + ":" + string.Join("/", segments);
        }
        private static string HierarchyPath(Transform value)
            => value.parent == null ? value.name : HierarchyPath(value.parent) + "/" + value.name;

        private void OnTelemetry(CombatFeedbackTelemetryEvent evt)
        {
            if (_disposed || _complete || !_armed || _player == null) return;
            // Camera/clock events may belong to the victim; confirmed hits must be ours.
            if (evt.kind == "confirmed-impact" && evt.ownerId != _player.gameObject.GetInstanceID()) return;
            int step = _formStepField == null ? 0 : Math.Max(0, (int)_formStepField.GetValue(_form));
            if (_ultimateFollowUp && _player.StateMachine.CurrentState is PlayerAttackState)
                step = Math.Max(0, (int)typeof(BaseForm).GetField("_comboStep", Hidden).GetValue(_form));
            if (evt.kind == "confirmed-impact" && !string.IsNullOrEmpty(evt.detail))
            {
                int marker = evt.detail.IndexOf("step=", StringComparison.Ordinal);
                if (marker >= 0 && int.TryParse(evt.detail.Substring(marker + 5).Split(';')[0], out int actualStep)) step = actualStep;
            }
            string target = "step." + step + (evt.hitIndex < 0 ? "" : ".hit." + evt.hitIndex);
            if (evt.kind == "camera-begin") _cameraTargetId = target;
            else if (evt.kind == "camera-output" || evt.kind.StartsWith("camera-return", StringComparison.Ordinal) || evt.kind == "camera-end")
                target = _cameraTargetId ?? target;
            if (evt.kind == "confirmed-impact") _confirmedHitIds.Add(target);
            _metrics.events.Add(new CombatEvent { kind = evt.kind, detail = evt.detail,
                eventId = "execution-" + _actionExecutions + "/" + target + "/event-" + evt.sequence,
                targetId = target, value = evt.value, realTime = (float)(evt.realTime - _startedAt),
                gameTime = evt.gameTime - _startedGameTime, unityFrameCount = evt.unityFrameCount,
                unityRealtimeUs = evt.realTime * 1000000d, ownerInstanceId = evt.ownerId, targetInstanceId = evt.targetId,
                position = evt.position, rotation = evt.rotation, fov = evt.fov });
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
            _ultimateGaugeField = typeof(PlayerStatManager).GetField("_currentUltimateGauge", Hidden);
            if (_ultimateGaugeField == null)
                throw new InvalidOperationException("The adapter can no longer snapshot and restore skill/ultimate resources.");
            _oldUltimateGauge = stats.CurrentUltimateGauge;
            _resourcesPrepared = true;
            if (_isUltimate)
            {
                stats.AddUltimateGauge(stats.MaxUltimateGauge);
                _metrics.notes.Add("Scenario setup fills the ultimate gauge once. R is requested until its execution is observed, then never toggled off or refilled by the policy. The original gauge is restored on disposal.");
            }
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
            if (_fixedScenario)
            {
                validEnemy = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None).Any(FourTigerCombatAdapter.IsFixedTargetReady);
                if (!validEnemy)
                    throw new InvalidOperationException("고정 표적 준비 대기: 활성 Enemy 콜라이더와 설정된 EnemyStat, NavMesh 위의 Agent를 갖춘 살아 있는 적이 필요합니다.");
            }
            else
            {
                foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                {
                    if (!IsLiving(enemy)) continue;
                    var agent = enemy.GetComponent<NavMeshAgent>();
                    if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                        throw new InvalidOperationException("Every active enemy needs an enabled NavMeshAgent on a baked NavMesh for meaningful knockback comparisons: " + enemy.name);
                    validEnemy = true;
                }
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
            if (!_fixedScenario)
            {
                var position = _player.transform.position;
                Value("runtime.player.start.x", position.x);
                Value("runtime.player.start.y", position.y);
                Value("runtime.player.start.z", position.z);
            }
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
            if (_disposed || !_armed) return;
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
            if (_disposed || !_armed || !Application.isPlaying || InputState.currentUpdateType != InputUpdateType.Dynamic
                || _keyboard == null || _mouse == null || _lastInputFrame == Time.frameCount) return;
            if (_fixedScenario && _fixedTarget != null) SubscribeEnemies();
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
                    if (!_fixedScenario && locomotion && !nearTarget) AddMovementKeys(offset.normalized, keys);
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
                else if (Time.realtimeSinceStartup >= _nextPressAt)
                {
                    bool normalPolicy = _isNormal || (_ultimateFollowUp && _actionExecutions > 0);
                    // Fixed trials execute once from the prepared fixture. Collider separation or a
                    // short hitbox must not silently suppress the requested action before measurement.
                    bool startAction = locomotion && target != null && (_fixedScenario || nearTarget);
                    if (_fixedScenario && (_ultimateFollowUp ? _followUpExecutions > 0 : _actionExecutions > 0)) startAction = false;
                    // Range gates the start of a new action only. Knockback or a defeated target
                    // must not suppress the remaining inputs of an already-started full combo.
                    // CanChainAttack also returns true on the final step, so check that a next
                    // step exists instead of leaving an extra buffered tap for a new first hit.
                    bool continueCombo = normalPolicy && (_actionId == "normal-combo" || _ultimateFollowUp)
                        && _player.StateMachine.CurrentState is PlayerAttackState attack
                        && !attack.IsCharging && !attack.IsHeavyAttack
                        && _lastActionStep >= 0 && _lastActionStep < _data.Actions.ComboSteps.Count - 1
                        && _form.CanChainAttack;
                    if (normalPolicy && (startAction || continueCombo))
                    {
                        pressed = true;
                        RequestInput("Virtual mouse tap", _ultimateFollowUp ? "follow-up-request" : "attack-request");
                    }
                    else if (_isHeavy && startAction)
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
                    else if (_isSkill && startAction && _form.CanSkill)
                    {
                        keys.Add(Key.E);
                        RequestInput("Virtual E press", "skill-request");
                    }
                    else if (_isUltimate && _actionExecutions == 0 && startAction && _form.CanUltimate)
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
                if (followUp) { _followUpExecutions++; AddEvent("follow-up-attack", detail, originalStep); }
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
            if (_fixedScenario && _fixedTarget != null)
            {
                foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                {
                    if (enemy == _fixedTarget) continue;
                    if (_enemies.Remove(enemy)) enemy.OnDamaged -= OnEnemyDamaged;
                    if (!enemy.gameObject.activeSelf) continue;
                    if (!_hiddenEnemies.Contains(enemy.gameObject)) _hiddenEnemies.Add(enemy.gameObject);
                    enemy.gameObject.SetActive(false);
                }
                if (IsLiving(_fixedTarget) && _enemies.Add(_fixedTarget)) _fixedTarget.OnDamaged += OnEnemyDamaged;
                return;
            }
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                if ((_fixedScenario ? FourTigerCombatAdapter.IsFixedTargetReady(enemy) : IsLiving(enemy))
                    && _enemies.Add(enemy)) enemy.OnDamaged += OnEnemyDamaged;
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
                gameTime = Time.time - _startedGameTime, kind = kind, detail = detail, value = value,
                unityFrameCount = Time.frameCount, unityRealtimeUs = Time.realtimeSinceStartupAsDouble * 1000000d });
        }

        private void Finish(string reason)
        {
            if (_complete) return;
            _complete = true;
            _metrics.elapsedSeconds = Time.realtimeSinceStartup - _startedAt;
            Value("runtime.action.executions", _actionExecutions);
            if (_fixedScenario)
            {
                int expected = FourTigerCombatAdapter.ExpectedFeedbackHits(_data, _isNormal ? "normal-combo" : _actionId);
                _metrics.expectedHitEvents = expected;
                _metrics.confirmedHitEvents = _confirmedHitIds.Count;
                _metrics.validationStatus = _confirmedHitIds.Count < expected ? "incomplete-hits" : "completed";
                if (_metrics.validationStatus != "completed")
                    _metrics.notes.Add("예상 타격 이벤트 " + expected + "개 중 " + _confirmedHitIds.Count + "개만 실제 명중했습니다. 거리·넉백·실행 길이를 확인하세요.");
            }
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
            CombatFeedbackTelemetry.Recorded -= OnTelemetry;
            Cleanup(() => CombatHitStopService.ReleaseOwner(_player));
            Cleanup(() => {
                if (_fixedTarget != null) {
                    _fixedTarget.CombatLabPassive = _targetPassive;
                    _fixedTarget.EnemyStat.CurrentHp = _targetHp;
                    var nav = _fixedTarget.GetComponent<NavMeshAgent>();
                    if (nav != null && nav.enabled && nav.isOnNavMesh) nav.Warp(_targetPosition);
                    _fixedTarget.transform.rotation = _targetRotation;
                }
                foreach (var hidden in _hiddenEnemies) if (hidden != null) hidden.SetActive(true);
            });
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
                _ultimateGaugeField.SetValue(stats, _oldUltimateGauge);
                // Public zero-delta notifications refresh the HUD after resource restoration.
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
            // Stop ownership is released above. Do not resume an external pause on disposal.
            if (!_v2) Time.timeScale = _oldTimeScale;
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
