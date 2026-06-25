using System.Xml.Serialization;
using Rocket.API;

namespace VaultBackpack
{
    public sealed class VaultBackpackConfig : IRocketPluginConfiguration
    {
        public string ConnectionString;
        public string TableName;
        public string CoinsTable;

        // starting size for new players
        public byte DefaultWidth;
        public byte DefaultHeight;
        public byte MaxHeight;   // cap; 0 = no limit

        // cost per +1 row upgrade
        public long UpgradeCoinsCost;
        public ushort UpgradeItemId;     // 0 = no item required
        public int UpgradeItemAmount;

        // barricade used to open the backpack UI (vanilla locker recommended)
        public ushort VaultBarricadeId;
        // death drop — 0 = disabled
        public ushort VaultDeadboxBarricadeId;

        public string MsgOpened;
        public string MsgAlreadyOpen;
        public string MsgUpgradeSuccess;
        public string MsgMaxSize;
        public string MsgInsufficientCoins;
        public string MsgInsufficientItems;
        public string MsgUpgradeOpen;    // shown when upgrade vault opens
        public string MsgUpgradeReturn;  // items returned because not enough
        public string MsgAdminSet;

        public void LoadDefaults()
        {
            ConnectionString   = "Server=mysql-singapore.restoremonarchy.com;Database=s203_unturned;User Id=u203_JE51bXhjxa;Password=mejv0F@ZPftqRMtUA@.+MfE!;";
            TableName          = "sv_vault_players";
            CoinsTable         = "sv_coins";
            DefaultWidth  = 5;
            DefaultHeight = 5;
            MaxHeight     = 15;

            UpgradeCoinsCost  = 500;  // base; actual = base × level (5→6=500, 6→7=1000, ...)
            UpgradeItemId     = 6114;
            UpgradeItemAmount = 1;    // base; actual = base × level (5→6=1, 6→7=2, ...)

            MsgOpened            = "เปิด Backpack ({w}x{h}) | Backpack opened ({w}x{h})";
            MsgAlreadyOpen       = "ปิด Backpack ก่อน | Close your backpack first.";
            MsgUpgradeSuccess    = "อัพเกรดแล้ว! Backpack ตอนนี้ {w}x{h} | Next: {next_item} item + {next_coin} coins";
            MsgMaxSize           = "Backpack ใหญ่สุดแล้ว ({w}x{h}) | Already at max size.";
            MsgInsufficientCoins = "Coins ไม่พอ ต้องการ {need} มีแค่ {have} | Need {need} coins, have {have}";
            MsgInsufficientItems = "ไอเทมไม่พอ ต้องการ {item} x{amount} | Need {item} x{amount}";
            MsgUpgradeOpen       = "ใส่ {item} x{amount} + {coins} coins แล้วปิดกล่อง | Put {item} x{amount} + {coins} coins then close.";
            MsgUpgradeReturn     = "ไอเทมถูกคืน (ไม่ครบ) | Items returned (not enough).";
            MsgAdminSet          = "ตั้ง Backpack {player} เป็น {w}x{h} | Set {player} backpack to {w}x{h}";

            VaultBarricadeId        = 328;   // vanilla Metal Locker
            VaultDeadboxBarricadeId = 6599;
        }
    }
}
