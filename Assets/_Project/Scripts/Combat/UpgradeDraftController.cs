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
        private readonly List<UpgradeCardDefinition> candidates = new List<UpgradeCardDefinition>(UpgradeRules.Count);
        private readonly int[] rarityCounts = new int[5];
        private readonly List<(string id, int tier)> allInOld = new List<(string, int)>(UpgradeRules.Count);
        private readonly List<UpgradeCardDefinition> allInPool = new List<UpgradeCardDefinition>(UpgradeRules.Count);
        private readonly List<UpgradeCardDefinition> allInCandidates = new List<UpgradeCardDefinition>(UpgradeRules.Count);
        private readonly List<(string id, int tier)> allInRolled = new List<(string, int)>(UpgradeRules.Count);
        private readonly HashSet<string> allInOldIds = new HashSet<string>();
        private readonly HashSet<string> allInRolledIds = new HashSet<string>();
        private CombatSession subscribedSession;
        private CombatProgression subscribedProgression;
        private IReadOnlyList<UpgradeOffer> readOnlyOffers;
        private bool resolving;
        public const float SelectionSeconds = 12f;
        public float SelectionTimeRemaining { get; private set; }
        public bool IsOpen { get; private set; }
        public bool HasAvailableCards => offers.Count > 0;
        public IReadOnlyList<UpgradeOffer> Offers => readOnlyOffers ??= offers.AsReadOnly();
        private int rerollsUsed;
        public int RerollsRemaining => stats != null ? Mathf.Max(0, stats.GetUpgradeTier(UpgradeId.GREED_04)-rerollsUsed) : 0;
        public bool TryReroll()
        {
            if (!IsOpen || resolving || offers.Count == 0 || RerollsRemaining == 0 || session.Clock.HasPauseReason(PauseReason.FocusLost)) return false;
            rerollsUsed++; GenerateOffers(); OnOffersChanged?.Invoke();
            ExpeditionSnapshotService.CaptureFromSession(session);
            return true;
        }
        private float Weight(UpgradeRarity rarity)
        {
            int luck=stats.GetUpgradeTier(UpgradeId.GREED_04);
            float baseWeight = UpgradeCatalog.RarityWeight(rarity);
            return rarity switch
            {
                UpgradeRarity.N => Mathf.Max(1f, baseWeight - luck * (stats.Upgrades.rareWeightPerTier + stats.Upgrades.epicWeightPerTier)),
                UpgradeRarity.R => baseWeight + luck * stats.Upgrades.rareWeightPerTier,
                UpgradeRarity.SR => baseWeight + luck * stats.Upgrades.epicWeightPerTier,
                _ => baseWeight
            };
        }
        public bool IsEligible(UpgradeCardDefinition card)
        {
            if(stats.GetUpgradeTier(card.Id)>=card.MaxTier) return false;
            if((card.Id=="DEMO_01"||card.Id=="DEMO_04")&&!stats.HasExplosionSource) return false;
            if(card.Id=="CRYO_02"&&stats.GetUpgradeTier(UpgradeId.CRYO_01)==0) return false;
            if(card.Id=="CRYO_04"&&stats.GetUpgradeTier(UpgradeId.CRYO_02)==0&&stats.GetUpgradeTier(UpgradeId.CRYO_03)==0) return false;
            if(card.Id=="CRIT_04"&&stats.CritChance<=0) return false;
            if(card.Id=="MYTH_01"&&!stats.HasAnyOtherUpgrade("MYTH_01")) return false;
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
            SelectionTimeRemaining = SelectionSeconds;
            GenerateOffers();
            OnOffersChanged?.Invoke();
            ExpeditionSnapshotService.CaptureFromSession(session);
        }
        public void AdvanceSelectionTime(float unscaledDeltaTime)
        {
            if (!IsOpen || resolving || offers.Count == 0 || !(unscaledDeltaTime > 0f) ||
                (session.Clock.ActiveReasons & (PauseReason.Manual | PauseReason.FocusLost)) != PauseReason.None)
                return;

            SelectionTimeRemaining = Mathf.Max(0f, SelectionTimeRemaining - unscaledDeltaTime);
            if (SelectionTimeRemaining == 0f)
                TryChoose(session.Rng.Combat.Range(0, offers.Count));
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
                for (int rarity = 0; rarity < 5; rarity++)
                    if (rarityCounts[rarity] > 0) totalWeight += Weight((UpgradeRarity)rarity);
                float draw = session.Rng.Combat.NextFloat() * totalWeight;
                int selectedRarity = 0;
                for (int rarity = 0; rarity < 5; rarity++)
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
                if (offer.Card.Id == "MYTH_01") ApplyAllIn();
                else if (offer.Card.Id == "BIO_01") health.Heal(15f);
                progression.ConsumePendingChoice();
                session.Upgrades?.UpgradeAcquired(offer.Card.Id);
                OnCardChosen?.Invoke(offer.Card);
                if (progression.PendingChoices > 0)
                {
                    SelectionTimeRemaining = SelectionSeconds;
                    GenerateOffers();
                    OnOffersChanged?.Invoke();
                }
                else CloseDraft();
            }
            finally { resolving = false; }
            ExpeditionSnapshotService.CaptureFromSession(session);
            return true;
        }
        private void ApplyAllIn()
        {
            if (stats == null || session == null) return;
            allInOld.Clear();
            allInOldIds.Clear();
            allInPool.Clear();
            allInRolled.Clear();
            allInRolledIds.Clear();
            foreach (var card in UpgradeCatalog.Cards)
            {
                if (card.Id == "MYTH_01") continue;
                int tier = stats.GetUpgradeTier(card.Id);
                if (tier > 0)
                {
                    allInOld.Add((card.Id, tier));
                    allInOldIds.Add(card.Id);
                }
                allInPool.Add(card);
            }
            foreach (var old in allInOld)
            {
                int desired = old.tier + 1;
                allInCandidates.Clear();
                for (int i = 0; i < allInPool.Count; i++)
                {
                    var candidate = allInPool[i];
                    if (!allInOldIds.Contains(candidate.Id) && candidate.MaxTier >= desired) allInCandidates.Add(candidate);
                }
                if (allInCandidates.Count == 0)
                {
                    for (int i = 0; i < allInPool.Count; i++)
                        if (!allInOldIds.Contains(allInPool[i].Id)) allInCandidates.Add(allInPool[i]);
                }
                if (allInCandidates.Count == 0)
                {
                    for (int i = 0; i < allInPool.Count; i++)
                        if (allInPool[i].Id != old.id) allInCandidates.Add(allInPool[i]);
                }
                if (allInCandidates.Count == 0)
                {
                    for (int i = 0; i < allInPool.Count; i++) allInCandidates.Add(allInPool[i]);
                }
                if (allInCandidates.Count > 0)
                {
                    int pick = session.Rng.Combat.Range(0, allInCandidates.Count);
                    var chosen = allInCandidates[pick];
                    allInPool.Remove(chosen);
                    allInRolled.Add((chosen.Id, Mathf.Clamp(desired, 1, chosen.MaxTier)));
                    allInRolledIds.Add(chosen.Id);
                }
                else if (UpgradeCatalog.TryGet(old.id, out var self))
                {
                    allInRolled.Add((self.Id, Mathf.Clamp(desired, 1, self.MaxTier)));
                    allInRolledIds.Add(self.Id);
                }
            }
            foreach (var rolled in allInRolled)
                stats.SetUpgradeTier(rolled.id, rolled.tier);
            foreach (var old in allInOld)
                if (!allInRolledIds.Contains(old.id))
                    stats.SetUpgradeTier(old.id, 0);
            foreach (var rolled in allInRolled)
                session.Upgrades?.UpgradeAcquired(rolled.id);
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
            ExpeditionSnapshotService.CaptureFromSession(session);
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
            SelectionTimeRemaining = 0f;
            if (session != null && session.Clock.HasPauseReason(PauseReason.TacticalDraft))
                session.ConfirmDraftSelectionAndResume();
            if (changed) OnOffersChanged?.Invoke();
        }
        private void ResetForSession()
        {
            IsOpen = false; offers.Clear(); candidates.Clear(); resolving = false; rerollsUsed = 0;
            SelectionTimeRemaining = 0f;
            OnOffersChanged?.Invoke();
        }

        internal UpgradeDraftSnapshot CaptureSnapshot()
        {
            var snapshot = new UpgradeDraftSnapshot
            {
                isOpen = IsOpen,
                rerollsUsed = rerollsUsed,
                selectionTimeRemaining = SelectionTimeRemaining
            };
            for (int i = 0; i < offers.Count; i++)
                snapshot.offerIds.Add(offers[i].Card.Id);
            return snapshot;
        }

        internal void RestoreFromSnapshot(UpgradeDraftSnapshot snapshot)
        {
            if (snapshot == null) return;
            resolving = false;
            offers.Clear();
            candidates.Clear();
            IsOpen = snapshot.isOpen;
            rerollsUsed = Mathf.Max(0, snapshot.rerollsUsed);
            SelectionTimeRemaining = Mathf.Max(0f, snapshot.selectionTimeRemaining);
            if (snapshot.offerIds != null && stats != null)
            {
                for (int i = 0; i < snapshot.offerIds.Count; i++)
                {
                    if (UpgradeCatalog.TryGet(snapshot.offerIds[i], out UpgradeCardDefinition card))
                        offers.Add(new UpgradeOffer(card, stats));
                }
            }
            if (IsOpen && offers.Count == 0 && progression != null && progression.PendingChoices == 0)
                IsOpen = false;
            OnOffersChanged?.Invoke();
        }
    }
}
