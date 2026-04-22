using System;
using System.Collections.Generic;

public class FormTransition
{
    public IForm TargetForm{ get; private set; }
    public Func<bool> Condition{ get; private set; }
    public FormTransition(IForm targetForm, Func<bool> condition)
    {
        TargetForm = targetForm;
        Condition = condition;
    }
}