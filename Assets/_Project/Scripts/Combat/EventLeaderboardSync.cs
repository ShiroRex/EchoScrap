using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace EchoScrap.Combat
{
    [Serializable]
    public sealed class EventPlayerRecord
    {
        public string uid;
        public string sessionId;
        public string playerName, gamertagNormalized;
        public int fusions;
        public int kills;
        public int collectibles;
        public int ownedInVault;
        public int discoveredCount;
        public int highestRarity;
        public string highestRarityName;
        public int survivalSeconds;
        public int score;
        public int rawScore, modeMinutes, rulesVersion;
        public string status;
        public long updatedAt;
    }

    [Serializable]
    internal sealed class FirebaseAnonAuthResponse
    {
        public string idToken;
        public string localId;
    }

    /// <summary>
    /// Real-time Firebase Realtime Database synchronization for live event leaderboards.
    /// Tracks player name, enemy kills, collectibles obtained, vault progress and run status.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EventLeaderboardSync : MonoBehaviour
    {
        public const string DefaultDatabaseUrl = "https://echoscrap-default-rtdb.firebaseio.com";
        public const string DefaultApiKey = "AIzaSyBgdPPFayt0SzNf95YMdTnONFtpZd2DflA";
        public const string DefaultProjectId = "echoscrap";
        public const string PlayerNamePrefKey = "EchoScrap.Event.PlayerName";
        private static string NameKey => Application.isBatchMode ? PlayerNamePrefKey + ".Test" : PlayerNamePrefKey;

        public static EventLeaderboardSync Instance { get; private set; }
        public static bool EnableNetworkInTests { get; set; }

        [Serializable]
        public sealed class RankingLedger
        {
            public List<EventPlayerRecord> pending = new List<EventPlayerRecord>();
            public string name, interrupted;
            public bool eligible;

            public void NewCollection(string input)
            {
                name = SanitizeName(input);
                // A named collection reaches this point after Firebase reserves its tag.
                eligible = name.Length > 0;
            }

            public bool Claim()
            {
                if (!eligible) return false;
                eligible = false;
                return true;
            }
        }

        // Keep automated play sessions isolated from the player's collection.
        private static string LedgerKey => Application.isBatchMode ? "EchoScrap.Rank.Test.v2" : "EchoScrap.Rank.v2";
        private static RankingLedger ReadLedger() =>
            JsonUtility.FromJson<RankingLedger>(PlayerPrefs.GetString(LedgerKey, "{}")) ?? new RankingLedger();
        private static void SaveLedger(RankingLedger ledger)
        {
            PlayerPrefs.SetString(LedgerKey, JsonUtility.ToJson(ledger));
            PlayerPrefs.Save();
        }

        public static string SanitizeName(string raw) => string.IsNullOrWhiteSpace(raw) ? "" :
            raw.Trim().Substring(0, Math.Min(raw.Trim().Length, 20));
        public static string PlayerName => PlayerPrefs.GetString(NameKey, "");
        public static event Action<string> OnPlayerNameChanged;
        public static void StartNewPlayerSession(string input)
        {
            Instance?.CancelAttempt();
            var ledger = ReadLedger();
            ledger.pending ??= new List<EventPlayerRecord>();
            if (!string.IsNullOrEmpty(ledger.interrupted)) QueueCancellation(ledger);
            ledger.NewCollection(input);
            PlayerPrefs.SetString(NameKey, ledger.name);
            SaveLedger(ledger);
            OnPlayerNameChanged?.Invoke(ledger.name);
        }

        public static int CalculateScore(int rawScore, int modeMinutes) =>
            modeMinutes == 8 ? rawScore / 2 : rawScore;

        private CombatGameBootstrap game;
        private FusionStation station;
        private string idToken = "", authUid = "";
        private bool active, sending, hasPending;
        private float retryAt, nextUpload;
        private int mode, collected, discoveries, fusions, highest;
        private string attemptId, attemptName, normalizedName;
        public EventPlayerRecord LastRecord { get; private set; }

        public void Initialize(CombatGameBootstrap bootstrap, FusionStation fusionStation)
        {
            Instance = this;
            game = bootstrap;
            station = fusionStation;
            game.Session.Events.OnSessionStateChanged += OnState;
            game.Session.SessionReset += CancelAttempt;
            var ledger = ReadLedger();
            if (!string.IsNullOrEmpty(ledger.interrupted)) QueueCancellation(ledger);
            hasPending = ledger.pending?.Count > 0;
        }

        public void BeginRankedExpedition(bool longMode)
        {
            var ledger = ReadLedger();
            if (active || !ledger.Claim()) return;
            active = true;
            attemptId = ledger.interrupted = Guid.NewGuid().ToString("N");
            SaveLedger(ledger);
            attemptName = ledger.name;
            normalizedName = GamertagRegistry.CanonicalName(attemptName);
            mode = longMode ? 8 : 3;
            collected = 0;
            highest = -1;
            discoveries = fusions = 0;
            nextUpload = 0;
        }

        private static void QueueCancellation(RankingLedger ledger)
        {
            ledger.pending ??= new List<EventPlayerRecord>();
            ledger.pending.Add(new EventPlayerRecord { sessionId = ledger.interrupted, status = "CANCELADA" });
            ledger.interrupted = null;
            ledger.eligible = true;
            SaveLedger(ledger);
        }

        private void CancelAttempt()
        {
            if (!active) return;
            active = false;
            QueueCancellation(ReadLedger());
            hasPending = true;
        }
        public void NotifyCollectibleObtained(int id, bool firstDiscovery, bool fromFusion)
        {
            if (!active) return;
            if (firstDiscovery) discoveries++;
            if (fromFusion) fusions++;
            collected++;
            highest = Mathf.Max(highest, (int)FusionCatalog.Rarities[id]);
        }

        public static int RawPoints(int kills, int units, int discoveries, int highest, int seconds) =>
            100 * kills + 300 * units + 150 * discoveries + 250 * (highest + 1) + 2 * seconds;

        private EventPlayerRecord Snapshot(string status, float gameplayTime)
        {
            var kills = game.Hud.KillCount;
            var seconds = Mathf.FloorToInt(gameplayTime);
            var raw = RawPoints(kills, collected, discoveries, highest, seconds);
            return new EventPlayerRecord {
                sessionId = attemptId, playerName = attemptName, gamertagNormalized = normalizedName, modeMinutes = mode, rulesVersion = 2,
                kills = kills, collectibles = collected, discoveredCount = discoveries, survivalSeconds = seconds,
                highestRarity = highest, ownedInVault = station.Progress.OwnedCount,
                highestRarityName = highest < 0 ? "" : FusionCatalog.RarityNames[highest],
                rawScore = raw, score = CalculateScore(raw, mode),
                status = status, fusions = fusions,
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private void OnState(SessionStateEvent change)
        {
            if (!active || (change.State != SessionState.Victory && change.State != SessionState.Defeat)) return;
            active = false;
            LastRecord = Snapshot(change.State == SessionState.Victory ? "VICTORIA" : "DERROTA", change.GameplayTime);
            var ledger = ReadLedger();
            ledger.interrupted = null;
            ledger.pending ??= new List<EventPlayerRecord>();
            ledger.pending.Add(LastRecord);
            SaveLedger(ledger);
            hasPending = true;
        }

        private void Update()
        {
            if (sending || Time.unscaledTime < retryAt || (Application.isBatchMode && !EnableNetworkInTests)) return;
            if (hasPending)
            {
                var queue = ReadLedger().pending;
                if (queue != null && queue.Count > 0) { StartCoroutine(PushRecordCoroutine(queue[0], true)); return; }
                hasPending = false;
            }
            if (!active || Time.unscaledTime < nextUpload) return;
            nextUpload = Time.unscaledTime + 2f;
            StartCoroutine(PushRecordCoroutine(Snapshot("EN PARTIDA", game.Session.GameplayTime), false));
        }

        private IEnumerator PushRecordCoroutine(EventPlayerRecord record, bool persistent)
        {
            sending = true;
            if (string.IsNullOrEmpty(idToken)) yield return AuthenticateAnonymouslyCoroutine();
            string url = $"{DefaultDatabaseUrl}/players/{record.sessionId}.json";
            if (!string.IsNullOrEmpty(idToken)) url += "?auth=" + UnityWebRequest.EscapeURL(idToken);
            record.uid = authUid;
            using var request = record.status == "CANCELADA" ? UnityWebRequest.Delete(url) : UnityWebRequest.Put(url, Encoding.UTF8.GetBytes(JsonUtility.ToJson(record).TrimEnd('}') + ",\"heartbeat\":{\".sv\":\"timestamp\"}}"));
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 8;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                if (!persistent) { sending = false; yield break; }
                var ledger = ReadLedger();
                ledger.pending.RemoveAll(r => r.sessionId == record.sessionId);
                SaveLedger(ledger);
                hasPending = ledger.pending.Count > 0;
            }
            else
            {
                if (request.responseCode == 401 || request.responseCode == 403) idToken = "";
                retryAt = Time.unscaledTime + 15;
            }
            sending = false;
        }

        private IEnumerator AuthenticateAnonymouslyCoroutine()
        {
            string authUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={DefaultApiKey}";
            byte[] body = Encoding.UTF8.GetBytes("{\"returnSecureToken\":true}");

            using var req = new UnityWebRequest(authUrl, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(req.downloadHandler.text))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<FirebaseAnonAuthResponse>(req.downloadHandler.text);
                    if (parsed != null && !string.IsNullOrEmpty(parsed.idToken))
                    {
                        idToken = parsed.idToken;
                        if (!string.IsNullOrEmpty(parsed.localId))
                            authUid = parsed.localId;
                    }
                }
                catch (Exception)
                {
                    // Keep default uid if parsing fails.
                }
            }
        }

        private void OnDestroy()
        {
            if (game != null && game.Session != null) { game.Session.Events.OnSessionStateChanged -= OnState; game.Session.SessionReset -= CancelAttempt; }
            CancelAttempt();
            if (Instance == this) Instance = null;
        }
    }
}
