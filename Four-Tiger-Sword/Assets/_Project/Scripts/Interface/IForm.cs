public interface IForm : IFormLifecycle, IFormCooldown, IFormActions
{
    ElementType Element { get; }
    void DrawHitboxGizmo();
}
