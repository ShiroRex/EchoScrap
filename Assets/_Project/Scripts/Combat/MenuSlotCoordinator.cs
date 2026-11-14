using System;

namespace EchoScrap.Combat
{
    internal enum PendingSlotConfirmation
    {
        None = 0,
        AbandonExpedition = 1,
        OverwriteSlotWarning = 2,
        NewGameName = 3,
        RecoverCorruptedExpedition = 4
    }

    /// <summary>
    /// Shared 3-slot selector and confirmation workflow used by both MainMenuController and GameFlowController.
    /// Ensures previous save files remain untouched until validation and atomic replacement succeed.
    /// </summary>
    internal sealed class MenuSlotCoordinator
    {
        private readonly Func<GameMenuView> getView;
        private readonly Action refresh;
        private readonly Func<bool> canLaunchArena;
        private readonly Action<SceneTransitionState.Entry, int> launchSlot;
        private readonly Action onAbandonAccepted;

        public SlotSelectorMode Mode { get; private set; }
        public PendingSlotConfirmation ConfirmationKind { get; private set; }
        public int SelectedSlotIndex { get; private set; } = -1;
        public bool IsConfirming => ConfirmationKind != PendingSlotConfirmation.None;
        public bool IsSlotSelectorOpen => Mode != SlotSelectorMode.None;

        public MenuSlotCoordinator(
            Func<GameMenuView> getView,
            Action refresh,
            Func<bool> canLaunchArena,
            Action<SceneTransitionState.Entry, int> launchSlot,
            Action onAbandonAccepted = null)
        {
            this.getView = getView;
            this.refresh = refresh;
            this.canLaunchArena = canLaunchArena;
            this.launchSlot = launchSlot;
            this.onAbandonAccepted = onAbandonAccepted;
        }

        public void OpenContinue()
        {
            if (!SaveSlotService.HasAnyContinuableOrRecoverableSlot()) return;
            ConfirmationKind = PendingSlotConfirmation.None;
            Mode = SlotSelectorMode.Continue;
            SelectedSlotIndex = SaveSlotService.GetDefaultSlotFocus(Mode);
            refresh();
        }

        public void OpenNewGame()
        {
            ConfirmationKind = PendingSlotConfirmation.None;
            Mode = SlotSelectorMode.NewGame;
            SelectedSlotIndex = SaveSlotService.GetDefaultSlotFocus(Mode);
            refresh();
        }

        public void CloseSelector()
        {
            if (IsConfirming) return;
            Mode = SlotSelectorMode.None;
            SelectedSlotIndex = -1;
            refresh();
        }

        public void Reset()
        {
            Mode = SlotSelectorMode.None;
            ConfirmationKind = PendingSlotConfirmation.None;
            SelectedSlotIndex = -1;
        }

        public void OpenAbandonConfirmation(string title, string message)
        {
            Mode = SlotSelectorMode.None;
            SelectedSlotIndex = -1;
            ConfirmationKind = PendingSlotConfirmation.AbandonExpedition;
            refresh();
            getView()?.ShowConfirmation(title, message, askPlayerName: false, acceptLabel: "CONFIRMAR");
        }

        public void SelectSlot(int slotIndex)
        {
            if (Mode == SlotSelectorMode.None || IsConfirming) return;
            var summaries = SaveSlotService.GetSlotSummaries();
            if (slotIndex < 0 || slotIndex >= summaries.Length) return;
            SlotSummary summary = summaries[slotIndex];
            SelectedSlotIndex = slotIndex;

            if (Mode == SlotSelectorMode.Continue)
            {
                if (summary.CanRecoverVault)
                {
                    ShowRecoveryPrompt(slotIndex, summary);
                    return;
                }
                if (!summary.CanContinue)
                {
                    getView()?.SetStatus(summary.Health == SlotHealth.Corrupted
                        ? $"El Archivo {slotIndex + 1} está dañado y no puede cargarse."
                        : $"El Archivo {slotIndex + 1} está vacío.");
                    refresh();
                    return;
                }
                if (!(canLaunchArena?.Invoke() ?? true))
                {
                    getView()?.SetStatus("No se pudo cargar el Vertedero.");
                    return;
                }
                if (!SaveSlotService.SelectSlotForContinue(slotIndex, out SlotSummary refreshed, out bool needsRecovery))
                {
                    if (needsRecovery)
                    {
                        ShowRecoveryPrompt(slotIndex, refreshed);
                        return;
                    }
                    getView()?.SetStatus($"No se pudo validar el Archivo {slotIndex + 1}.");
                    refresh();
                    return;
                }

                Mode = SlotSelectorMode.None;
                ConfirmationKind = PendingSlotConfirmation.None;
                launchSlot(SceneTransitionState.Entry.Continue, slotIndex);
                return;
            }

            if (summary.IsOccupied)
            {
                ConfirmationKind = PendingSlotConfirmation.OverwriteSlotWarning;
                string title = $"¿REEMPLAZAR ARCHIVO {slotIndex + 1}?";
                string msg = summary.Health == SlotHealth.Corrupted
                    ? $"El Archivo {slotIndex + 1} ({summary.DisplayTitle}) está dañado.\nSi confirmas, se reemplazará tras validar la nueva partida."
                    : $"Archivo {slotIndex + 1}: \"{summary.DisplayTitle}\" ({summary.CollectiblesLabel} · {summary.FormattedPlayTime}).\nSe conservará intacto hasta validar la nueva partida.";
                refresh();
                getView()?.ShowConfirmation(title, msg, askPlayerName: false, acceptLabel: "CONFIRMAR");
                return;
            }

            OpenNewGameNamePrompt(slotIndex);
        }

        public void CancelConfirmation()
        {
            var view = getView();
            if (view != null && view.IsValidatingName) return;
            ConfirmationKind = PendingSlotConfirmation.None;
            refresh();
        }

        public void AcceptConfirmation()
        {
            var view = getView();
            if (!IsConfirming || view == null || view.IsValidatingName) return;

            switch (ConfirmationKind)
            {
                case PendingSlotConfirmation.AbandonExpedition:
                    ConfirmationKind = PendingSlotConfirmation.None;
                    onAbandonAccepted?.Invoke();
                    return;

                case PendingSlotConfirmation.OverwriteSlotWarning:
                    OpenNewGameNamePrompt(SelectedSlotIndex);
                    return;

                case PendingSlotConfirmation.RecoverCorruptedExpedition:
                    if (!(canLaunchArena?.Invoke() ?? true))
                    {
                        view.SetStatus("No se pudo cargar el Vertedero.");
                        return;
                    }
                    if (!SaveSlotService.RecoverVaultInSlot(SelectedSlotIndex, out _))
                    {
                        ConfirmationKind = PendingSlotConfirmation.None;
                        view.SetStatus($"No se pudo recuperar la bóveda del Archivo {SelectedSlotIndex + 1}.");
                        refresh();
                        return;
                    }
                    int recoveredSlot = SelectedSlotIndex;
                    ConfirmationKind = PendingSlotConfirmation.None;
                    Mode = SlotSelectorMode.None;
                    launchSlot(SceneTransitionState.Entry.Continue, recoveredSlot);
                    return;

                case PendingSlotConfirmation.NewGameName:
                    if (!(canLaunchArena?.Invoke() ?? true))
                    {
                        view.SetStatus("No se pudo cargar el Vertedero.");
                        return;
                    }
                    int targetSlot = SelectedSlotIndex;
                    view.ValidateCollectionName(validatedName =>
                    {
                        if (!SaveSlotService.CreateNewGameInSlot(targetSlot, validatedName))
                        {
                            view.SetConfirmationMessage("No se pudo guardar el nuevo archivo. El archivo anterior sigue intacto.");
                            return;
                        }
                        if (!string.IsNullOrEmpty(validatedName))
                        {
                            GamertagRegistry.ForgetReceipt(validatedName);
                            EventLeaderboardSync.SyncSessionNameFromActiveSlot();
                        }
                        else
                        {
                            EventLeaderboardSync.SyncSessionNameFromActiveSlot();
                        }
                        ConfirmationKind = PendingSlotConfirmation.None;
                        Mode = SlotSelectorMode.None;
                        launchSlot(SceneTransitionState.Entry.NewCollection, targetSlot);
                    });
                    return;
            }
        }

        private void ShowRecoveryPrompt(int slotIndex, SlotSummary summary)
        {
            SelectedSlotIndex = slotIndex;
            ConfirmationKind = PendingSlotConfirmation.RecoverCorruptedExpedition;
            refresh();
            string title = $"EXPEDICIÓN DAÑADA · ARCHIVO {slotIndex + 1}";
            string msg = $"El estado de expedición en \"{summary.DisplayTitle}\" está dañado y no puede reanudarse.\nTus {summary.OwnedCount}/18 coleccionables en la bóveda pueden recuperarse.";
            getView()?.ShowConfirmation(title, msg, askPlayerName: false, acceptLabel: "RECUPERAR BÓVEDA");
        }

        private void OpenNewGameNamePrompt(int slotIndex)
        {
            SelectedSlotIndex = slotIndex;
            ConfirmationKind = PendingSlotConfirmation.NewGameName;
            refresh();
            string title = $"NUEVA PARTIDA · ARCHIVO {slotIndex + 1}";
            string msg = "Elige un gamertag para el ranking (todas tus expediciones válidas puntúan).\nDéjalo vacío para jugar en Práctica.";
            getView()?.ShowConfirmation(title, msg, askPlayerName: true, acceptLabel: "CONFIRMAR");
        }
    }
}
