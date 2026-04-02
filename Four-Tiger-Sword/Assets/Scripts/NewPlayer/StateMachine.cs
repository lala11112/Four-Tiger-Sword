using System;
using System.Collections.Generic;

public class StateMachine
{
    public IPlayerState _currentState;
    private readonly Dictionary<Type, List<Transition>> _transitions = new Dictionary<Type, List<Transition>>();
    private List<Transition> _anyTransitions = new List<Transition>();

    public void Update()
    {
        var transition = GetTransition();
        if (transition != null)
        {
            ChangeState(transition.To);
        }

        _currentState?.Update();
    }

    public void ChangeState(IPlayerState state)
    {
        if (_currentState?.GetType() == state.GetType()) return;

        _currentState?.Exit();
        _currentState = state;
        _currentState?.Enter();
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

    private Transition GetTransition()
    {
        foreach (var transition in _anyTransitions)
        {
            if (transition.Condition())
            {
                return transition;
            }
        }

        if(_currentState != null && _transitions.TryGetValue(_currentState.GetType(), out var currentTransitions))
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