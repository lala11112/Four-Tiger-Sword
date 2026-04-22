public class FormManagerSetup
{
    private readonly PlayerController _playerController;

    public FormManagerSetup(PlayerController playerController) {_playerController = playerController;}

    public FormManager Build(WeaponActionDataSO FireFormActionData, WeaponActionDataSO WaterFormActionData, WeaponActionDataSO WoodFormActionData, WeaponActionDataSO IronFormActionData, WeaponActionDataSO EarthFormActionData)
    {
        var formManager = new FormManager(_playerController);
        var fireForm  = new FireForm(FireFormActionData);
        var waterForm = new WaterForm(WaterFormActionData);
        var woodForm  = new WoodForm(WoodFormActionData);
        var ironForm  = new IronForm(IronFormActionData);
        var earthForm = new EarthForm(EarthFormActionData);

        formManager.CanTransition = () =>
            _playerController.StateMachine.CurrentState is PlayerIdleState ||
            _playerController.StateMachine.CurrentState is PlayerMoveState;

        formManager.AddTransition(fireForm,  () => _playerController.Input.IsForm1Pressed);
        formManager.AddTransition(waterForm, () => _playerController.Input.IsForm2Pressed);
        formManager.AddTransition(woodForm,  () => _playerController.Input.IsForm3Pressed);
        formManager.AddTransition(ironForm,  () => _playerController.Input.IsForm4Pressed);
        formManager.AddTransition(earthForm, () => _playerController.Input.IsForm5Pressed);

        formManager.ChangeForm(fireForm);
        return formManager;
    }
}