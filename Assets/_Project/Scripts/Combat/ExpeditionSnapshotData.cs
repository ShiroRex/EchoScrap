using System;
using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    [Serializable]
    public sealed class ExpeditionSnapshotData
    {
        public const int CurrentSchemaVersion = 1;

        // Top-level header fields must stay aligned with SaveSlotService.ExpeditionSnapshotHeader.
        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = string.Empty;
        public string attemptId = string.Empty;
        public long revision;
        public int modeMinutes = 3;
        public float gameplayTime;
        public bool isInWarehouse;
        public string fusionSnapshotJson = string.Empty;
        public long profileRevision;
        public double totalPlaySeconds;
        public string sceneName = string.Empty;

        public SessionRngState rng;
        public CombatSessionSnapshot session = new CombatSessionSnapshot();
        public WarehouseTravelSnapshot travel = new WarehouseTravelSnapshot();
        public PlayerSnapshot player = new PlayerSnapshot();
        public CombatProgressionSnapshot progression = new CombatProgressionSnapshot();
        public UpgradeDraftSnapshot draft = new UpgradeDraftSnapshot();
        public FlashlightWeaponSnapshot weapon = new FlashlightWeaponSnapshot();
        public WaveDirectorSnapshot waveDirector = new WaveDirectorSnapshot();
        public BossEncounterSnapshot bossEncounter = new BossEncounterSnapshot();
        public BossArenaSnapshot bossArena = new BossArenaSnapshot();
        public GolgoraSnapshot golgora = new GolgoraSnapshot();
        public MagmaRingPoolSnapshot magmaRings = new MagmaRingPoolSnapshot();
        public List<EnemySnapshot> enemies = new List<EnemySnapshot>();
        public EliteProjectilePoolSnapshot eliteProjectiles = new EliteProjectilePoolSnapshot();
        public CorrosivePuddlePoolSnapshot puddles = new CorrosivePuddlePoolSnapshot();
        public MothExplosionPoolSnapshot mothExplosions = new MothExplosionPoolSnapshot();
        public CombatPickupPoolSnapshot pickups = new CombatPickupPoolSnapshot();
        public CompanionFieldSnapshot companionField = new CompanionFieldSnapshot();
        public CompanionPowerSnapshot companionPowers = new CompanionPowerSnapshot();
        public UpgradeRuntimeSnapshot upgradeRuntime = new UpgradeRuntimeSnapshot();
        public HudSnapshot hud = new HudSnapshot();
        public LeaderboardAttemptSnapshot leaderboard = new LeaderboardAttemptSnapshot();
    }

    [Serializable]
    public sealed class CombatSessionSnapshot
    {
        public int state;
        public int pauseMask;
        public int simulationStep;
        public float gameplayTime;
        public bool isInWarehouse;
        public bool bossIntroRequested;
    }

    [Serializable]
    public sealed class WarehouseTravelSnapshot
    {
        public bool hasSavedReturnPose;
        public Vector3 returnPosition;
        public Quaternion returnRotation = Quaternion.identity;
        public bool hasPendingReturn;
        public Vector3 pendingReturnPosition;
        public Quaternion pendingReturnRotation = Quaternion.identity;
    }

    [Serializable]
    public sealed class PlayerSnapshot
    {
        public PlayerMotorData motor = new PlayerMotorData();

        public float currentHealth;
        public float lastMaxHealth;
        public float protectionRemaining;
        public bool dashInvulnerable;
        public float upgradeProtection;
        public bool isDead;
        public bool isEncounterTransferProtected;
        public bool isBossCinematicProtected;

        public PlayerRuntimeStatsData stats = new PlayerRuntimeStatsData();
    }

    [Serializable]
    public sealed class CombatProgressionSnapshot
    {
        public int level = 1;
        public long currentXp;
        public int requiredXp = 100;
        public long totalXp;
        public int pendingChoices;
        public int queuedExperience;
        public bool dispatching;
        public bool closingRewardGranted;
    }

    [Serializable]
    public sealed class UpgradeDraftSnapshot
    {
        public bool isOpen;
        public int rerollsUsed;
        public float selectionTimeRemaining;
        public List<string> offerIds = new List<string>();
    }

    [Serializable]
    public sealed class FlashlightWeaponSnapshot
    {
        public float weaponTime;
        public int currentBurstId;
        public bool isBurstInitialized;
        public bool isInRestPhase;
        public int emittedPulsesInCurrentBurst;
        public float nextPulseTime;
        public float restEndTime;
        public float stationaryTime;
        public float stationaryDamageBonus;
        public float pulseInterval;
        public float restDuration;
        public float capturedRange;
        public float capturedAngle;
        public float capturedDamagePerPulse;
        public float capturedPulseInterval;
        public float capturedAttackSpeed;
        public float capturedRestDuration;
        public float capturedKnockbackImpulse;
        public float capturedCritChance;
        public float capturedCritMultiplier;
        public bool capturedSolarMode;
        public bool capturedSolarCapacitor;
        public bool capturedVacuumBeacon;
        public int capturedWallSlamTier;
        public float capturedRearDamage;
        public Vector3 currentAimDirection = Vector3.forward;
        public int guaranteedPending;
        public int guaranteedActive;
        public int totalPulsesEmitted;
        public int burstKills;
    }

    [Serializable]
    public sealed class WaveDirectorSnapshot
    {
        public float nextSpawnTime;
        public float nextMinionTime;
        public int phaseIndex = -1;
        public float nextEliteTime;
        public bool nextEliteCopper;
        public int minionOrderOffset;
        public bool isBossPhase;
        public bool bossActive;
        public bool bossMinionsSuppressed;
        public int totalSpawns;
        public int totalDiscards;
        public int groupsAttemptedCount;
        public int bossMinionWavesCount;
        public int lastGroupSpawnedCount;
        public int[] discardedByReason = new int[8];
    }

    [Serializable]
    public sealed class BossEncounterSnapshot
    {
        public bool isEncounterActive;
        public bool introInProgress;
        public float introRemaining;
        public bool introWaitingForClearance;
        public bool closingRewardResolved;
        public int closingXpReward;
    }

    [Serializable]
    public sealed class BossArenaSnapshot
    {
        public bool isClosed;
        public bool isTransferring;
        public float transferAge;
        public bool transferred;
        public int nextVent;
        public int spawnedMinions;
        public int pendingMinions;
        public List<BossReinforcementSnapshot> pending = new List<BossReinforcementSnapshot>();
    }

    [Serializable]
    public sealed class BossReinforcementSnapshot
    {
        public int archetypeId;
        public float age;
        public Vector3 position;
    }

    [Serializable]
    public sealed class GolgoraSnapshot
    {
        public bool exists;
        public bool active;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 velocity;
        public float currentHealth;
        public float maxHealth;
        public float assignedMoveSpeed;
        public Vector3 facingDirection = Vector3.forward;
        public int state;
        public int currentAttackKind;
        public bool isPhaseTwo;
        public bool cercoSequence;
        public bool preferSweep;
        public float jumpHeight;
        public Vector3 jumpStart;
        public bool jumpWasKinematic;
        public bool jumpHadCollider;
        public float controlImmunityRemaining;
        public float bossTime;
        public float phaseTime;
        public float nextStompTime;
        public float nextSweepTime;
        public float nextBarrageTime;
        public float nextCercoTime;
        public float nextFissureTime;
        public float nextRainTime;
        public float firstCercoGap;
        public Vector3 arenaCenter;
        public float arenaRadius = 22f;
        public Vector3 stompOrigin;
        public Vector3 attackDirection = Vector3.forward;
        public Vector3[] barrageTargets = new Vector3[5];
        public Vector3[] patternTargets = new Vector3[5];
        public float slowFraction;
        public float slowRemaining;
        public float frozenRemaining;
        public float stunRemaining;
        public float cableSlow;
        public float cableSlowRemaining;
        public float cableDamageReadyAt;
        public int stompsExecuted;
        public int sweepsExecuted;
        public int barragesExecuted;
        public int fissuresExecuted;
        public int rainsExecuted;
        public int cercosExecuted;
        public int cercoPulsesEmitted;
    }

    [Serializable]
    public sealed class MagmaRingPoolSnapshot
    {
        public bool isGapPreviewVisible;
        public float previewGapAngle;
        public int totalEmitted;
        public List<MagmaRingSnapshot> rings = new List<MagmaRingSnapshot>();
    }

    [Serializable]
    public sealed class MagmaRingSnapshot
    {
        public Vector3 center;
        public float radius;
        public float maximumRadius;
        public float speed;
        public float width;
        public float gap;
        public float gapSize;
        public float damage;
        public int sourceId;
        public float visualAge;
        public float residueRemaining;
        public Vector3 previousPlayer;
        public bool playerResolved;
        public bool active;
    }

    [Serializable]
    public sealed class EnemySnapshot
    {
        public int archetypeId;
        public bool isBossMinion;
        public bool isArenaMinion;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 velocity;
        public Vector3 frozenVelocity;
        public bool hasFrozenVelocity;

        public float currentHealth;
        public float assignedMoveSpeed;
        public Vector3 knockbackVelocity;
        public float wallSlamTimeRemaining;
        public int wallSlamTier;
        public Vector3 facingDirection = Vector3.forward;
        public float nextContactReadyTime;
        public float attackCooldownTimer;
        public float fuseTimer;
        public float telegraphTimer;
        public bool isFuseActive;
        public float slowFraction;
        public float slowRemaining;
        public float frozenRemaining;
        public float stunRemaining;
        public float frozenVisualCoverage;
        public int coldHits;
        public float vulnerabilityBonus;
        public float vulnerabilityRemaining;
        public float cableSlow;
        public float cableSlowRemaining;
        public float cableDamageReadyAt;
        public int mortarShotsFiredCount;

        public bool hasColossusCharge;
        public int colossusState;
        public float colossusPhaseTime;
        public Vector3 colossusLockedDirection;
        public Vector3 colossusWarningOrigin;
        public float colossusWarningDistance;
        public float colossusCooldownRemaining;
        public int colossusChargesStarted;
        public int colossusWallStops;
        public int colossusPlayerHitAttempts;

        public bool hasElite;
        public int eliteState;
        public float elitePhaseTime;
        public Vector3 eliteLockedDirection;
        public Vector3 eliteWarningOrigin;
        public float eliteCooldownRemaining;
        public int eliteAttacksResolved;
        public int elitePlayerHitAttempts;

        public bool hasMoth;
        public bool mothIsPrimed;
        public bool mothHasDetonated;
        public float mothFuseRemaining;
        public float mothActiveFuseDuration;
        public float mothBlinkTimer;
        public bool mothIsWhite;
    }

    [Serializable]
    public sealed class EliteProjectilePoolSnapshot
    {
        public int totalFired;
        public int playerHitAttempts;
        public List<EliteBoltSnapshot> bolts = new List<EliteBoltSnapshot>();
    }

    [Serializable]
    public sealed class EliteBoltSnapshot
    {
        public Vector3 position;
        public Vector3 direction;
        public int source;
        public float damage;
        public float speed;
        public float remaining;
        public float age;
    }

    [Serializable]
    public sealed class CorrosivePuddlePoolSnapshot
    {
        public int sequenceId;
        public int totalProjectilesLaunched;
        public int totalPuddlesCreated;
        public List<PuddleProjectileSnapshot> projectiles = new List<PuddleProjectileSnapshot>();
        public List<PuddleSnapshot> puddles = new List<PuddleSnapshot>();
    }

    [Serializable]
    public sealed class PuddleProjectileSnapshot
    {
        public float elapsed;
        public int reservedPuddleIndex = -1;
        public Vector3 origin;
        public Vector3 target;
        public float flightDuration;
        public float arcHeight;
        public float impactRadius;
        public float directImpactDamage;
        public int directImpactSource;
        public float puddleRadius;
        public float puddleDuration;
        public float tickDamage;
        public float tickInterval;
        public int sourceId;
        public int damageSource;
        public int style;
    }

    [Serializable]
    public sealed class PuddleSnapshot
    {
        public int slotIndex;
        public int sourceId;
        public int sequenceId;
        public Vector3 center;
        public float radius;
        public float duration;
        public float elapsed;
        public float nextTickAge;
        public float tickDamage;
        public float tickInterval;
        public float fadeAlpha;
        public int damageSource;
        public int style;
        public bool active;
        public bool reserved;
    }

    [Serializable]
    public sealed class MothExplosionPoolSnapshot
    {
        public int totalExplosions;
        public List<MothBlastSnapshot> pending = new List<MothBlastSnapshot>();
    }

    [Serializable]
    public sealed class MothBlastSnapshot
    {
        public int source;
        public Vector3 position;
        public float radius;
        public float damage;
        public float knockback;
    }

    [Serializable]
    public sealed class CombatPickupPoolSnapshot
    {
        public int nextId = 1;
        public float nextMedkitDropTime;
        public long totalSpawnedExperience;
        public long totalCollectedExperience;
        public int totalCollectedMedkits;
        public float experienceRemainder;
        public List<CombatPickupSnapshot> pickups = new List<CombatPickupSnapshot>();
    }

    [Serializable]
    public sealed class CombatPickupSnapshot
    {
        public int kind;
        public Vector3 position;
        public int id;
        public int experience;
        public int gemCount;
        public List<int> sharpGemHitEnemyIndices;
        public bool attracted;
        public float age;
        public float bobPhase;
        public bool globalFlight;
        public Vector3 flightStart;
        public float flightAge;
        public float flightDuration;
    }

    [Serializable]
    public sealed class CompanionFieldSnapshot
    {
        public float elapsed;
        public List<CompanionPickupSnapshot> pickups = new List<CompanionPickupSnapshot>();
    }

    [Serializable]
    public sealed class CompanionPickupSnapshot
    {
        public int id;
        public Vector3 ground;
        public float life;
    }

    [Serializable]
    public sealed class CompanionPowerSnapshot
    {
        public float[] shieldCooldowns = new float[3];
        public float[] cooldowns = Array.Empty<float>();
        public bool boltActive;
        public float boltLife;
        public Vector3 boltPosition;
        public Vector3 boltDirection;
        public List<int> boltHitEnemyIndices = new List<int>();
        public float speedBuff;
        public int kills;
        public int rescuesUsed;
        public List<CompanionBurnSnapshot> burns = new List<CompanionBurnSnapshot>();
    }

    [Serializable]
    public sealed class CompanionBurnSnapshot
    {
        public int enemyIndex = -1;
        public int ticks;
        public int copyIndex;
        public float nextTime;
    }

    [Serializable]
    public sealed class UpgradeRuntimeSnapshot
    {
        public int[] attackTiers = Array.Empty<int>();
        public Vector3 dashStart;
        public float wallSlamReadyAt;
        public bool trackingDash;
        public bool hasReturnMarker;
        public float time;
        public float blizzardTimer;
        public float turretTimer;
        public float regenTimer;
        public float emergencyReady;
        public float overheal;
        public int gems;
        public int activeDecoys;
        public ulong nextPlacementOrder;
        public int explosionsResolved;
        public int projectilesFired;
        public int chainsTriggered;
        public float reactiveReadyAt;
        public int batteryGems;
        public float puddleTimer;
        public float discTimer;
        public float boomerangTimer;
        public float orbitalTimer;
        public float parabolicTimer;
        public float whipTimer;
        public float auraTimer;
        public Vector3 lastTrailPos;
        public bool trailInitialized;

        public List<UpgradeProjectileSnapshot> projectiles = new List<UpgradeProjectileSnapshot>();
        public List<UpgradeDeviceSnapshot> devices = new List<UpgradeDeviceSnapshot>();
        public List<ElectricCableSnapshot> cables = new List<ElectricCableSnapshot>();
        public List<UpgradeBlastSnapshot> blasts = new List<UpgradeBlastSnapshot>();
        public List<VoltaicChainSnapshot> chains = new List<VoltaicChainSnapshot>();
        public List<GroundZoneSnapshot> zones = new List<GroundZoneSnapshot>();
    }

    [Serializable]
    public sealed class UpgradeProjectileSnapshot
    {
        public bool piercing;
        public Vector3 position;
        public Vector3 direction;
        public float damage;
        public float speed;
        public float life;
        public float freeze;
        public float radius;
        public int sourceType;
        public int damageType;
        public int targetEnemyIndex = -1;
        public List<int> hitEnemyIndices = new List<int>();
        public int mode;
        public float floatData;
        public bool boolFlag;
        public float elapsed;
        public float duration;
        public Vector3 startPosition;
        public Vector3 endPosition;
        public List<int> hitCooldownKeys = new List<int>();
        public List<float> hitCooldownValues = new List<float>();
    }

    [Serializable]
    public sealed class GroundZoneSnapshot
    {
        public int kind;
        public Vector3 position;
        public float radius;
        public float damagePerTick;
        public float tickInterval;
        public float tickTimer;
        public float life;
        public float slow;
        public float vulnerability;
        public int sourceType;
        public int damageType;
    }

    [Serializable]
    public sealed class UpgradeDeviceSnapshot
    {
        public bool fresh;
        public int kind;
        public Vector3 position;
        public float life;
        public bool infiniteLife;
        public float damage;
        public float shotTimer;
        public float burstTimer;
        public float placedAt;
        public int shots;
        public ulong placementOrder;
    }

    [Serializable]
    public sealed class ElectricCableSnapshot
    {
        public int slotIndex;
        public Vector3 start;
        public Vector3 end;
        public float expiresAt;
        public float damage;
    }

    [Serializable]
    public sealed class UpgradeBlastSnapshot
    {
        public Vector3 position;
        public float damage;
        public float radius;
        public float delay;
        public float stun;
        public float push;
        public float freeze;
        public bool implosion;
        public bool echo;
        public bool shockwave;
        public int sourceType;
        public int damageType;
    }

    [Serializable]
    public sealed class VoltaicChainSnapshot
    {
        public int[] targetEnemyIndices = new int[6];
        public int count;
        public int next;
        public int order;
        public int previousEnemyIndex = -1;
        public Vector3 origin;
        public Vector3 previous;
        public float damage;
        public float radius;
        public float wait;
        public float hopDelay;
    }

    [Serializable]
    public sealed class HudSnapshot
    {
        public int kills;
    }

    [Serializable]
    public sealed class LeaderboardAttemptSnapshot
    {
        public bool active;
        public string attemptId = string.Empty;
        public string attemptName = string.Empty;
        public string normalizedName = string.Empty;
        public int mode = 3;
        public int collected;
        public int discoveries;
        public int fusions;
        public int highest = -1;
    }
}
