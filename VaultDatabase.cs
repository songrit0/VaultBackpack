using System;
using System.Collections.Generic;
using System.Text;
using MySql.Data.MySqlClient;
using Logger = Rocket.Core.Logging.Logger;

namespace VaultBackpack
{
    public sealed class SavedItem
    {
        public ushort Id;
        public byte X, Y, Rot, Amount, Quality;
        public byte[] State;
    }

    public sealed class PlayerData
    {
        public byte Width;
        public byte Height;
        public List<SavedItem> Items;
    }

    public sealed class VaultDatabase
    {
        private readonly string _conn;
        private readonly string _tbl;
        private readonly string _coins;

        public VaultDatabase(string conn, string tbl, string coins)
        {
            _conn  = conn;
            _tbl   = tbl;
            _coins = coins;
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    // create table with width/height columns
                    using (var cmd = new MySqlCommand(
                        "CREATE TABLE IF NOT EXISTS `" + _tbl + "` ("
                        + "`steam_id`   BIGINT UNSIGNED NOT NULL PRIMARY KEY,"
                        + "`width`      TINYINT UNSIGNED NOT NULL DEFAULT 5,"
                        + "`height`     TINYINT UNSIGNED NOT NULL DEFAULT 5,"
                        + "`items_data` LONGTEXT NULL,"
                        + "`updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP "
                        +              "ON UPDATE CURRENT_TIMESTAMP"
                        + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", c))
                        cmd.ExecuteNonQuery();

                    // migrate existing rows that may lack width/height columns
                    try
                    {
                        using (var cmd = new MySqlCommand(
                            "ALTER TABLE `" + _tbl + "` "
                            + "ADD COLUMN IF NOT EXISTS `width`  TINYINT UNSIGNED NOT NULL DEFAULT 5, "
                            + "ADD COLUMN IF NOT EXISTS `height` TINYINT UNSIGNED NOT NULL DEFAULT 5;", c))
                            cmd.ExecuteNonQuery();
                    }
                    catch { /* older MySQL without IF NOT EXISTS — ignore, columns already exist */ }
                }
                Logger.Log("[VaultBackpack] MySQL table ready.");
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] EnsureSchema"); }
        }

        public PlayerData Load(ulong steamId, byte defaultW, byte defaultH)
        {
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    using (var cmd = new MySqlCommand(
                        "INSERT IGNORE INTO `" + _tbl + "` (steam_id, width, height) VALUES (@s,@w,@h);", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        cmd.Parameters.AddWithValue("@w", defaultW);
                        cmd.Parameters.AddWithValue("@h", defaultH);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = new MySqlCommand(
                        "SELECT width, height, items_data FROM `" + _tbl + "` WHERE steam_id=@s;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) return new PlayerData { Width = defaultW, Height = defaultH, Items = new List<SavedItem>() };
                            return new PlayerData
                            {
                                Width  = Convert.ToByte(r["width"]),
                                Height = Convert.ToByte(r["height"]),
                                Items  = Deserialize(r["items_data"] as string),
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[VaultBackpack] Load");
                return new PlayerData { Width = defaultW, Height = defaultH, Items = new List<SavedItem>() };
            }
        }

        public void SaveItems(ulong steamId, List<SavedItem> items)
        {
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    using (var cmd = new MySqlCommand(
                        "INSERT INTO `" + _tbl + "` (steam_id, items_data) VALUES (@s,@d) "
                        + "ON DUPLICATE KEY UPDATE items_data=@d;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        cmd.Parameters.AddWithValue("@d", Serialize(items));
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] SaveItems"); }
        }

        /// <summary>Increment height by 1. Returns new height, or 0 if already at max or error.</summary>
        public byte IncrementHeight(ulong steamId, byte maxHeight)
        {
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    // only increment if below max (or no max)
                    string whereMax = maxHeight > 0 ? " AND height < @max" : "";
                    using (var cmd = new MySqlCommand(
                        "UPDATE `" + _tbl + "` SET height=height+1 WHERE steam_id=@s" + whereMax + ";", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        if (maxHeight > 0) cmd.Parameters.AddWithValue("@max", maxHeight);
                        if (cmd.ExecuteNonQuery() == 0) return 0; // at max or no row
                    }
                    using (var cmd = new MySqlCommand(
                        "SELECT height FROM `" + _tbl + "` WHERE steam_id=@s;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        return Convert.ToByte(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] IncrementHeight"); return 0; }
        }

        public void SetSize(ulong steamId, byte width, byte height)
        {
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    using (var cmd = new MySqlCommand(
                        "INSERT INTO `" + _tbl + "` (steam_id, width, height) VALUES (@s,@w,@h) "
                        + "ON DUPLICATE KEY UPDATE width=@w, height=@h;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        cmd.Parameters.AddWithValue("@w", width);
                        cmd.Parameters.AddWithValue("@h", height);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] SetSize"); }
        }

        public bool SpendCoins(ulong steamId, long amount)
        {
            if (amount <= 0) return true;
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    using (var cmd = new MySqlCommand(
                        "UPDATE `" + _coins + "` SET balance=balance-@a WHERE steam_id=@s AND balance>=@a;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        cmd.Parameters.AddWithValue("@a", amount);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] SpendCoins"); return false; }
        }

        public void CreditCoins(ulong steamId, long amount)
        {
            if (amount <= 0) return;
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    using (var cmd = new MySqlCommand(
                        "UPDATE `" + _coins + "` SET balance=balance+@a WHERE steam_id=@s;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        cmd.Parameters.AddWithValue("@a", amount);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] CreditCoins"); }
        }

        public long GetBalance(ulong steamId)
        {
            try
            {
                using (var c = new MySqlConnection(_conn))
                {
                    c.Open();
                    using (var cmd = new MySqlCommand(
                        "SELECT balance FROM `" + _coins + "` WHERE steam_id=@s;", c))
                    {
                        cmd.Parameters.AddWithValue("@s", steamId);
                        object o = cmd.ExecuteScalar();
                        return o == null || o == DBNull.Value ? 0L : Convert.ToInt64(o);
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] GetBalance"); return 0; }
        }

        // "id:x:y:rot:amount:quality:stateB64" per item, "|" separated
        private static string Serialize(List<SavedItem> items)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var it in items)
            {
                if (sb.Length > 0) sb.Append('|');
                string s64 = it.State != null && it.State.Length > 0 ? Convert.ToBase64String(it.State) : "";
                sb.Append(it.Id).Append(':').Append(it.X).Append(':').Append(it.Y).Append(':')
                  .Append(it.Rot).Append(':').Append(it.Amount).Append(':').Append(it.Quality).Append(':').Append(s64);
            }
            return sb.ToString();
        }

        public static List<SavedItem> Deserialize(string data)
        {
            var list = new List<SavedItem>();
            if (string.IsNullOrEmpty(data)) return list;
            foreach (var part in data.Split('|'))
            {
                var f = part.Split(':');
                if (f.Length < 7) continue;
                try
                {
                    list.Add(new SavedItem
                    {
                        Id = ushort.Parse(f[0]), X = byte.Parse(f[1]), Y = byte.Parse(f[2]),
                        Rot = byte.Parse(f[3]), Amount = byte.Parse(f[4]), Quality = byte.Parse(f[5]),
                        State = string.IsNullOrEmpty(f[6]) ? new byte[0] : Convert.FromBase64String(f[6]),
                    });
                }
                catch { }
            }
            return list;
        }
    }
}
