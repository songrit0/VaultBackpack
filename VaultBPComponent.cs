using SDG.Unturned;
using UnityEngine;

namespace VaultBackpack
{
    /// <summary>Per-player hook: detects main backpack and upgrade vault close via onInventoryResized.</summary>
    public sealed class VaultBPComponent : MonoBehaviour
    {
        public Player Player;
        private bool _subscribed;

        public void Init(Player player)
        {
            Player = player;
            if (Player?.inventory != null && !_subscribed)
            {
                Player.inventory.onInventoryResized += OnInventoryResized;
                _subscribed = true;
            }
        }

        private void OnInventoryResized(byte page, byte width, byte height)
        {
            if (page != PlayerInventory.STORAGE || width != 0 || height != 0) return;
            var plugin = VaultBackpackPlugin.Instance;
            if (plugin == null || Player == null) return;
            ulong sid = Player.channel?.owner?.playerID.steamID.m_SteamID ?? 0;
            if (sid == 0) return;

            if (plugin.Sessions.TryGetValue(sid, out var session))
                plugin.SaveAndClose(sid, session);

            plugin.OnUpgradeVaultClosed(Player, sid);
        }

        private void OnDestroy()
        {
            if (Player?.inventory != null && _subscribed)
                Player.inventory.onInventoryResized -= OnInventoryResized;
            _subscribed = false;
        }
    }
}
