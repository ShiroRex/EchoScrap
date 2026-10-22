using System;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public readonly struct ProgressionSnapshot
    {
        public readonly int Level;
        public readonly long CurrentXp;
        public readonly int RequiredXp;
        public readonly long TotalXp;
        public readonly int PendingChoices;

        public ProgressionSnapshot(int level, long currentXp, int requiredXp, long totalXp, int pendingChoices)
        {
            Level = level;
            CurrentXp = currentXp;
            RequiredXp = requiredXp;
            TotalXp = totalXp;
            PendingChoices = pendingChoices;
        }
    }

    /// <summary>XP de la partida y elecciones acumuladas. El draft consume las elecciones por separado.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatProgression : MonoBehaviour
    {
        [SerializeField] private CombatSession session;

        public int Level { get; private set; } = 1;
        public long CurrentXp { get; private set; }
        public int RequiredXp { get; private set; } = 100;
        public long TotalXp { get; private set; }
        public int PendingChoices { get; private set; }
        public ProgressionSnapshot Snapshot => new ProgressionSnapshot(Level, CurrentXp, RequiredXp, TotalXp, PendingChoices);

        public event Action<ProgressionSnapshot> OnExperienceChanged;
        public event Action<int> OnLevelGained;
        public event Action<int> OnPendingChoicesChanged;

        private CombatSession subscribedSession;

        private void OnEnable()
        {
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        public void BindSession(CombatSession combatSession)
        {
            Unsubscribe();
            session = combatSession;
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribedSession == session)
                return;
            Unsubscribe();
            subscribedSession = session;
            if (subscribedSession != null)
                subscribedSession.SessionReset += ResetForSession;
        }

        private void Unsubscribe()
        {
            if (subscribedSession != null)
                subscribedSession.SessionReset -= ResetForSession;
            subscribedSession = null;
        }

        public int AddExperience(int amount)
        {
            if (amount <= 0 || (session != null && (session.IsPaused || !session.IsGameplayFlowActive())))
                return 0;

            TotalXp += amount;
            CurrentXp += amount;
            int gainedLevels = 0;
            // Resolver primero todo el saldo. Un listener puede abrir la pausa táctica al recibir el evento.
            while (CurrentXp >= RequiredXp)
            {
                CurrentXp -= RequiredXp;
                Level++;
                PendingChoices++;
                gainedLevels++;
                RequiredXp = RequiredXp >= 1431655680
                    ? int.MaxValue
                    : Mathf.Max(RequiredXp + 1, Mathf.RoundToInt(RequiredXp * 1.5f));
            }

            if (gainedLevels > 0)
            {
                int firstLevel = Level - gainedLevels + 1;
                for (int level = firstLevel; level <= Level; level++)
                    OnLevelGained?.Invoke(level);
                OnPendingChoicesChanged?.Invoke(PendingChoices);
            }
            OnExperienceChanged?.Invoke(Snapshot);
            return gainedLevels;
        }

        /// <summary>Consume una elección; no reanuda la sesión mientras puedan quedar elecciones o pausas.</summary>
        public bool ConsumePendingChoice()
        {
            if (PendingChoices <= 0)
                return false;
            PendingChoices--;
            OnPendingChoicesChanged?.Invoke(PendingChoices);
            OnExperienceChanged?.Invoke(Snapshot);
            return true;
        }

        public void ResetForSession()
        {
            Level = 1;
            CurrentXp = 0;
            RequiredXp = 100;
            TotalXp = 0;
            PendingChoices = 0;
            OnPendingChoicesChanged?.Invoke(0);
            OnExperienceChanged?.Invoke(Snapshot);
        }
    }
}
