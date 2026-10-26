using System;
using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Consumes queued level choices without ever releasing the tactical pause between them.</summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeDraftController : MonoBehaviour
    {
        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerRuntimeStats stats;
        [SerializeField] private PlayerHealth health;
        [SerializeField] private CombatProgression progression;
        private readonly List<UpgradeOffer> offers = new List<UpgradeOffer>(3);
        private readonly List<UpgradeCardDefinition> candidates = new List<UpgradeCardDefinition>(35);
        private readonly int[] rarityCounts = new int[4];
        private CombatSession subscribedSession;
        private CombatProgression subscribedProgression;
        private IReadOnlyList<UpgradeOffer> readOnlyOffers;
        private bool resolving;
        public bool IsOpen { get; private set; }
        public bool HasAvailableCards => offers.Count > 0;
        public IReadOnlyList<UpgradeOffer> Offers => readOnlyOffers ??= offers.AsReadOnly();
        private int rerollsUsed;
        public int RerollsRemaining => stats != null ? Mathf.Max(0, stats.GetUpgradeTier(UpgradeId.GREED_04)-rerollsUsed) : 0;
        public bool TryReroll()
        {
            if (!IsOpen || resolving || offers.Count == 0 || RerollsRemaining == 0 || session.Clock.HasPauseReason(PauseReason.FocusLost)) return false;
            rerollsUsed++; GenerateOffers(); OnOffersChanged?.Invoke(); return true;
        }
        private float Weight(UpgradeRarity rarity)
        {
            int luck=stats.GetUpgradeTier(UpgradeId.GREED_04);
            return UpgradeCatalog.RarityWeight(rarity)+(rarity==UpgradeRarity.N ? -luck*(stats.Upgrades.rareWeightPerTier+stats.Upgrades.epicWeightPerTier) : rarity==UpgradeRarity.R ? luck*stats.Upgrades.rareWeightPerTier : rarity==UpgradeRarity.SR ? luck*stats.Upgrades.epicWeightPerTier : 0);
        }
        public bool IsEligible(UpgradeCardDefinition card)
        {
            if(stats.GetUpgradeTier(card.Id)>=card.MaxTier) return false;
            if((card.Id=="DEMO_01"||card.Id=="DEMO_04")&&!stats.HasExplosionSource) return false;
            if(card.Id=="CRYO_02"&&stats.GetUpgradeTier(UpgradeId.CRYO_01)==0) return false;
            if(card.Id=="CRYO_04"&&stats.GetUpgradeTier(UpgradeId.CRYO_02)==0&&stats.GetUpgradeTier(UpgradeId.CRYO_03)==0) return false;
            if(card.Id=="CRIT_04"&&stats.CritChance<=0) return false;
            return true;
        }
        public int PendingChoices => progression != null ? progression.PendingChoices : 0;
        public event Action OnOffersChanged;
        public event Action<UpgradeCardDefinition> OnCardChosen;

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            RefreshPendingChoices();
        }
        private void OnDisable()
        {
            Unsubscribe();
            CloseDraft();
        }
        public void BindReferences(CombatSession combatSession, PlayerRuntimeStats playerStats,
            PlayerHealth playerHealth, CombatProgression combatProgression)
        {
            if (session == combatSession && stats == playerStats && health == playerHealth && progression == combatProgression)
            {
                Subscribe();
                RefreshPendingChoices();
                return;
            }
            CloseDraft();
            Unsubscribe();
            session = combatSession; stats = playerStats; health = playerHealth; progression = combatProgression;
            offers.Clear(); IsOpen = false;
            Subscribe();
            RefreshPendingChoices();
            OnOffersChanged?.Invoke();
        }
        private void ResolveReferences()
        {
            if (session == null) session = FindAnyObjectByType<CombatSession>();
            if (health == null) health = session != null ? session.PlayerHealth : FindAnyObjectByType<PlayerHealth>();
            if (stats == null && health != null) stats = health.Stats;
            if (progression == null) progression = FindAnyObjectByType<CombatProgression>();
        }
        private void Subscribe()
        {
            if (!isActiveAndEnabled) return;
            if (subscribedSession != session)
            {
                if (subscribedSession != null)
                {
                    subscribedSession.SessionReset -= ResetForSession;
                    subscribedSession.Events.OnSessionStateChanged -= HandleSessionState;
                }
                subscribedSession = session;
                if (subscribedSession != null)
                {
                    subscribedSession.SessionReset += ResetForSession;
                    subscribedSession.Events.OnSessionStateChanged += HandleSessionState;
                }
            }
            if (subscribedProgression != progression)
            {
                if (subscribedProgression != null) subscribedProgression.OnPendingChoicesChanged -= HandlePendingChoices;
                subscribedProgression = progression;
                if (subscribedProgression != null) subscribedProgression.OnPendingChoicesChanged += HandlePendingChoices;
            }
        }
        private void Unsubscribe()
        {
            if (subscribedSession != null)
            {
                subscribedSession.SessionReset -= ResetForSession;
                subscribedSession.Events.OnSessionStateChanged -= HandleSessionState;
            }
            if (subscribedProgression != null) subscribedProgression.OnPendingChoicesChanged -= HandlePendingChoices;
            subscribedSession = null; subscribedProgression = null;
        }
        private void HandlePendingChoices(int _) { if (!resolving) RefreshPendingChoices(); }
        private void HandleSessionState(SessionStateEvent state)
        {
            if (state.State == SessionState.Victory || state.State == SessionState.Defeat) CloseDraft();
        }
        public void RefreshPendingChoices()
        {
            if (!isActiveAndEnabled || resolving) return;
            if (session == null || stats == null || health == null || !health.IsAlive || progression == null ||
                progression.PendingChoices == 0 || session.State == SessionState.Victory || session.State == SessionState.Defeat)
            { CloseDraft(); return; }
            if (IsOpen) return; // Input, paints and duplicate progression events cannot reroll.
            IsOpen = true;
            session.SetTacticalDraftPause(true);
            GenerateOffers();
            OnOffersChanged?.Invoke();
        }
        private void GenerateOffers()
        {
            offers.Clear(); candidates.Clear();
            foreach (var card in UpgradeCatalog.Cards)
                if (IsEligible(card)) candidates.Add(card);
            for (int pick = 0; pick < 3 && candidates.Count > 0; pick++)
            {
                Array.Clear(rarityCounts, 0, rarityCounts.Length);
                foreach (var card in candidates) rarityCounts[(int)card.Rarity]++;
                float totalWeight = 0f;
                for (int rarity = 0; rarity < 4; rarity++)
                    if (rarityCounts[rarity] > 0) totalWeight += Weight((UpgradeRarity)rarity);
                float draw = session.Rng.Combat.NextFloat() * totalWeight;
                int selectedRarity = 0;
                for (int rarity = 0; rarity < 4; rarity++)
                {
                    if (rarityCounts[rarity] == 0) continue;
                    selectedRarity = rarity;
                    draw -= Weight((UpgradeRarity)rarity);
                    if (draw < 0f) break;
                }
                int selection = session.Rng.Combat.Range(0, rarityCounts[selectedRarity]);
                for (int index = 0; index < candidates.Count; index++)
                {
                    if ((int)candidates[index].Rarity != selectedRarity) continue;
                    if (selection-- != 0) continue;
                    offers.Add(new UpgradeOffer(candidates[index], stats));
                    candidates.RemoveAt(index);
                    break;
                }
            }
        }
        public bool TryChoose(int index)
        {
            if (!IsOpen || resolving || session == null || stats == null || health == null || progression == null || progression.PendingChoices <= 0 ||
                index < 0 || index >= offers.Count || !health.IsAlive ||
                session.State == SessionState.Victory || session.State == SessionState.Defeat) return false;
            UpgradeOffer offer = offers[index];
            if (stats.GetUpgradeTier(offer.Card.Id) != offer.CurrentTier || offer.NextTier > offer.Card.MaxTier) return false;
            resolving = true;
            try
            {
                SuppressConfirmationDash();
                stats.SetUpgradeTier(offer.Card.Id, offer.NextTier);
                if (offer.Card.Id == "BIO_01") health.Heal(15f);
                progression.ConsumePendingChoice();
                session.Upgrades?.UpgradeAcquired(offer.Card.Id);
                OnCardChosen?.Invoke(offer.Card);
                if (progression.PendingChoices > 0)
                {
                    GenerateOffers();
                    OnOffersChanged?.Invoke();
                }
                else CloseDraft();
            }
            finally { resolving = false; }
            return true;
        }
        public bool ContinueWithoutUpgrade()
        {
            if (!IsOpen || resolving || offers.Count != 0 || progression == null || session == null ||
                health == null || !health.IsAlive || session.State == SessionState.Victory || session.State == SessionState.Defeat) return false;
            resolving = true;
            try
            {
                SuppressConfirmationDash();
                while (progression.PendingChoices > 0) progression.ConsumePendingChoice();
                CloseDraft();
            }
            finally { resolving = false; }
            return true;
        }
        private void SuppressConfirmationDash()
        {
            if (health != null && health.TryGetComponent<PlayerInputReader>(out var input)) input.SuppressDashFromUiConfirmation();
        }
        private void CloseDraft()
        {
            bool changed = IsOpen || offers.Count > 0;
            IsOpen = false; offers.Clear();
            if (session != null && session.Clock.HasPauseReason(PauseReason.TacticalDraft))
                session.ConfirmDraftSelectionAndResume();
            if (changed) OnOffersChanged?.Invoke();
        }
        private void ResetForSession()
        {
            IsOpen = false; offers.Clear(); candidates.Clear(); resolving = false; rerollsUsed = 0;
            OnOffersChanged?.Invoke();
        }
    }
}
