using BoundaryZero.SupplyDepot;
using UnityEngine;

/// <summary>Guides the existing workshop and supply depot after the tutorial map.</summary>
[DisallowMultipleComponent]
public sealed class TutorialHubDirector : MonoBehaviour
{
    private PlayerUpgradeService _service;
    private HubWorldController _world;
    private HubWorkshopInteraction _workshop;
    private HubSupplyDepotInteraction _depot;
    private SupplyDepotController _depotController;
    private TutorialView _view;
    private Transform _stationTarget;
    private bool _overview;
    private bool _overviewFinished;
    private int _overviewStep;
    private bool _showCompletion;
    private float _completionUntil;
    private string _error;
    private bool _retrySkip;
    private (int Step, bool Overview, bool Open, bool Ready, bool Confirm, string Error)? _lastPrompt;

    internal bool CanStartExpedition => _overviewFinished || !_overview && _service?.TutorialStatus != TutorialProgressStatus.HubLessons;

    internal static TutorialHubDirector Ensure(HubWorldController world, PlayerUpgradeService service)
    {
        if (!TutorialSession.PendingHubOverview && service.TutorialStatus != TutorialProgressStatus.HubLessons) return null;
        var director = world.GetComponent<TutorialHubDirector>() ?? world.gameObject.AddComponent<TutorialHubDirector>();
        director._world = world;
        director._service = service;
        director._overview = TutorialSession.PendingHubOverview;
        director._workshop = world.GetComponent<HubWorkshopInteraction>();
        director._depot = world.GetComponent<HubSupplyDepotInteraction>();
        director._depotController = director._depot != null ? director._depot.Controller : null;
        TutorialSession.PendingHubOverview = false;
        TutorialSession.IsReplay = false;
        return director;
    }

    private void Start()
    {
        if (_service == null) { enabled = false; return; }
        _view = TutorialView.Ensure(this);
        _view.SetSkipAction(SkipLesson);
        _stationTarget = new GameObject("Tutorial Station Target").transform;
        _stationTarget.SetParent(transform, false);
    }

    private void Update()
    {
        if (_view == null || HubReturnTransition.IsActive) return;
        bool actualLessons = !_overviewFinished && _service.TutorialStatus == TutorialProgressStatus.HubLessons;
        if (!_overview && !actualLessons)
        {
            ShowCompletion();
            return;
        }

        _showCompletion = true;
        int step = _overview ? _overviewStep : _service.TutorialHubStep;
        bool depotStep = step == PlayerUpgradeService.TutorialDepotStep;
        var workshop = _workshop != null ? _workshop.View : null;
        var expectedPage = step == PlayerUpgradeService.TutorialEngravingStep
            ? HubWorkshopView.WorkshopPage.Engravings : HubWorkshopView.WorkshopPage.Basic;
        bool open = depotStep ? _depot != null && _depot.IsOpen : workshop != null && workshop.IsOpen;
        bool ready = depotStep ? open : open && workshop.IsInteractionReady && workshop.SelectedPage == expectedPage;
        bool acknowledgement = _service.CanAcknowledgeTutorialHubStep(DepotPrice);
        bool confirm = _retrySkip || ready && (_overview || acknowledgement);
        workshop?.SetTutorialOverview(_overview);
        _depotController?.SetDrawPermitted(!_overview && depotStep && !acknowledgement);

        var prompt = (step, _overview, open, ready, confirm, _error);
        if (_lastPrompt != prompt)
        {
            _lastPrompt = prompt;
            ShowLesson(step, open, ready, confirm);
        }

        if (!open)
        {
            Vector3? position = depotStep ? _depot?.InteractionPosition : _workshop?.InteractionPosition;
            if (position.HasValue) _stationTarget.position = position.Value + Vector3.up;
            _view.SetWorldTarget(position.HasValue ? _stationTarget : null);
        }
        else if (depotStep) _view.SetUITarget(_depotController?.view?.DrawTarget);
        else _view.SetUITarget(GetWorkshopTarget(workshop, step, expectedPage, confirm));
    }

    private int DepotPrice => _depotController?.catalog?.price ?? 100;

    private RectTransform GetWorkshopTarget(HubWorkshopView workshop, int step,
        HubWorkshopView.WorkshopPage expectedPage, bool confirm)
    {
        if (workshop.SelectedPage != expectedPage) return workshop.TutorialTabTarget(expectedPage);
        if (!workshop.IsInteractionReady || confirm) return null;
        if (step == PlayerUpgradeService.TutorialUpgradeStep) return workshop.UI?.purchaseButton?.transform as RectTransform;
        var tree = workshop.EngravingView;
        if (tree == null) return null;
        if (_service.GetNodePurchaseState(tree.SelectedId).CanPurchase) return tree.PurchaseButton?.transform as RectTransform;
        foreach (var node in _service.EngravingSettings.nodes)
            if (node != null && _service.GetNodePurchaseState(node.id).CanPurchase)
                return tree.GetNodeButton(node.id)?.transform as RectTransform;
        return null;
    }

    private void ShowLesson(int step, bool open, bool ready, bool confirm)
    {
        if (_retrySkip)
        {
            _view.SetGoal("거점 안내", _error ?? "진행 기록을 저장하지 못했습니다.", string.Empty);
            _view.ShowContinue("다시 시도", SkipLesson);
            return;
        }
        string title;
        string body;
        switch (step)
        {
            case PlayerUpgradeService.TutorialUpgradeStep:
                title = "능력치 강화";
                body = !open ? "작업대에서 강화석으로 기본 능력치를 높일 수 있습니다. 표시된 작업대로 이동하세요."
                    : !ready ? "능력치 강화 탭을 열어 강화 비용과 다음 단계 효과를 확인하세요."
                    : confirm ? "강화석으로 최대 체력, 공격력, 방어력을 높일 수 있습니다. 강화 비용과 효과를 확인하세요."
                    : "최대 체력, 공격력, 방어력 중 하나를 선택해 한 번 강화하세요.";
                break;
            case PlayerUpgradeService.TutorialEngravingStep:
                title = "각인 트리";
                body = !open ? "작업대에서 각인 트리를 열면 강화석으로 새 효과를 해금할 수 있습니다."
                    : !ready ? "작업대의 각인 탭을 여세요."
                    : confirm ? "시작 각인에서 이어지는 능력을 살펴보세요. 각인은 연결된 선행 각인부터 해금합니다."
                    : "첫 각인과 연결된 효과를 선택하고 강화석으로 해금하세요.";
                break;
            default:
                title = "보급품 뽑기";
                body = !open ? "작업대에서 나와 표시된 보급고로 이동하세요."
                    : confirm ? $"보급품 뽑기에는 강화석 {DepotPrice:N0}개가 필요합니다. 보급품은 다음 탐사에 지급됩니다."
                    : $"강화석 {DepotPrice:N0}개로 무작위 보급품을 뽑으세요. 획득한 보급품은 다음 탐사 시작 때 지급됩니다.";
                break;
        }

        if (!string.IsNullOrEmpty(_error)) body += "\n" + _error;
        _view.SetGoal(title, body, open ? string.Empty : "F");
        if (confirm)
        {
            bool overview = _overview;
            _view.ShowContinue("확인", () => ConfirmLesson(step, overview));
        }
    }

    private void ConfirmLesson(int step, bool overview)
    {
        if (overview != _overview || step != (_overview ? _overviewStep : _service.TutorialHubStep)) return;
        var workshop = _workshop?.View;
        bool correctWindow = step == PlayerUpgradeService.TutorialDepotStep ? _depot?.IsOpen == true
            : workshop?.IsInteractionReady == true && workshop.SelectedPage == (step == PlayerUpgradeService.TutorialEngravingStep
                ? HubWorkshopView.WorkshopPage.Engravings : HubWorkshopView.WorkshopPage.Basic);
        if (!correctWindow) return;
        _lastPrompt = null;
        _error = null;
        if (!_overview)
        {
            if (!_service.TryAcknowledgeTutorialHubStep(DepotPrice, out _error)) return;
        }
        else if (++_overviewStep > PlayerUpgradeService.TutorialDepotStep)
        {
            _overview = false;
            _overviewFinished = true;
            ClosePanels();
        }
    }

    private void SkipLesson()
    {
        _lastPrompt = null;
        if (!_overview && _service.TutorialStatus == TutorialProgressStatus.HubLessons
            && !_service.TrySkipTutorial(out _error))
        {
            _retrySkip = true;
            return;
        }

        if (_overview)
        {
            _overview = false;
            _overviewFinished = true;
        }
        _retrySkip = false;
        _showCompletion = false;
        ClosePanels();
        _view.SetSkipAction(null);
        _view.Hide();
    }

    private void ClosePanels()
    {
        if (_workshop?.IsOpen == true) _workshop.Close();
        if (_depot?.IsOpen == true) _depot.Close();
        _world?.ClosePauseAfterTutorial();
    }

    private void ShowCompletion()
    {
        if (_depotController != null && _depotController.IsDrawing)
        {
            _view.SetWorldTarget(null);
            _view.SetUITarget(null);
            return;
        }
        _workshop?.View?.SetTutorialOverview(false);
        _depotController?.SetDrawPermitted(true);
        if (!_showCompletion) return;
        if (_completionUntil == 0f)
        {
            _completionUntil = Time.unscaledTime + 6f;
            _view.SetSkipAction(null);
            _view.SetGoal("튜토리얼 완료", "거점 안내를 마쳤습니다. 탐사를 시작하려면 게이트로 이동하세요.", string.Empty);
            _view.SetWorldTarget(null);
            _view.SetUITarget(null);
        }
        else if (Time.unscaledTime >= _completionUntil)
        {
            _view.Hide();
            _showCompletion = false;
        }
    }

    private void OnDisable()
    {
        _lastPrompt = null;
        _view?.Hide();
        _workshop?.View?.SetTutorialOverview(false);
        _depotController?.SetDrawPermitted(true);
    }
}
