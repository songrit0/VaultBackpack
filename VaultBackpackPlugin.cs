using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Action = System.Action;
using Logger = Rocket.Core.Logging.Logger;

namespace VaultBackpack
{
    public sealed class VaultSession
    {
        public GameObject Go;
        public InteractableStorage Stor;
        public byte Width;
        public byte Height;
    }

    public sealed class UpgradeSession
    {
        public GameObject Go;
        public InteractableStorage Stor;
        public int ItemCost;
        public long CoinCost;
        public byte CurrentHeight;
        public byte CurrentWidth;
    }

    public sealed class VaultBackpackPlugin : RocketPlugin<VaultBackpackConfig>
    {
        public static VaultBackpackPlugin Instance { get; private set; }
        public VaultDatabase Database { get; private set; }

        public readonly Dictionary<ulong, VaultSession>   Sessions       = new Dictionary<ulong, VaultSession>();
        public readonly Dictionary<ulong, UpgradeSession> UpgradeVaults  = new Dictionary<ulong, UpgradeSession>();
        // instanceId → storage: tracks live vault drop boxes for empty→vanish
        private readonly Dictionary<uint, InteractableStorage> _vaultBoxes = new Dictionary<uint, InteractableStorage>();
        // steam IDs whose vault was already dropped on ban — skip re-save in OnDisconnected
        private readonly HashSet<ulong> _vaultDropped = new HashSet<ulong>();

        private readonly List<VaultBPComponent> _components = new List<VaultBPComponent>();
        private readonly object _lock = new object();
        private readonly Queue<Action> _main = new Queue<Action>();

        private static MethodInfo _resizeMethod;
        private static bool _resizeResolved;

        protected override void Load()
        {
            Instance = this;
            var cfg = Configuration.Instance;
            Database = new VaultDatabase(cfg.ConnectionString, cfg.TableName, cfg.CoinsTable);
            U.Events.OnPlayerConnected    += OnConnected;
            U.Events.OnPlayerDisconnected += OnDisconnected;
            UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDeath;
            Provider.onBanPlayerRequestedV2 += OnBanRequested;
            InvokeRepeating("TickVaultBoxes", 2f, 2f);
            Logger.Log("[VaultBackpack] Loaded.");
        }

        protected override void Unload()
        {
            U.Events.OnPlayerConnected    -= OnConnected;
            U.Events.OnPlayerDisconnected -= OnDisconnected;
            UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDeath;
            Provider.onBanPlayerRequestedV2 -= OnBanRequested;
            CancelInvoke("TickVaultBoxes");
            foreach (var kv in Sessions.ToList())      SaveAndClose(kv.Key, kv.Value);
            foreach (var kv in UpgradeVaults.ToList()) DestroyUpgradeVault(kv.Key, kv.Value, refund: false);
            Sessions.Clear();
            UpgradeVaults.Clear();
            foreach (var c in _components) if (c != null) UnityEngine.Object.Destroy(c);
            _components.Clear();
            lock (_lock) _main.Clear();
            Database = null;
            Instance = null;
            Logger.Log("[VaultBackpack] Unloaded.");
        }

        private void FixedUpdate()
        {
            while (true)
            {
                Action a = null;
                lock (_lock) { if (_main.Count > 0) a = _main.Dequeue(); }
                if (a == null) break;
                try { a(); } catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] main"); }
            }
        }

        // -----------------------------------------------------------------------
        //  Connect / disconnect
        // -----------------------------------------------------------------------
        private void OnConnected(UnturnedPlayer player)
        {
            var c = player.Player.gameObject.AddComponent<VaultBPComponent>();
            c.Init(player.Player);
            _components.Add(c);
        }

        private void OnDisconnected(UnturnedPlayer player)
        {
            ulong steamId = player.CSteamID.m_SteamID;
            bool alreadyDropped = _vaultDropped.Remove(steamId);
            if (!alreadyDropped && Sessions.TryGetValue(steamId, out var session))
                SaveAndClose(steamId, session);
            if (UpgradeVaults.TryGetValue(steamId, out var upg))
                DestroyUpgradeVault(steamId, upg, refund: false);
        }

        // -----------------------------------------------------------------------
        //  Death drop
        // -----------------------------------------------------------------------
        private void OnPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer)
        {
            var cfg = Configuration.Instance;
            if (cfg.VaultDeadboxBarricadeId == 0) return;

            ulong steamId  = player.CSteamID.m_SteamID;
            Vector3 deathPos = player.Position;

            // close open session first so items are flushed to DB
            if (Sessions.TryGetValue(steamId, out var session))
                SaveAndClose(steamId, session);

            var db = Database;
            if (db == null) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var data = db.Load(steamId, cfg.DefaultWidth, cfg.DefaultHeight);
                if (data.Items.Count == 0) return;

                // clear vault in DB — items now live in the drop box
                db.SaveItems(steamId, new System.Collections.Generic.List<SavedItem>());

                Enqueue(() => SpawnVaultDropBox(deathPos, steamId, data));
            });
        }

        // -----------------------------------------------------------------------
        //  Ban drop — vault ร่วงเป็น barricade box ณ จุดที่ player อยู่ตอน ban
        // -----------------------------------------------------------------------
        private void OnBanRequested(CSteamID steamID, CSteamID judgeID, uint repBan, IEnumerable<byte[]> hwids, ref string reason, ref uint duration, ref bool isValid)
        {
            var cfg = Configuration.Instance;
            if (cfg.VaultDeadboxBarricadeId == 0) return;

            ulong sid = steamID.m_SteamID;
            var player = UnturnedPlayer.FromCSteamID(steamID);
            if (player == null) return;

            Vector3 banPos = player.Position;
            _vaultDropped.Add(sid);

            // if BP is open, snapshot items directly from memory (no DB round-trip)
            if (Sessions.TryGetValue(sid, out var session))
            {
                var items = new List<SavedItem>();
                if (session.Stor?.items != null)
                    foreach (var jar in session.Stor.items.items)
                        items.Add(new SavedItem {
                            Id = jar.item.id, X = jar.x, Y = jar.y, Rot = jar.rot,
                            Amount = jar.item.amount, Quality = jar.item.quality, State = jar.item.state,
                        });
                Sessions.Remove(sid);
                if (session.Go != null) UnityEngine.Object.Destroy(session.Go);

                if (items.Count == 0) return;
                var data = new PlayerData { Width = session.Width, Height = session.Height, Items = items };
                var db2 = Database;
                ThreadPool.QueueUserWorkItem(_ => db2?.SaveItems(sid, new List<SavedItem>()));
                SpawnVaultDropBox(banPos, sid, data);
                return;
            }

            // BP is closed — load from DB
            var db = Database;
            if (db == null) return;
            var capturedCfg = cfg;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var data = db.Load(sid, capturedCfg.DefaultWidth, capturedCfg.DefaultHeight);
                if (data.Items.Count == 0) return;
                db.SaveItems(sid, new List<SavedItem>());
                Enqueue(() => SpawnVaultDropBox(banPos, sid, data));
            });
        }

        private void SpawnVaultDropBox(Vector3 deathPos, ulong steamId, PlayerData data)
        {
            var cfg   = Configuration.Instance;
            var asset = Assets.find(EAssetType.ITEM, cfg.VaultDeadboxBarricadeId) as ItemBarricadeAsset;
            if (asset == null)
            {
                Logger.LogError("[VaultBackpack] VaultDeadboxBarricadeId " + cfg.VaultDeadboxBarricadeId + " not found.");
                return;
            }

            // slight X offset so it doesn't perfectly overlap Deadbox at same position
            var origin   = new Vector3(deathPos.x + 0.8f, deathPos.y, deathPos.z);
            var spawnPos = FindGroundPosition(origin, 0.5f);
            var rot      = BarricadeManager.getRotation(asset, 0f, 0f, 0f);

            Transform t = BarricadeManager.dropNonPlantedBarricade(
                new Barricade(asset), spawnPos, rot, 0UL, 0UL);
            if (t == null) return;

            var stor = t.GetComponent<InteractableStorage>()
                    ?? t.GetComponentInParent<InteractableStorage>();
            if (stor == null) { SafeDestroyBarricade(t); return; }

            TryResize(stor.items, data.Width, data.Height);

            foreach (var it in data.Items)
                if (it.X < data.Width && it.Y < data.Height)
                    stor.items.addItem(it.X, it.Y, it.Rot,
                        new Item(it.Id, it.Amount, it.Quality, it.State));

            // track for empty→vanish
            byte bx, by; ushort plant, idx; BarricadeRegion reg; BarricadeDrop drop;
#pragma warning disable CS0618
            if (BarricadeManager.tryGetInfo(t, out bx, out by, out plant, out idx, out reg, out drop))
                _vaultBoxes[drop.instanceID] = stor;
#pragma warning restore CS0618
        }

        private void TickVaultBoxes()
        {
            if (_vaultBoxes.Count == 0) return;
            var empty = new List<uint>();
            foreach (var kv in _vaultBoxes)
            {
                try
                {
                    if (kv.Value == null || kv.Value.items == null || kv.Value.items.getItemCount() == 0)
                        empty.Add(kv.Key);
                }
                catch { empty.Add(kv.Key); }
            }
            foreach (var id in empty)
            {
                _vaultBoxes.Remove(id);
                DestroyBoxByInstance(id);
            }
        }

        private static void DestroyBoxByInstance(uint instanceId)
        {
            try
            {
                foreach (var region in BarricadeManager.regions)
                {
                    if (region?.drops == null) continue;
                    foreach (var drop in region.drops)
                    {
                        if (drop == null || drop.instanceID != instanceId) continue;
                        SafeDestroyBarricade(drop.model);
                        return;
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, "[VaultBackpack] DestroyBoxByInstance"); }
        }

        private static Vector3 FindGroundPosition(Vector3 origin, float heightOffset)
        {
            try
            {
                RaycastHit hit;
                Vector3 from = new Vector3(origin.x, origin.y + 10f, origin.z);
                if (Physics.Raycast(from, Vector3.down, out hit, 30f, RayMasks.BLOCK_COLLISION))
                    return new Vector3(origin.x, hit.point.y + heightOffset, origin.z);
            }
            catch { }
            return new Vector3(origin.x, origin.y + heightOffset, origin.z);
        }

        private static void SafeDestroyBarricade(Transform t)
        {
            try
            {
#pragma warning disable CS0618
                if (BarricadeManager.tryGetInfo(t, out byte bx, out byte by, out ushort plant,
                    out ushort _, out BarricadeRegion _, out BarricadeDrop drop))
                    BarricadeManager.destroyBarricade(drop, bx, by, plant);
#pragma warning restore CS0618
            }
            catch { }
        }

        // -----------------------------------------------------------------------
        //  Open main backpack
        // -----------------------------------------------------------------------
        public void OpenBackpack(UnturnedPlayer player)
        {
            ulong steamId = player.CSteamID.m_SteamID;
            if (Sessions.ContainsKey(steamId))
            {
                UnturnedChat.Say(player, Configuration.Instance.MsgAlreadyOpen, Color.yellow);
                return;
            }
            var cfg = Configuration.Instance;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var data = Database?.Load(steamId, cfg.DefaultWidth, cfg.DefaultHeight)
                           ?? new PlayerData { Width = cfg.DefaultWidth, Height = cfg.DefaultHeight, Items = new List<SavedItem>() };
                Enqueue(() => DoOpen(player, data));
            });
        }

        private void DoOpen(UnturnedPlayer player, PlayerData data)
        {
            ulong owner = player.CSteamID.m_SteamID;
            var cfg = Configuration.Instance;

            var go   = new GameObject("VaultBP_" + owner);
            var stor = go.AddComponent<InteractableStorage>();
            if (stor.items == null) InjectItems(stor, new Items(7));
            TryResize(stor.items, data.Width, data.Height);
            foreach (var it in data.Items)
                if (it.X < data.Width && it.Y < data.Height)
                    stor.items.addItem(it.X, it.Y, it.Rot,
                        new Item(it.Id, it.Amount, it.Quality, it.State));

            Sessions[owner] = new VaultSession { Go = go, Stor = stor, Width = data.Width, Height = data.Height };

            try { player.Player.inventory.openStorage(stor); }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[VaultBackpack] openStorage");
                SaveAndClose(owner, Sessions[owner]);
                return;
            }

            UnturnedChat.Say(player,
                cfg.MsgOpened.Replace("{w}", data.Width.ToString()).Replace("{h}", data.Height.ToString()),
                Color.green);
        }

        // -----------------------------------------------------------------------
        //  Upgrade vault — /bpu opens a small box; close triggers upgrade check
        // -----------------------------------------------------------------------
        public void TryUpgrade(UnturnedPlayer player)
        {
            ulong steamId = player.CSteamID.m_SteamID;
            var cfg = Configuration.Instance;

            if (UpgradeVaults.ContainsKey(steamId))
            {
                UnturnedChat.Say(player, cfg.MsgAlreadyOpen, Color.yellow);
                return;
            }

            var db = Database;
            if (db == null) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var data     = db.Load(steamId, cfg.DefaultWidth, cfg.DefaultHeight);
                int level    = data.Height - cfg.DefaultHeight + 1;
                int itemCost = cfg.UpgradeItemAmount * level;
                long coinCost = cfg.UpgradeCoinsCost * level;

                Enqueue(() =>
                {
                    if (data.Height >= cfg.MaxHeight && cfg.MaxHeight > 0)
                    {
                        UnturnedChat.Say(player,
                            cfg.MsgMaxSize.Replace("{w}", data.Width.ToString()).Replace("{h}", data.Height.ToString()),
                            Color.yellow);
                        return;
                    }

                    var go   = new GameObject("VaultUpg_" + steamId);
                    var stor = go.AddComponent<InteractableStorage>();
                    if (stor.items == null) InjectItems(stor, new Items(7));
                    TryResize(stor.items, 8, 25);

                    UpgradeVaults[steamId] = new UpgradeSession
                    {
                        Go            = go,
                        Stor          = stor,
                        ItemCost      = itemCost,
                        CoinCost      = coinCost,
                        CurrentHeight = data.Height,
                        CurrentWidth  = data.Width,
                    };

                    try { player.Player.inventory.openStorage(stor); }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "[VaultBackpack] openStorage upgrade");
                        DestroyUpgradeVault(steamId, UpgradeVaults[steamId], refund: false);
                        return;
                    }

                    string itemName = (Assets.find(EAssetType.ITEM, cfg.UpgradeItemId) as ItemAsset)?.itemName
                                      ?? cfg.UpgradeItemId.ToString();
                    UnturnedChat.Say(player,
                        cfg.MsgUpgradeOpen
                            .Replace("{item}", itemName)
                            .Replace("{amount}", itemCost.ToString())
                            .Replace("{coins}", coinCost.ToString()),
                        Color.cyan);
                });
            });
        }

        // called by VaultBPComponent when STORAGE page resizes to 0x0
        public void OnUpgradeVaultClosed(Player player, ulong steamId)
        {
            if (!UpgradeVaults.TryGetValue(steamId, out var upg)) return;

            var cfg = Configuration.Instance;
            var db  = Database;
            if (db == null) return;

            // snapshot items before destroying the GO
            int have = CountStorageItem(upg.Stor, cfg.UpgradeItemId);

            if (cfg.UpgradeItemId > 0 && have < upg.ItemCost)
            {
                // not enough — return items to player inventory, destroy vault
                ReturnStorageItems(player, upg.Stor);
                DestroyUpgradeVault(steamId, upg, refund: false);
                string itemName = (Assets.find(EAssetType.ITEM, cfg.UpgradeItemId) as ItemAsset)?.itemName
                                  ?? cfg.UpgradeItemId.ToString();
                UnturnedPlayer up = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                if (up != null)
                    UnturnedChat.Say(up,
                        cfg.MsgInsufficientItems
                            .Replace("{item}", itemName)
                            .Replace("{amount}", upg.ItemCost.ToString()),
                        Color.red);
                return;
            }

            // consume exact required items, return the rest
            RemoveStorageItem(upg.Stor, cfg.UpgradeItemId, upg.ItemCost);
            ReturnStorageItems(player, upg.Stor); // any leftover items
            DestroyUpgradeVault(steamId, upg, refund: false);

            int capturedItemCost  = upg.ItemCost;
            long capturedCoinCost = upg.CoinCost;
            byte capturedW        = upg.CurrentWidth;
            byte capturedH        = upg.CurrentHeight;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (capturedCoinCost > 0 && !db.SpendCoins(steamId, capturedCoinCost))
                {
                    long bal = db.GetBalance(steamId);
                    Enqueue(() =>
                    {
                        // refund consumed items
                        UnturnedPlayer up2 = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                        if (up2 != null)
                        {
                            GivePlayerItem(up2.Player, cfg.UpgradeItemId, capturedItemCost);
                            UnturnedChat.Say(up2,
                                cfg.MsgInsufficientCoins
                                    .Replace("{need}", capturedCoinCost.ToString())
                                    .Replace("{have}", bal.ToString()),
                                Color.red);
                        }
                    });
                    return;
                }

                byte newH = db.IncrementHeight(steamId, cfg.MaxHeight);
                if (newH == 0)
                {
                    if (capturedCoinCost > 0) db.CreditCoins(steamId, capturedCoinCost);
                    Enqueue(() =>
                    {
                        UnturnedPlayer up2 = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                        if (up2 != null)
                        {
                            GivePlayerItem(up2.Player, cfg.UpgradeItemId, capturedItemCost);
                            UnturnedChat.Say(up2,
                                cfg.MsgMaxSize.Replace("{w}", capturedW.ToString()).Replace("{h}", capturedH.ToString()),
                                Color.yellow);
                        }
                    });
                    return;
                }

                int nextLevel = newH - cfg.DefaultHeight + 1;
                int nextItem  = cfg.UpgradeItemAmount * nextLevel;
                long nextCoin = cfg.UpgradeCoinsCost  * nextLevel;

                Enqueue(() =>
                {
                    // grow the live main BP session if open
                    if (Sessions.TryGetValue(steamId, out var bpSession))
                    {
                        bpSession.Height = newH;
                        TryResize(bpSession.Stor.items, capturedW, newH);
                    }
                    UnturnedPlayer up2 = UnturnedPlayer.FromCSteamID(new CSteamID(steamId));
                    if (up2 != null)
                        UnturnedChat.Say(up2,
                            cfg.MsgUpgradeSuccess
                                .Replace("{w}", capturedW.ToString())
                                .Replace("{h}", newH.ToString())
                                .Replace("{next_item}", nextItem.ToString())
                                .Replace("{next_coin}", nextCoin.ToString()),
                            Color.green);
                });
            });
        }

        // -----------------------------------------------------------------------
        //  Admin: set exact size
        // -----------------------------------------------------------------------
        public void AdminSetSize(UnturnedPlayer caller, UnturnedPlayer target, byte w, byte h)
        {
            ulong targetId = target.CSteamID.m_SteamID;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Database?.SetSize(targetId, w, h);
                Enqueue(() =>
                {
                    string msg = Configuration.Instance.MsgAdminSet
                        .Replace("{player}", target.CharacterName)
                        .Replace("{w}", w.ToString())
                        .Replace("{h}", h.ToString());
                    UnturnedChat.Say(caller, msg, Color.green);
                });
            });
        }

        // -----------------------------------------------------------------------
        //  Save & close main BP
        // -----------------------------------------------------------------------
        public void SaveAndClose(ulong steamId, VaultSession session)
        {
            Sessions.Remove(steamId);
            var items = new List<SavedItem>();
            if (session.Stor?.items != null)
                foreach (var jar in session.Stor.items.items)
                    items.Add(new SavedItem
                    {
                        Id = jar.item.id, X = jar.x, Y = jar.y, Rot = jar.rot,
                        Amount = jar.item.amount, Quality = jar.item.quality, State = jar.item.state,
                    });

            if (session.Go != null) UnityEngine.Object.Destroy(session.Go);
            var captured = items;
            ThreadPool.QueueUserWorkItem(_ => Database?.SaveItems(steamId, captured));
        }

        private void DestroyUpgradeVault(ulong steamId, UpgradeSession upg, bool refund)
        {
            UpgradeVaults.Remove(steamId);
            if (upg.Go != null) UnityEngine.Object.Destroy(upg.Go);
        }

        // -----------------------------------------------------------------------
        //  Helpers
        // -----------------------------------------------------------------------
        private static void InjectItems(InteractableStorage stor, Items value)
        {
            var prop = typeof(InteractableStorage).GetProperty("items",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.CanWrite == true) { prop.SetValue(stor, value); return; }

            var field = System.Array.Find(
                typeof(InteractableStorage).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                f => f.FieldType == typeof(Items));
            field?.SetValue(stor, value);
        }

        private static void TryResize(Items grid, byte w, byte h)
        {
            try
            {
                if (!_resizeResolved)
                {
                    _resizeMethod = typeof(Items).GetMethod("resize",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(byte), typeof(byte) }, null);
                    _resizeResolved = true;
                }
                _resizeMethod?.Invoke(grid, new object[] { w, h });
            }
            catch { }
        }

        private static int CountStorageItem(InteractableStorage stor, ushort itemId)
        {
            int total = 0;
            if (stor?.items == null) return 0;
            foreach (var jar in stor.items.items)
                if (jar.item.id == itemId) total += jar.item.amount;
            return total;
        }

        private static void RemoveStorageItem(InteractableStorage stor, ushort itemId, int amount)
        {
            int remaining = amount;
            for (int i = stor.items.items.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var jar = stor.items.items[i];
                if (jar.item.id != itemId) continue;
                remaining -= jar.item.amount;
                stor.items.removeItem((byte)i);
            }
        }

        private static void ReturnStorageItems(Player player, InteractableStorage stor)
        {
            if (stor?.items == null) return;
            while (stor.items.getItemCount() > 0)
            {
                var jar = stor.items.items[0];
                player.inventory.forceAddItem(jar.item, true);
                stor.items.removeItem(0);
            }
        }

        private static void GivePlayerItem(Player player, ushort itemId, int amount)
        {
            while (amount > 0)
            {
                byte chunk = (byte)Math.Min(amount, 255);
                player.inventory.forceAddItem(new Item(itemId, chunk, 100), true);
                amount -= chunk;
            }
        }

        public void Enqueue(Action a) { if (a != null) lock (_lock) _main.Enqueue(a); }
    }
}
