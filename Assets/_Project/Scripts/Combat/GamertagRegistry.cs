using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace EchoScrap.Combat
{
    public static class GamertagRegistry
    {
        public static string DisplayName(string input) =>
            Regex.Replace((input ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");

        public static string CanonicalName(string name) => DisplayName(name).ToUpperInvariant();

        public static string Key(string name)
        {
            using var hash = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(CanonicalName(name));
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static IEnumerator Reserve(string name, Action<string> finish)
        {
            string key = Key(name);
            string pref = "EchoScrap.PendingGamertag." + key;
            // Reuse our receipt if Firebase accepted the write but the response was lost.
            string receipt = PlayerPrefs.GetString(pref, "");
            if (receipt.Length == 0)
            {
                receipt = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(pref, receipt);
                PlayerPrefs.Save();
            }
            string url = EventLeaderboardSync.DefaultDatabaseUrl + "/gamertags/" + key + ".json";
            string json = "\"" + receipt + "\"";
            using var put = UnityWebRequest.Put(url, Encoding.UTF8.GetBytes(json));
            put.SetRequestHeader("Content-Type", "application/json");
            put.SetRequestHeader("if-match", "null_etag");
            put.timeout = 8;
            yield return put.SendWebRequest();
            if (put.result == UnityWebRequest.Result.Success) { finish(null); yield break; }

            // Rules may reject an existing immutable node before the ETag check.
            using var read = UnityWebRequest.Get(url);
            read.timeout = 5;
            yield return read.SendWebRequest();
            if (read.result == UnityWebRequest.Result.Success)
            {
                string stored = read.downloadHandler.text.Trim();
                if (stored == json) { finish(null); yield break; }
                if (stored != "null")
                {
                    PlayerPrefs.DeleteKey(pref);
                    finish("Ese gamertag ya está en uso. Elige otro.");
                    yield break;
                }
            }
            finish("No se pudo verificar el gamertag. Revisa tu conexión y vuelve a intentar.");
        }

        public static void ForgetReceipt(string name) =>
            PlayerPrefs.DeleteKey("EchoScrap.PendingGamertag." + Key(name));
    }
}
