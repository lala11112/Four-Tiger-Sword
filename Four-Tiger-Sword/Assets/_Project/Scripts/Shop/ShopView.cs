using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Temporary uGUI adapter; replace this prefab or implement IShopView for the final UI.</summary>
public sealed class ShopView : MonoBehaviour, IShopView
{
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject interactionPrompt;
    [SerializeField] private TMP_Text balance;
    [SerializeField] private TMP_Text health;
    [SerializeField] private TMP_Text message;
    [SerializeField] private Transform offersRoot;
    [SerializeField] private ShopOfferView offerPrefab;
    [SerializeField] private UnityEngine.UI.Button closeButton;
    private readonly List<ShopOfferView> _rows = new();
    public event Action<string> PurchaseRequested;
    public event Action CloseRequested;

    public void Configure(GameObject panelRoot, GameObject prompt, TMP_Text gold, TMP_Text hp,
        TMP_Text feedback, Transform content, ShopOfferView row, UnityEngine.UI.Button close)
    { panel = panelRoot; interactionPrompt = prompt; balance = gold; health = hp; message = feedback; offersRoot = content; offerPrefab = row; closeButton = close; }

    private void Awake()
    {
        closeButton.onClick.AddListener(Close);
        panel.SetActive(false);
        interactionPrompt.SetActive(false);
    }
    private void OnDestroy() { if (closeButton != null) closeButton.onClick.RemoveListener(Close); }
    private void Close() => CloseRequested?.Invoke();
    private void Buy(string id) => PurchaseRequested?.Invoke(id);

    public void Render(ShopViewState state)
    {
        bool opening = state.Visible && !panel.activeSelf;
        if (!state.Visible && panel.activeSelf && EventSystem.current != null)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(panel.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        panel.SetActive(state.Visible);
        interactionPrompt.SetActive(state.ShowInteractionPrompt && !state.Visible);
        if (!state.Visible) return;
        balance.text = "보유 골드  " + state.Gold + " G";
        health.text = $"체력  {Mathf.CeilToInt(state.Hp):N0} / {Mathf.CeilToInt(state.MaxHp):N0}";
        message.text = state.Message ?? string.Empty;
        for (int i = 0; i < state.Offers.Length; i++)
        {
            if (i >= _rows.Count) _rows.Add(Instantiate(offerPrefab, offersRoot));
            _rows[i].gameObject.SetActive(true);
            _rows[i].Render(state.Offers[i], Buy);
        }
        for (int i = state.Offers.Length; i < _rows.Count; i++) _rows[i].gameObject.SetActive(false);
        if (opening && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
    }
}
