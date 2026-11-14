using System;
using System.Collections.Generic;

namespace EchoScrap.Core
{
    public enum UpgradeId
    {
        FLASH_01, FLASH_02, FLASH_03, FLASH_04, FLASH_05,
        DASH_01, DASH_02, DASH_03, DASH_04,
        BIO_01, BIO_02, BIO_03, BIO_04,
        GREED_01, GREED_02, GREED_03, GREED_04, GREED_05,
        CRIT_01, CRIT_02, CRIT_03, CRIT_04, CRIT_05,
        DEMO_01, DEMO_02, DEMO_03, DEMO_04,
        SCRAP_01, SCRAP_02, SCRAP_03, SCRAP_04,
        CRYO_01, CRYO_02, CRYO_03, CRYO_04,
        FLASH_06, FLASH_07, FLASH_08, DASH_05, DASH_06, DASH_07, GREED_06, BIO_05
    }

    public static class UpgradeRules
    {
        public const int Count = (int)UpgradeId.BIO_05 + 1;
        private static readonly Dictionary<string, UpgradeId> ids = BuildIds();
        private static Dictionary<string, UpgradeId> BuildIds()
        {
            var result = new Dictionary<string, UpgradeId>(Count, StringComparer.Ordinal);
            foreach (UpgradeId id in Enum.GetValues(typeof(UpgradeId))) result.Add(id.ToString(), id);
            return result;
        }
        public static bool TryGetId(string id, out UpgradeId value)
        {
            value = default;
            return id != null && ids.TryGetValue(id, out value);
        }
        public static int MaxTier(UpgradeId id) => id switch
        {
            UpgradeId.FLASH_05 or UpgradeId.DASH_04 or UpgradeId.BIO_04 or UpgradeId.GREED_05 or
            UpgradeId.CRIT_05 or UpgradeId.DEMO_04 or UpgradeId.SCRAP_04 or UpgradeId.CRYO_04 => 1,
            UpgradeId.FLASH_01 or UpgradeId.FLASH_02 or UpgradeId.DASH_01 or UpgradeId.BIO_01 or
            UpgradeId.GREED_01 or UpgradeId.GREED_02 or UpgradeId.CRIT_01 or UpgradeId.CRIT_02 or
            UpgradeId.DEMO_01 or UpgradeId.SCRAP_01 or UpgradeId.CRYO_01 => 5,
            _ => 3
        };
    }
}
