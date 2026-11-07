// Unity Pipeline run_script: invoke Start in Game Scene Play Mode, then poll ResultKey.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ActionTimingPlayModeRegression
{
    public const string ResultKey = "ActionTiming.PlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Isolation = new Vector3(12345f, 200f, 12345f);
    private static readonly StringBuilder Report = new StringBuilder();
    private static DateTime deadline;
    private static PlayerController player;
    private static Action endAction;
    private static int checks, firstFrame;
    private static bool running;

    private sealed class NeutralState : IPlayerState
    { public void Enter() { } public void Update() { } public void Exit() { } }

    private sealed class Saved
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Gravity, Vertical, Speed, Hp, Sp, Ultimate, SpRegen;
        public uint AttackPressId;
        public bool PlayerEnabled, InputEnabled, ControllerEnabled, RootMotion;
        public AnimatorCullingMode Culling;
        public IForm Form;
        public readonly List<(BaseForm Form, double Sp, bool Exhausted, float Skill, float Ultimate)> Forms
            = new List<(BaseForm, double, bool, float, float)>();
    }

    public static string Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Game Scene Play Mode first.");
        if (running) return "Action timing Play Mode regression is already running.";
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (player == null || player.FormManager == null || player.Animator == null)
            throw new InvalidOperationException("The real Game Scene player and Animator must be ready.");
        running = true; checks = 0; firstFrame = Time.frameCount;
        deadline = DateTime.UtcNow.AddSeconds(180); Report.Clear();
        SessionState.SetString(ResultKey, "RUNNING: preparing real Game Scene player.");
        player.StartCoroutine(Run());
        return "Action timing Play Mode regression started; poll " + ResultKey + ".";
    }

    private static IEnumerator Run()
    {
        Saved saved = null;
        try { saved = Capture(); }
        catch (Exception exception)
        {
            running = false;
            SessionState.SetString(ResultKey, "FAIL: live fixture capture: " + exception);
        }
        if (saved == null) yield break;
        Exception failure = null;
        var stack = new Stack<IEnumerator>();
        stack.Push(Exercise());
        // Flatten nested iterators so every case exception reaches cleanup and the pollable result.
        while (stack.Count > 0)
        {
            object yielded = null;
            try
            {
                if (!Application.isPlaying || DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Live action timing frames timed out.");
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                yielded = current.Current;
                if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
            }
            catch (Exception exception) { failure = exception; break; }
            yield return yielded;
        }
        try { Restore(saved); }
        catch (Exception exception) { failure = failure ?? exception; }
        running = false;
        string result = failure == null
            ? $"PASS: {checks} checks across {Time.frameCount - firstFrame} real frames.\n" + Report
            : "FAIL: " + failure + "\nCompleted cases:\n" + Report;
        SessionState.SetString(ResultKey, result);
        Debug.Log(result);
    }

    private static Saved Capture()
    {
        var saved = new Saved
        {
            Position = player.transform.position, Rotation = player.transform.rotation,
            Gravity = player.Gravity, Vertical = player.VerticalVelocity,
            Speed = player.ActorClock != null ? player.ActorClock.AnimationSpeed : player.Animator.speed,
            PlayerEnabled = player.enabled, InputEnabled = player.Input.enabled,
            ControllerEnabled = player.Controller.enabled, RootMotion = player.Animator.applyRootMotion,
            Culling = player.Animator.cullingMode, Form = player.FormManager.CurrentForm,
            Hp = player.StatManager.CurrentHp, Sp = player.StatManager.CurrentSp,
            Ultimate = player.StatManager.CurrentUltimateGauge,
            SpRegen = Read<float>(player.StatManager, "_spRegenTimer"),
            AttackPressId = player.Input.AttackPressId
        };
        foreach (var form in Forms()) saved.Forms.Add((form, Read<double>(form, "_currentFormSp"),
            form.IsFormSpExhausted, Read<float>(form, "_skillCooldownTimer"), Read<float>(form, "_ultimateCooldownTimer")));
        return saved;
    }

    private static IEnumerator Exercise()
    {
        Check(player.enabled && player.gameObject.activeInHierarchy && !player.IsStageTransitioning
            && !player.IsGameplayModalPaused, "The live player is not available for action checks.");
        player.Input.enabled = false;
        player.enabled = false;
        player.Gravity = player.VerticalVelocity = 0f;
        player.Animator.applyRootMotion = false;
        player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        player.ActorClock?.ClearStops("action-timing-playmode-regression");
        var forms = Forms();
        Check(forms.Count == 5, "Expected five live registered forms.");
        foreach (var form in forms)
        {
            yield return NoInput(form, "normal1", 0);
            if (form is FireForm || form is WaterForm) yield return NoInput(form, "normal3", 2);
            yield return NoInput(form, "skill", 0);
            if (form is WaterForm || form is IronForm) yield return NoInput(form, "ultimate", 0);
        }
        var fire = forms.Find(form => form is FireForm);
        yield return NoInput(fire, "counter", 0, 1.4f);
        yield return NoInput(fire, "air-start", 0, 19f / 30f);
        yield return NoInput(fire, "air-finish", 0, 1.1f);
        foreach (var form in forms)
            if (form is FireForm || form is WaterForm)
            {
                yield return StateTransition(form, "skill-normal");
                yield return StateTransition(form, "skill-move");
                yield return StateTransition(form, "normal-skill");
            }
        foreach (var form in forms)
        {
            var data = Read<WeaponActionDataSO>(form, "_weaponActionData");
            for (int stage = 0; stage < Mathf.Min(3, data.ComboSteps.Count); stage++)
                yield return QueuedSkillTransition(form, stage);
        }
    }

    private static IEnumerator Prepare(BaseForm form, string label)
    {
        if (endAction != null) { endAction(); endAction = null; }
        player.StateMachine.ChangeState(new NeutralState());
        player.FormManager.CleanupTransientEffects();
        player.FormManager.ChangeForm(form);
        Write(form, "_skillCooldownTimer", 0f); Write(form, "_ultimateCooldownTimer", 0f);
        Write(form, "_currentFormSp", (double)form.MaxFormSp);
        typeof(BaseForm).GetProperty("IsFormSpExhausted").SetValue(form, false);
        player.StatManager.Heal(player.StatManager.MaxHp);
        player.StatManager.AddSp(player.StatManager.MaxSp);
        player.StatManager.AddUltimateGauge(player.StatManager.MaxUltimateGauge);
        ClearInput(); Move(Isolation); player.SetAnimationSpeed(1f);
        player.Animator.CrossFade("Idle", 0f, 0, 0f);
        SessionState.SetString(ResultKey, "RUNNING: " + form.GetType().Name + " " + label + "\n" + Report);
        yield return null; yield return null;
        Check(player.ActionDeltaTime > 0f, "The live actor clock is not advancing.");
    }

    private delegate void Tick(out bool complete);

    private static IEnumerator NoInput(BaseForm form, string kind, int stage, float knownClip = 0f)
    {
        yield return Prepare(form, kind);
        var data = Read<WeaponActionDataSO>(form, "_weaponActionData");
        WeaponActionData step;
        string animation;
        float execution;
        Tick tick;
        if (kind.StartsWith("normal", StringComparison.Ordinal))
        {
            step = data.ComboSteps[stage]; animation = step.AnimationName; execution = step.Duration;
            endAction = form.EndAttack; form.BeginAttack();
            if (stage > 0) { Write(form, "_comboStep", stage); Invoke(form, "PlayCombo"); }
            tick = form.UpdateAttack;
        }
        else if (kind == "skill")
        {
            step = data.SkillSteps[data.SkillSteps.Count - 1]; animation = step.AnimationName; execution = step.Duration;
            endAction = form.EndSkill; form.BeginSkill(); tick = form.UpdateSkill;
        }
        else if (kind == "ultimate")
        {
            step = data.UltimateSteps[data.UltimateSteps.Count - 1]; animation = step.AnimationName;
            execution = form is WaterForm ? 2f : step.Duration;
            endAction = form.EndUltimate; form.BeginUltimate(); tick = form.UpdateUltimate;
        }
        else if (kind == "counter")
        {
            step = data.CounterStep; animation = step.AnimationName; execution = step.Duration;
            endAction = form.EndCounter; form.BeginCounter(); tick = form.UpdateCounter;
        }
        else if (kind == "air-start")
        {
            step = null; animation = data.AirAttackStartAnimationName; execution = data.AirAttackStartDuration;
            endAction = form.EndAirAttack; form.BeginAirAttack(); tick = form.UpdateAirAttackStart;
        }
        else
        {
            step = data.AirAttackStep; animation = step.AnimationName; execution = step.Duration;
            endAction = form.EndAirAttack; form.BeginAirAttack(); form.BeginAirAttackFinish(); tick = form.UpdateAirAttack;
        }
        int startFrame = Time.frameCount, tailFrames = 0;
        float mechanicalAt = -1f, actionTime = 0f, clipLength = 0f, normalized = 0f;
        bool observed = false, moveOpened = false, actionOpened = false;
        Vector3 tailPosition = default;
        while (true)
        {
            yield return null;
            float delta = player.ActionDeltaTime * AnimationSpeed(form);
            actionTime += delta;
            tick(out bool complete);
            if (SampleAnimation(animation, out float length, out float normalizedTime))
            { observed = true; clipLength = length; normalized = normalizedTime; }
            if (mechanicalAt >= 0f)
            {
                Check(Horizontal(player.transform.position - tailPosition) <= .003f,
                    form.GetType().Name + " " + kind + " moved horizontally after mechanical completion.");
                tailFrames++;
            }
            else if (form.IsCurrentActionExecutionComplete)
            {
                mechanicalAt = actionTime; tailPosition = player.transform.position;
                Check(actionTime + .002f >= execution && actionTime <= execution + delta + .08f,
                    form.GetType().Name + " " + kind + " mechanical duration differs from the live data.");
            }
            if (kind == "skill")
            {
                float elapsed = Elapsed(form);
                if (form.CanCancelCurrentActionToMove)
                { Check(elapsed + .002f >= step.RecoveryCancelTime, "Skill movement gate opened early."); moveOpened = true; }
                if (form.CanCancelCurrentActionToAction)
                { Check(elapsed + .002f >= step.ActionCancelTime, "Skill combat gate opened early."); actionOpened = true; }
            }
            if (!complete) continue;
            Check(mechanicalAt >= 0f, kind + " completed before its execution-complete flag.");
            Check(Time.frameCount > startFrame + 1, kind + " did not advance through real frames.");
            if (observed) Check(normalized >= .9999f, form.GetType().Name + " " + kind + " ended before its matched animation finished.");
            if (knownClip > 0f)
                Check(observed && Mathf.Abs(clipLength - knownClip) < .04f, kind + " did not use the expected full live clip.");
            if (kind == "skill") Check(moveOpened && actionOpened, form.GetType().Name + " skill recovery gates never opened.");
            Report.AppendLine($"{form.GetType().Name} {kind}: execution={N(mechanicalAt)}, end={N(actionTime)}, "
                + $"clip={N(clipLength)}, normalized={N(normalized)}, tailFrames={tailFrames}, frames={Time.frameCount - startFrame}");
            endAction(); endAction = null;
            break;
        }
    }

    private static IEnumerator StateTransition(BaseForm form, string kind)
    {
        yield return Prepare(form, kind);
        bool fromSkill = kind.StartsWith("skill", StringComparison.Ordinal);
        var machine = player.StateMachine;
        IPlayerState source = fromSkill ? (IPlayerState)State<PlayerSkillState>() : State<PlayerAttackState>();
        machine.ChangeState(source);
        // A released Water tap resolves its production heavy-attack decision before the timed input.
        machine.Update(player.ActionDeltaTime > 0f, player.ActionDeltaTime > 0f);
        var data = Read<WeaponActionDataSO>(form, "_weaponActionData");
        var step = fromSkill ? data.SkillSteps[data.SkillSteps.Count - 1] : data.ComboSteps[0];
        float gate = kind == "skill-move" ? step.RecoveryCancelTime : step.ActionCancelTime;
        Check(gate >= 0f, "A live transition gate is disabled.");
        bool requested = false;
        int startFrame = Time.frameCount;
        float requestedAt = -1f, transitionAt = -1f;
        while (ReferenceEquals(machine.CurrentState, source))
        {
            yield return null;
            float elapsed = Elapsed(form);
            if (!requested && elapsed >= Mathf.Max(0f, gate - .06f))
            {
                requested = true; requestedAt = elapsed;
                if (kind == "skill-move") SetMove(Vector2.up);
                else if (kind == "skill-normal") player.Input.AttackBuffer.Set(.5f);
                else player.Input.SkillBuffer.Set(.15f);
            }
            UpdateBuffers();
            bool open = kind == "skill-move" ? form.CanCancelCurrentActionToMove : form.CanCancelCurrentActionToAction;
            machine.Update(player.ActionDeltaTime > 0f, player.ActionDeltaTime > 0f);
            if (!ReferenceEquals(machine.CurrentState, source))
            {
                transitionAt = elapsed;
                Check(requested && open && elapsed + .002f >= gate, kind + " transitioned before its independent gate.");
            }
        }
        Check(kind == "skill-move" ? machine.CurrentState is PlayerRunState
            : kind == "skill-normal" ? machine.CurrentState is PlayerAttackState : machine.CurrentState is PlayerSkillState,
            form.GetType().Name + " " + kind + " reached the wrong production state: " + machine.CurrentState.GetType().Name);
        if (kind != "skill-move") Check(kind == "skill-normal" ? !player.Input.AttackBuffer.IsActive
            : !player.Input.SkillBuffer.IsActive, "The executed live input buffer was not consumed.");
        Check(Time.frameCount > startFrame, "The production transition did not advance through real frames.");
        Report.AppendLine($"{form.GetType().Name} {kind}: input={N(requestedAt)}, gate={N(gate)}, "
            + $"transition={N(transitionAt)}, frames={Time.frameCount - startFrame}");
        ClearInput(); machine.ChangeState(new NeutralState());
    }

    private static IEnumerator QueuedSkillTransition(BaseForm form, int stage)
    {
        string label = "queued-normal" + (stage + 1) + "-skill";
        yield return Prepare(form, label);
        var machine = player.StateMachine;
        var attack = State<PlayerAttackState>();
        var data = Read<WeaponActionDataSO>(form, "_weaponActionData");
        var step = data.ComboSteps[stage];
        Check(step.ActionCancelTime >= 0f, label + " requires an enabled live action gate.");
        Check(player.CanSkill, form.GetType().Name + " skill is unavailable before reservation.");
        machine.ChangeState(attack);
        // Resolve a released Water tap, then begin the requested authored stage at time zero.
        machine.Update(player.ActionDeltaTime > 0f, player.ActionDeltaTime > 0f);
        Check(Read<bool>(attack, "_normalStarted") && !attack.IsHeavyAttack,
            label + " did not enter a normal attack.");
        Write(form, "_comboStep", stage); Invoke(form, "PlayCombo");
        float requestedAt = Elapsed(form), spent = 0f, waited = 0f, transitionAt = -1f;
        float firstNormalized = 0f, lastNormalized = 0f;
        bool observed = false;
        int startFrame = Time.frameCount, attackFrames = 0, spamTaps = 0, spends = 0;
        player.Input.SkillBuffer.Set(.15f);
        SetMove(Vector2.up); TapAttack(); spamTaps++;
        float before = player.StatManager.CurrentSp;
        // Invoke the production pre-transition hook through PlayerController.Update itself.
        // The component remains disabled so Unity cannot run a second controller tick.
        Invoke(player, "Update");
        Check(ReferenceEquals(machine.CurrentState, attack) && QueuedSkill(attack),
            label + " did not reserve the early E input through PlayerController.Update.");
        Check(!player.Input.SkillBuffer.IsActive && !player.Input.AttackBuffer.IsActive,
            label + " did not consume E and the competing normal tap at capture.");
        Check(Mathf.Abs(player.StatManager.CurrentSp - before) < .001f,
            label + " spent spirit before the queued skill executed.");

        while (ReferenceEquals(machine.CurrentState, attack))
        {
            yield return null;
            waited += player.ActionDeltaTime;
            attackFrames++;
            UpdateBuffers(); SetMove(Vector2.up); TapAttack(); spamTaps++;
            float elapsed = Elapsed(form);
            bool open = attack.CanCancelToAction;
            Check(form.CurrentComboStage == stage && QueuedSkill(attack) && attack.HasSkillRequest,
                label + " lost the reservation or advanced to another normal hit.");
            if (SampleAnimation(step.AnimationName, out _, out float normalized))
            {
                if (!observed) firstNormalized = normalized;
                observed = true; lastNormalized = normalized;
            }
            before = player.StatManager.CurrentSp;
            Invoke(player, "Update");
            float consumed = Mathf.Max(0f, before - player.StatManager.CurrentSp);
            if (consumed > .001f) { spent += consumed; spends++; }
            if (ReferenceEquals(machine.CurrentState, attack))
            {
                Check(form.CurrentComboStage == stage && QueuedSkill(attack),
                    label + " allowed normal spam to replace the reserved hit.");
                Check(!attack.CanCancelToMove && !player.Input.AttackBuffer.IsActive,
                    label + " allowed held movement or a competing normal tap to steal the reservation.");
                Check(consumed < .001f, label + " spent spirit while still attacking.");
                continue;
            }
            transitionAt = elapsed;
            Check(machine.CurrentState is PlayerSkillState && open && elapsed + .002f >= step.ActionCancelTime,
                label + " did not transition directly to skill at the same hit's action gate.");
            Check(!QueuedSkill(attack) && !attack.HasSkillRequest && !player.Input.AttackBuffer.IsActive,
                label + " left a reservation or competing tap after execution.");
            Check(Mathf.Abs(consumed - form.SkillSpCost) < .01f,
                label + " did not consume exactly one skill's spirit cost on entry.");
        }

        Check(waited > .15f && attackFrames > 1 && Time.frameCount > startFrame + 1,
            label + " did not retain E beyond the ordinary 0.15-second input buffer across real frames.");
        if (observed) Check(lastNormalized > firstNormalized + .001f,
            label + " did not advance the real attack Animator while E was reserved.");

        int skillEntries = 1, settledFrames = 0;
        // Keep movement held until skill recovery finishes, then watch for a leaked second cast.
        // Normal spam stops when skill wins; no second E is injected.
        while (settledFrames < 8)
        {
            yield return null;
            UpdateBuffers(); SetMove(Vector2.up);
            var previous = machine.CurrentState;
            before = player.StatManager.CurrentSp;
            Invoke(player, "Update");
            float consumed = Mathf.Max(0f, before - player.StatManager.CurrentSp);
            if (consumed > .001f) { spent += consumed; spends++; }
            if (machine.CurrentState is PlayerSkillState && !(previous is PlayerSkillState)) skillEntries++;
            // Isolation has no floor: Run legitimately becomes Fall on its next controller tick.
            bool recovered = machine.CurrentState is PlayerRunState || machine.CurrentState is PlayerFallState;
            Check(machine.CurrentState is PlayerSkillState || recovered,
                label + " leaked into another attack or unexpected state after casting: " + machine.CurrentState.GetType().Name);
            Check(!QueuedSkill(attack) && !attack.HasSkillRequest,
                label + " restored an already consumed skill reservation.");
            if (recovered) settledFrames++;
        }
        Check(skillEntries == 1 && Mathf.Abs(spent - form.SkillSpCost) < .01f
            && spends == (form.SkillSpCost > .001f ? 1 : 0),
            label + " executed or charged the reserved skill more than once.");
        Report.AppendLine($"{form.GetType().Name} {label}: input={N(requestedAt)}, gate={N(step.ActionCancelTime)}, "
            + $"transition={N(transitionAt)}, buffered={N(waited)}, spamTaps={spamTaps}, casts={skillEntries}, "
            + $"spSpent={N(spent)}, attackNormalized={N(firstNormalized)}->{N(lastNormalized)}, "
            + $"attackFrames={attackFrames}, frames={Time.frameCount - startFrame}, controllerUpdate=true");
        ClearInput(); machine.ChangeState(new NeutralState());
    }

    private static bool SampleAnimation(string name, out float length, out float normalized)
    {
        length = normalized = 0f;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var animator = player.Animator;
        bool next = animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(name);
        var state = next ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(name)) return false;
        foreach (var info in next ? animator.GetNextAnimatorClipInfo(0) : animator.GetCurrentAnimatorClipInfo(0))
            if (info.clip != null && info.clip.length > 0f) length = Mathf.Max(length, info.clip.length);
        normalized = state.normalizedTime;
        return length > 0f;
    }

    private static void Restore(Saved saved)
    {
        if (player == null || !Application.isPlaying) return;
        if (endAction != null) { endAction(); endAction = null; }
        ClearInput(); player.StateMachine.ChangeState(new NeutralState());
        player.FormManager.CleanupTransientEffects(); player.FormManager.ChangeForm(saved.Form);
        foreach (var state in saved.Forms)
        {
            Write(state.Form, "_currentFormSp", state.Sp);
            typeof(BaseForm).GetProperty("IsFormSpExhausted").SetValue(state.Form, state.Exhausted);
            Write(state.Form, "_skillCooldownTimer", state.Skill); Write(state.Form, "_ultimateCooldownTimer", state.Ultimate);
        }
        player.Controller.enabled = false;
        player.transform.SetPositionAndRotation(saved.Position, saved.Rotation);
        player.Controller.enabled = saved.ControllerEnabled; player.Gravity = saved.Gravity; player.VerticalVelocity = saved.Vertical;
        player.Animator.cullingMode = saved.Culling;
        player.SetAnimationSpeed(saved.Speed);
        Write(player.StatManager, "_currentHp", saved.Hp); Write(player.StatManager, "_currentSp", saved.Sp);
        Write(player.StatManager, "_currentUltimateGauge", saved.Ultimate);
        Write(player.StatManager, "_spRegenTimer", saved.SpRegen);
        typeof(PlayerInputHandler).GetProperty("AttackPressId").SetValue(player.Input, saved.AttackPressId);
        player.StateMachine.ChangeState(State<PlayerIdleState>());
        player.Animator.applyRootMotion = saved.RootMotion;
        player.enabled = saved.PlayerEnabled; player.Input.enabled = saved.InputEnabled;
        Physics.SyncTransforms();
    }

    private static List<BaseForm> Forms()
    {
        var forms = new List<BaseForm>();
        foreach (ElementType element in Enum.GetValues(typeof(ElementType)))
        { var form = player.FormManager.GetForm(element); if (form != null && !forms.Contains(form)) forms.Add(form); }
        return forms;
    }
    private static T State<T>() where T : class, IPlayerState
    {
        var transitions = Read<Dictionary<Type, List<Transition>>>(player.StateMachine, "_transitions");
        foreach (var list in transitions.Values) foreach (var transition in list) if (transition.To is T found) return found;
        throw new InvalidOperationException("Production state was not registered: " + typeof(T).Name);
    }
    private static void Move(Vector3 position)
    { player.Controller.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.identity);
      player.Controller.enabled = true; player.VerticalVelocity = 0f; Physics.SyncTransforms(); }
    private static void ClearInput()
    { Invoke(player.Input, "ClearGameplayInput"); SetMove(Vector2.zero); }
    private static void SetMove(Vector2 value) => typeof(PlayerInputHandler).GetProperty("MoveInput").SetValue(player.Input, value);
    private static void TapAttack()
    {
        typeof(PlayerInputHandler).GetProperty("AttackPressId").SetValue(player.Input,
            unchecked(player.Input.AttackPressId + 1u));
        player.Input.AttackBuffer.Set(.5f);
    }
    private static bool QueuedSkill(PlayerAttackState attack)
        => (bool)typeof(PlayerAttackState).GetProperty("HasQueuedSkill", Hidden).GetValue(attack);
    private static void UpdateBuffers()
    { float dt = player.ActionDeltaTime; player.Input.AttackBuffer.Update(dt); player.Input.SkillBuffer.Update(dt);
      player.Input.UltimateBuffer.Update(dt); player.Input.JumpBuffer.Update(dt); player.Input.DashBuffer.Update(dt); }
    private static float AnimationSpeed(BaseForm form)
        => (float)typeof(BaseForm).GetProperty("CurrentAttackAnimationSpeed", Hidden).GetValue(form);
    private static float Elapsed(BaseForm form)
        => (float)typeof(BaseForm).GetProperty("CurrentActionElapsed", Hidden).GetValue(form);
    private static FieldInfo Field(object target, string name)
    { for (var type = target.GetType(); type != null; type = type.BaseType)
      { var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly); if (field != null) return field; }
      throw new MissingFieldException(target.GetType().Name, name); }
    private static T Read<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void Write(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static object Invoke(object target, string name)
    { for (var type = target.GetType(); type != null; type = type.BaseType)
      { var method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly); if (method != null) return method.Invoke(target, null); }
      throw new MissingMethodException(target.GetType().Name, name); }
    private static float Horizontal(Vector3 value) => new Vector2(value.x, value.z).magnitude;
    private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; }
}
