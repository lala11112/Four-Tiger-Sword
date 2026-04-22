using System;
using System.Collections.Generic;
public class FormManager
{
    private PlayerController _playerController;
    public IForm CurrentForm{ get; private set; }
    private List<FormTransition> _transitions = new List<FormTransition>();

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

        foreach(var transition in _transitions)
        {
            if(transition.Condition.Invoke())
            {
                ChangeForm(transition.TargetForm);
                break;
            }
        }
    }

    public void AddTransition(IForm targetForm, Func<bool> condition)
    {
        _transitions.Add(new FormTransition(targetForm, condition));
    }

    public void ChangeForm(IForm form)
    {
        if(CurrentForm == form) return;

        CurrentForm?.Unequip(_playerController);
        CurrentForm = form;
        CurrentForm?.Equip(_playerController);
        OnFormChanged?.Invoke(CurrentForm);
    }
}
