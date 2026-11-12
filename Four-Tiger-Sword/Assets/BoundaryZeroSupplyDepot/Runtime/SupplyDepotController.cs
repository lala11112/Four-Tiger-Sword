using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoundaryZero.SupplyDepot
{
    [DisallowMultipleComponent]
    public sealed class SupplyDepotController : MonoBehaviour
    {
        const float InitialReelIndex = 3f;
        const float FinalReelIndex = 27f;

        public SupplyDepotCatalog catalog;
        public SupplyDepotView view;
        public MonoBehaviour accountProvider;
        public bool openOnStart;
        public bool automaticKeyboardInput = true;
        public bool manageCursor = true;

        public bool IsOpen { get; private set; }
        public bool IsDrawing => pendingReceipt != null;

        public event Action<bool> OpenChanged;
        public event Action<SupplyDepotReceipt> RewardGranted;

        readonly System.Random drawRandom = new System.Random();
        ISupplyDepotAccount account;
        SupplyDepotView configuredView;
        SupplyDepotCatalog configuredCatalog;
        bool accountAssigned;
        bool busy;
        bool observingAccount;
        bool cursorCaptured;
        bool reelInitialized;
        CursorLockMode savedCursorLock;
        bool savedCursorVisible;
        string displayedTransaction;
        string displayedItem;
        SupplyDepotReceipt pendingReceipt;
        SupplyDepotItem pendingItem;
        double drawStartedAt;
        float drawSeconds;
        int openedFrame;

        void OnEnable()
        {
            if (!accountAssigned) account = accountProvider as ISupplyDepotAccount;
            SubscribeAccount();
            if (EnsureView()) Refresh();
        }

        void Start()
        {
            if (openOnStart) Open();
        }

        void OnDisable()
        {
            UnsubscribeAccount();
            DetachView();
            RestoreCursor();

            bool wasOpen = IsOpen;
            IsOpen = false;
            if (view != null) view.SetOpen(false);
            bool wasBusy = busy;
            busy = true;
            try
            {
                if (IsDrawing) CompleteDraw();
                if (wasOpen) OpenChanged?.Invoke(false);
            }
            finally { busy = wasBusy; }
        }

        void Update()
        {
            if (IsDrawing)
            {
                var elapsed = Time.realtimeSinceStartupAsDouble - drawStartedAt;
                float progress = Mathf.Clamp01((float)(elapsed / drawSeconds));
                if (progress >= 1f)
                {
                    SkipAnimation();
                }
                else if (view != null)
                {
                    float remaining = 1f - progress;
                    view.SetReelPosition(Mathf.Lerp(InitialReelIndex, FinalReelIndex, 1f - remaining * remaining * remaining));
                }
                return;
            }

            if (!IsOpen || !automaticKeyboardInput || Time.frameCount == openedFrame) return;
            ReadKeyboard(out bool draw, out bool close);
            if (close) Close();
            else if (draw) TryDraw();
        }

        public void BindAccount(ISupplyDepotAccount nextAccount)
        {
            if (busy || IsDrawing) return;
            busy = true;
            try
            {
                UnsubscribeAccount();
                account = nextAccount;
                accountAssigned = true;
                SubscribeAccount();
                reelInitialized = false;
                if (isActiveAndEnabled && EnsureView()) Refresh();
            }
            finally { busy = false; }
        }

        public bool Open()
        {
            if (busy || IsDrawing || IsOpen || !isActiveAndEnabled || !EnsureView()) return false;
            busy = true;
            try
            {
                IsOpen = true;
                // Do not spend from the same F press that opened a world interaction.
                openedFrame = Time.frameCount;
                CaptureCursor();
                Refresh();
                view.SetOpen(true);
                OpenChanged?.Invoke(true);
                return true;
            }
            finally { busy = false; }
        }

        public bool Close()
        {
            if (busy || IsDrawing || !IsOpen) return false;
            busy = true;
            try
            {
                IsOpen = false;
                RestoreCursor();
                if (view != null) view.SetOpen(false);
                OpenChanged?.Invoke(false);
                return true;
            }
            finally { busy = false; }
        }

        public bool TryDraw()
        {
            if (busy || IsDrawing || !IsOpen || !isActiveAndEnabled || !EnsureView() || !CanDraw(account?.CurrentReceipt))
                return false;

            busy = true;
            try
            {
                var chosen = catalog.items[drawRandom.Next(catalog.items.Count)];
                if (!account.TryPurchase(chosen.id, catalog.price, out var receipt))
                {
                    Refresh();
                    return false;
                }

                // The receipt is authoritative. Neither the reel nor skipping can choose another item.
                receipt = receipt ?? account.CurrentReceipt;
                var committedItem = FindItem(receipt?.itemId);
                if (receipt == null || committedItem == null)
                {
                    Debug.LogError("Supply Depot account returned an invalid receipt for a committed purchase.", this);
                    Refresh();
                    return false;
                }

                pendingReceipt = new SupplyDepotReceipt(receipt.transactionId, receipt.itemId);
                pendingItem = committedItem;
                drawStartedAt = Time.realtimeSinceStartupAsDouble;
                drawSeconds = catalog.drawDuration > 0f && !float.IsInfinity(catalog.drawDuration)
                    ? catalog.drawDuration : 4.2f;

                if (!isActiveAndEnabled || !IsOpen)
                {
                    CompleteDraw();
                }
                else
                {
                    view.PrepareReel(pendingItem);
                    view.SetReelPosition(InitialReelIndex);
                    view.Render(account.PermanentCurrency, false, true, null);
                }
                return true;
            }
            finally { busy = false; }
        }

        public void SkipAnimation()
        {
            if (busy || !IsDrawing) return;
            busy = true;
            try { CompleteDraw(); }
            finally { busy = false; }
        }

        public void NotifyRunEnded()
        {
            if (busy || IsDrawing || account == null) return;
            busy = true;
            try
            {
                account.ResetForNextRun();
                if (isActiveAndEnabled && EnsureView()) Refresh();
            }
            finally { busy = false; }
        }

        void CompleteDraw()
        {
            var receipt = pendingReceipt;
            var winner = pendingItem;
            pendingReceipt = null;
            pendingItem = null;
            displayedTransaction = receipt.transactionId;
            displayedItem = receipt.itemId;
            reelInitialized = true;

            if (view != null && isActiveAndEnabled)
            {
                view.SetReelPosition(FinalReelIndex);
                view.Render(account?.PermanentCurrency ?? 0, false, false, winner);
            }
            RewardGranted?.Invoke(receipt);
        }

        void Refresh()
        {
            var receipt = account?.CurrentReceipt;
            var result = FindItem(receipt?.itemId);
            if (!reelInitialized || displayedTransaction != receipt?.transactionId || displayedItem != receipt?.itemId)
            {
                reelInitialized = true;
                displayedTransaction = receipt?.transactionId;
                displayedItem = receipt?.itemId;
                if (result == null)
                {
                    view.PrepareReel(null);
                    view.SetReelPosition(InitialReelIndex);
                }
                else
                {
                    view.ShowRestoredResult(result);
                }
            }
            view.Render(account?.PermanentCurrency ?? 0, CanDraw(receipt), false, result);
        }

        bool CanDraw(SupplyDepotReceipt receipt) =>
            account != null && receipt == null && catalog != null && catalog.price >= 0 &&
            account.PermanentCurrency >= catalog.price && HasValidItems();

        bool HasValidItems()
        {
            if (catalog.items == null || catalog.items.Count == 0) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in catalog.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id)) return false;
            }
            return true;
        }

        SupplyDepotItem FindItem(string id)
        {
            if (string.IsNullOrEmpty(id) || catalog == null || catalog.items == null) return null;
            foreach (var item in catalog.items)
            {
                if (item != null && item.id == id) return item;
            }
            return null;
        }

        bool EnsureView()
        {
            if (view == null || catalog == null) return false;
            if (configuredView == view && configuredCatalog == catalog) return true;
            DetachView();
            view.Configure(catalog);
            view.DrawRequested += OnDrawRequested;
            view.SkipRequested += SkipAnimation;
            view.CloseRequested += OnCloseRequested;
            view.ReopenRequested += OnOpenRequested;
            configuredView = view;
            configuredCatalog = catalog;
            reelInitialized = false;
            view.SetOpen(IsOpen);
            return true;
        }

        void DetachView()
        {
            if (configuredView != null)
            {
                configuredView.DrawRequested -= OnDrawRequested;
                configuredView.SkipRequested -= SkipAnimation;
                configuredView.CloseRequested -= OnCloseRequested;
                configuredView.ReopenRequested -= OnOpenRequested;
            }
            configuredView = null;
            configuredCatalog = null;
        }

        void OnDrawRequested() => TryDraw();
        void OnCloseRequested() => Close();
        void OnOpenRequested() => Open();

        void SubscribeAccount()
        {
            if (observingAccount || !isActiveAndEnabled || account == null) return;
            account.Changed += OnAccountChanged;
            observingAccount = true;
        }

        void UnsubscribeAccount()
        {
            if (!observingAccount) return;
            account.Changed -= OnAccountChanged;
            observingAccount = false;
        }

        void OnAccountChanged()
        {
            if (busy || !isActiveAndEnabled || view == null || configuredView != view) return;
            busy = true;
            try
            {
                if (IsDrawing) view.Render(account.PermanentCurrency, false, true, null);
                else Refresh();
            }
            finally { busy = false; }
        }

        void CaptureCursor()
        {
            if (!manageCursor || cursorCaptured) return;
            savedCursorLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void RestoreCursor()
        {
            if (!cursorCaptured) return;
            cursorCaptured = false;
            Cursor.lockState = savedCursorLock;
            Cursor.visible = savedCursorVisible;
        }

        static void ReadKeyboard(out bool draw, out bool close)
        {
            draw = false;
            close = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            draw |= Input.GetKeyDown(KeyCode.F);
            close |= Input.GetKeyDown(KeyCode.Escape);
#endif
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                draw |= keyboard.fKey.wasPressedThisFrame;
                close |= keyboard.escapeKey.wasPressedThisFrame;
            }
#endif
        }
    }
}
