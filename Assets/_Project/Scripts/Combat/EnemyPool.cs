using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Pool reutilizable de instancias de EnemyActor libre de asignaciones en tiempo de combate.
    /// </summary>
    public sealed class EnemyPool : MonoBehaviour
    {
        private readonly Dictionary<EnemyArchetypeId, EnemySpawnGeometry> spawnGeometry =
            new Dictionary<EnemyArchetypeId, EnemySpawnGeometry>(5);
        private readonly Dictionary<EnemyArchetypeId, Stack<EnemyActor>> inactivePools =
            new Dictionary<EnemyArchetypeId, Stack<EnemyActor>>();
        private readonly List<EnemyActor> allCreatedActors = new List<EnemyActor>(256);

        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private CorrosivePuddlePool puddlePool;

        public int TotalCreatedCount => allCreatedActors.Count;
        private CombatSession subscribedSession;
        private Action<EnemyActor> returnCallback;
        private Action<EnemyActor> ReturnCallback => returnCallback ??= ReturnToPool;

        private void OnEnable() => SubscribeToSession();
        private void OnDisable() => UnsubscribeFromSession();

        private void SubscribeToSession()
        {
            if (subscribedSession == session) return;
            UnsubscribeFromSession();
            if (session == null || !isActiveAndEnabled) return;
            subscribedSession = session;
            subscribedSession.SessionReset += DespawnAllActive;
        }

        private void UnsubscribeFromSession()
        {
            if (subscribedSession != null)
                subscribedSession.SessionReset -= DespawnAllActive;
            subscribedSession = null;
        }

        public void BindReferences(CombatSession combatSession, PlayerHealth player, CorrosivePuddlePool corrosivePool = null)
        {
            session = combatSession;
            playerHealth = player;
            if (corrosivePool != null)
                puddlePool = corrosivePool;
            SubscribeToSession();
        }

        public void Prewarm(EnemyArchetypeId archetypeId, int count)
        {
            if (!inactivePools.TryGetValue(archetypeId, out Stack<EnemyActor> stack))
            {
                stack = new Stack<EnemyActor>(count);
                inactivePools[archetypeId] = stack;
            }

            EnemyArchetypeData data = EnemyArchetypeData.CreateDefault(archetypeId);
            while (stack.Count < count)
            {
                EnemyActor actor = CreateInstance(data);
                actor.gameObject.SetActive(false);
                stack.Push(actor);
            }
        }

        public EnemySpawnGeometry GetSpawnGeometry(EnemyArchetypeData data)
        {
            if (spawnGeometry.TryGetValue(data.ArchetypeId, out EnemySpawnGeometry geometry) && geometry.Matches(data))
                return geometry;
            EnemyActor model;
            if (inactivePools.TryGetValue(data.ArchetypeId, out Stack<EnemyActor> stack) && stack.Count > 0)
            {
                model = stack.Peek();
                model.PrepareForPool(data, session, playerHealth);
            }
            else
            {
                model = CreateInstance(data);
                ReturnToPool(model);
            }
            geometry = new EnemySpawnGeometry(data, model.CaptureSpawnModelBounds());
            spawnGeometry[data.ArchetypeId] = geometry;
            return geometry;
        }

        public EnemyActor Spawn(EnemyArchetypeId archetypeId, Vector3 position)
        {
            EnemyArchetypeData data = EnemyArchetypeData.CreateDefault(archetypeId);
            return Spawn(data, position);
        }

        public EnemyActor Spawn(EnemyArchetypeData data, Vector3 position)
        {
            if (!inactivePools.TryGetValue(data.ArchetypeId, out Stack<EnemyActor> stack))
            {
                stack = new Stack<EnemyActor>(32);
                inactivePools[data.ArchetypeId] = stack;
            }

            EnemyActor actor = null;
            while (stack.Count > 0 && actor == null)
            {
                actor = stack.Pop();
            }

            if (actor == null)
            {
                actor = CreateInstance(data);
            }

            actor.ReturnToPoolCallback = ReturnCallback;
            if (puddlePool != null)
                actor.BindPuddlePool(puddlePool);
            actor.InitializeOrResetFromPool(data, session, playerHealth, position);
            return actor;
        }

        public void ReturnToPool(EnemyActor actor)
        {
            if (actor == null)
                return;

            CombatTargetRegistry.Unregister(actor);
            actor.gameObject.SetActive(false);

            EnemyArchetypeId id = actor.ArchetypeId;
            if (!inactivePools.TryGetValue(id, out Stack<EnemyActor> stack))
            {
                stack = new Stack<EnemyActor>(32);
                inactivePools[id] = stack;
            }

            if (!stack.Contains(actor))
                stack.Push(actor);
        }

        public void DespawnAllActive()
        {
            for (int i = 0; i < allCreatedActors.Count; i++)
            {
                EnemyActor actor = allCreatedActors[i];
                if (actor != null && actor.gameObject.activeSelf)
                    ReturnToPool(actor);
            }
        }

        private EnemyActor CreateInstance(EnemyArchetypeData data)
        {
            var go = new GameObject($"Enemy_{data.DisplayName}_{allCreatedActors.Count}");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.layer = PhysicsLayers.EnemyLayer;
            go.tag = PhysicsLayers.EnemyTag;

            EnemyActor actor = data.ArchetypeId == EnemyArchetypeId.GolgoraBoss
                ? go.AddComponent<GolgoraActor>()
                : go.AddComponent<EnemyActor>();
            actor.ReturnToPoolCallback = ReturnCallback;
            if (puddlePool != null)
                actor.BindPuddlePool(puddlePool);
            actor.PrepareForPool(data, session, playerHealth);
            allCreatedActors.Add(actor);
            return actor;
        }
    }
}
