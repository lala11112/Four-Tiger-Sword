using System;
using System.Collections.Generic;
public class FormManager
{
    private PlayerController _playerController;
    public IForm CurrentForm{ get; private set; }
    private List<FormTransition> _transitions = new List<FormTransition>();
    private readonly Dictionary<Type, List<FormTransition>> _fastTransitions = new Dictionary<Type, List<FormTransition>>();


    public Func<bool> CanTransition{ get; set; }
    public event Action<IForm> OnFormChanged;

    public FormManager(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Update()
    {
        foreach (var transition in _transitions)
            transition.TargetForm.UpdateCooldowns();

        if(CanTransition != null && !CanTransition.Invoke())
        {
            return;
        }

        var toTransition = GetTransition();
        if(toTransition != null)
        {
            ChangeForm(toTransition.TargetForm);
        }
    }

    public void AddTransition(IForm targetForm, Func<bool> condition)
    {
        _transitions.Add(new FormTransition(targetForm, condition));
    }

    public void AddFastTransition(IForm fromForm, IForm toForm, Func<bool> condition)
    {
        if (!_fastTransitions.TryGetValue(fromForm.GetType(), out var transitions))
        {
            transitions = new List<FormTransition>();
            _fastTransitions[fromForm.GetType()] = transitions;
        }
        transitions.Add(new FormTransition(toForm, condition));
    }

    public void ChangeForm(IForm form)
    {
        if(CurrentForm == form) return;

        CurrentForm?.Unequip(_playerController);
        CurrentForm = form;
        CurrentForm?.Equip(_playerController);
        OnFormChanged?.Invoke(CurrentForm);
    }

    private FormTransition GetTransition()
    {
        foreach(var transition in _transitions)
        {
            if(transition.Condition.Invoke())
            {
                return transition;
                break;
            }
        }

        if(CurrentForm != null && _fastTransitions.TryGetValue(CurrentForm.GetType(), out var currentTransitions))
        {
            foreach(var transition in currentTransitions)
            {
                if(transition.Condition.Invoke())
                {
                    return transition;
                }
            }
        }

        return null;
    }
}
