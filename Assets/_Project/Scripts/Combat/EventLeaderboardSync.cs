using System;
using System.Collections;
using System.IO;
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
        public string playerName;
        public int kills;
        public int collectibles;
        public int ownedInVault;
        public int discoveredCount;
        public int highestRarity;
        public string highestRarityName;
        public int survivalSeconds;
        public int score;
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
        public const string SessionIdPrefKey = "EchoScrap.Event.SessionId";

        public static EventLeaderboardSync Instance { get; private set; }
        public static bool EnableNetworkInTests { get; set; }

        private static string currentPlayerName = "";
        private static string currentSessionId = "";
        public static event Action<string> OnPlayerNameChanged;

        public static string PlayerName
        {
            get => currentPlayerName;
            set
            {
                string sanitized = SanitizeName(value);
                if (currentPlayerName == sanitized) return;
                currentPlayerName = sanitized;
                if (!string.IsNullOrEmpty(sanitized))
                {
                    PlayerPrefs.SetString(PlayerNamePrefKey, sanitized);
                    PlayerPrefs.Save();
                }
                OnPlayerNameChanged?.Invoke(currentPlayerName);
                if (Instance != null) Instance.RequestImmediateSync();
            }
        }

        public static string SessionId
        {
            get
            {
                if (string.IsNullOrEmpty(currentSessionId))
                {
                    currentSessionId = "run_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                }
                return currentSessionId;
            }
        }

        public static void StartNewPlayerSession(string playerName = null)
        {
            currentSessionId = "run_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            if (playerName != null)
            {
                currentPlayerName = SanitizeName(playerName);
            }
            if (Instance != null)
            {
                Instance.ResetSessionCounters();
                Instance.RequestImmediateSync();
            }
        }

        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string trimmed = raw.Trim();
            return trimmed.Length <= 20 ? trimmed : trimmed.Substring(0, 20);
        }

        private CombatGameBootstrap game;
        private FusionStation station;
        private string idToken = "";
        private string authUid = "event_booth";
        private bool authAttempted;
        private bool syncPending;
        private bool isSyncing;
        private float nextAllowedSyncTime;
        private int bestKillsInSession;
        private int bestCollectiblesInSession;
        private int lastObservedOwned = -1;
        private int lastObservedKills = -1;
        private SessionState lastObservedState = (SessionState)(-1);
        private bool lastObservedWarehouse;

        public EventPlayerRecord LastRecord { get; private set; }

        public void Initialize(CombatGameBootstrap bootstrap, FusionStation fusionStation)
        {
            Instance = this;
            game = bootstrap;
            station = fusionStation;
            if (string.IsNullOrEmpty(currentPlayerName) && PlayerPrefs.HasKey(PlayerNamePrefKey))
            {
                currentPlayerName = SanitizeName(PlayerPrefs.GetString(PlayerNamePrefKey, ""));
            }
            if (string.IsNullOrEmpty(currentSessionId))
            {
                currentSessionId = SessionId;
            }
            LastRecord = BuildSnapshot();
        }

        public void ResetSessionCounters()
        {
            bestKillsInSession = 0;
            bestCollectiblesInSession = 0;
            lastObservedKills = -1;
            lastObservedOwned = -1;
        }

        public void NotifyCollectibleObtained()
        {
            bestCollectiblesInSession++;
            RequestImmediateSync();
        }

        public void RequestImmediateSync()
        {
            syncPending = true;
            nextAllowedSyncTime = 0f;
            LastRecord = BuildSnapshot();
        }

        private void Update()
        {
            if (game == null || game.Session == null) return;

            int currentKills = game.Hud != null ? game.Hud.KillCount : 0;
            if (currentKills > bestKillsInSession)
                bestKillsInSession = currentKills;

            int owned = station != null && station.Progress != null ? station.Progress.OwnedCount : 0;
            int discovered = station != null && station.Progress != null ? station.Progress.DiscoveredCount : 0;
            int collectiblesMetric = Mathf.Max(bestCollectiblesInSession, Mathf.Max(owned, discovered));
            if (collectiblesMetric > bestCollectiblesInSession)
                bestCollectiblesInSession = collectiblesMetric;

            SessionState state = game.Session.State;
            bool inWarehouse = game.Session.IsInWarehouse;

            if (currentKills != lastObservedKills || owned != lastObservedOwned || state != lastObservedState || inWarehouse != lastObservedWarehouse)
            {
                bool urgent = state != lastObservedState || inWarehouse != lastObservedWarehouse || owned != lastObservedOwned;
                lastObservedKills = currentKills;
                lastObservedOwned = owned;
                lastObservedState = state;
                lastObservedWarehouse = inWarehouse;
                syncPending = true;
                if (urgent) nextAllowedSyncTime = 0f;
            }

            if (syncPending && !isSyncing && Time.unscaledTime >= nextAllowedSyncTime)
            {
                syncPending = false;
                nextAllowedSyncTime = Time.unscaledTime + 1.5f;
                EventPlayerRecord snapshot = BuildSnapshot();
                LastRecord = snapshot;
                SaveLocalBackup(snapshot);
                if (ShouldSendNetworkRequests())
                {
                    StartCoroutine(PushRecordCoroutine(snapshot));
                }
            }
        }

        public EventPlayerRecord BuildSnapshot()
        {
            int currentKills = game != null && game.Hud != null ? game.Hud.KillCount : 0;
            int totalKills = Mathf.Max(bestKillsInSession, currentKills);

            int owned = 0;
            int discovered = 0;
            int highestRarity = -1;

            if (station != null && station.Progress != null)
            {
                owned = station.Progress.OwnedCount;
                discovered = station.Progress.DiscoveredCount;
                for (int id = 0; id < FusionCatalog.Count; id++)
                {
                    if (station.Progress.owned[id] > 0 || station.Progress.IsDiscovered(id))
                    {
                        int r = (int)FusionCatalog.Rarities[id];
                        if (r > highestRarity) highestRarity = r;
                    }
                }
            }

            int totalCollectibles = Mathf.Max(bestCollectiblesInSession, Mathf.Max(owned, discovered));
            int survival = game != null && game.Session != null ? Mathf.FloorToInt(game.Session.GameplayTime) : 0;
            int rarityBonus = Mathf.Max(0, highestRarity + 1) * 250;
            int computedScore = totalKills * 100 + totalCollectibles * 300 + discovered * 150 + rarityBonus + survival * 2;

            string statusText = "EN BÓVEDA";
            if (game != null && game.Session != null)
            {
                if (game.Session.State == SessionState.Victory) statusText = "VICTORIA";
                else if (game.Session.State == SessionState.Defeat) statusText = "DERROTA";
                else if (!game.Session.IsInWarehouse) statusText = "EN PARTIDA";
            }

            string displayPlayerName = string.IsNullOrWhiteSpace(currentPlayerName) ? "JUGADOR" : currentPlayerName;
            string rarityLabel = highestRarity >= 0 && highestRarity < FusionCatalog.RarityNames.Length
                ? FusionCatalog.RarityNames[highestRarity]
                : "SIN COLECCIÓN";

            return new EventPlayerRecord
            {
                uid = authUid,
                sessionId = SessionId,
                playerName = displayPlayerName,
                kills = totalKills,
                collectibles = totalCollectibles,
                ownedInVault = owned,
                discoveredCount = discovered,
                highestRarity = Mathf.Max(0, highestRarity),
                highestRarityName = rarityLabel,
                survivalSeconds = survival,
                score = computedScore,
                status = statusText,
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static bool ShouldSendNetworkRequests()
        {
            if (EnableNetworkInTests) return true;
            return !Application.isBatchMode;
        }

        private static void SaveLocalBackup(EventPlayerRecord record)
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "EchoScrap_EventRanking_LastSession.json");
                File.WriteAllText(path, JsonUtility.ToJson(record, true), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Ignore local disk errors in restricted environments.
            }
        }

        private IEnumerator PushRecordCoroutine(EventPlayerRecord record)
        {
            isSyncing = true;

            if (!authAttempted)
            {
                authAttempted = true;
                yield return AuthenticateAnonymouslyCoroutine();
                if (!string.IsNullOrEmpty(authUid))
                {
                    record.uid = authUid;
                }
            }

            string baseUrl = $"{DefaultDatabaseUrl}/players/{UnityWebRequest.EscapeURL(record.sessionId)}.json";
            string url = !string.IsNullOrEmpty(idToken) ? $"{baseUrl}?auth={UnityWebRequest.EscapeURL(idToken)}" : baseUrl;
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(record));

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success && !string.IsNullOrEmpty(idToken))
                {
                    // Fallback without token in case database rules are public and token expired.
                    using var fallback = new UnityWebRequest(baseUrl, UnityWebRequest.kHttpVerbPUT);
                    fallback.uploadHandler = new UploadHandlerRaw(payload);
                    fallback.downloadHandler = new DownloadHandlerBuffer();
                    fallback.SetRequestHeader("Content-Type", "application/json");
                    fallback.timeout = 5;
                    yield return fallback.SendWebRequest();
                }
            }

            isSyncing = false;
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
            if (Instance == this) Instance = null;
        }
    }
}
