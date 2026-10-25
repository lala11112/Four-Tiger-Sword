using System;
using System.Collections.Generic;

public class StateMachine
{
    public IPlayerState CurrentState{get; private set;}
    private readonly Dictionary<Type, List<Transition>> _transitions = new Dictionary<Type, List<Transition>>();
    private List<Transition> _anyTransitions = new List<Transition>();

    public void Update(bool allowActionTransitions = true, bool updateState = true)
    {
        var transition = GetTransition(allowActionTransitions);
        if (transition != null)
        {
            ChangeState(transition.To);
        }

        if (updateState) CurrentState?.Update();
    }

    public void ChangeState(IPlayerState state)
    {
        if (CurrentState?.GetType() == state.GetType()) return;

        CurrentState?.Exit();
        CurrentState = state;
        CurrentState?.Enter();
    }

    public void AddTransition(IPlayerState from, IPlayerState to, Func<bool> condition)
    {
        if (!_transitions.TryGetValue(from.GetType(), out var transitions))
        {
            transitions = new List<Transition>();
            _transitions[from.GetType()] = transitions;
        }
        transitions.Add(new Transition(to, condition));
    }

    public void AddAnyTransition(IPlayerState to, Func<bool> condition)
    {
        _anyTransitions.Add(new Transition(to, condition));
    }

    private Transition GetTransition(bool allowActionTransitions)
    {
        foreach (var transition in _anyTransitions)
        {
            if (transition.Condition())
            {
                return transition;
            }
        }

        if(allowActionTransitions && CurrentState != null && _transitions.TryGetValue(CurrentState.GetType(), out var currentTransitions))
        {
            foreach(var transition in currentTransitions)
            {
                if(transition.Condition())
                {
                    return transition;
                }
            }
        }

        return null;
    }
}
