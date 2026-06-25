using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using UnityEngine;

namespace VaultBackpack
{
    /// <summary>/bpset <player> <w> <h> — admin: set exact backpack size.</summary>
    public sealed class CommandBackpackSet : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name        => "backpackset";
        public string Help        => "Set a player's backpack size.";
        public string Syntax      => "<player> <width> <height>";
        public List<string> Aliases     => new List<string> { "bpset" };
        public List<string> Permissions => new List<string> { "vaultbackpack.admin" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            if (command.Length < 3
                || !byte.TryParse(command[1], out byte w)
                || !byte.TryParse(command[2], out byte h)
                || w == 0 || h == 0)
            {
                UnturnedChat.Say(caller, "ใช้: /bpset <player> <width> <height>", Color.yellow);
                return;
            }

            var target = UnturnedPlayer.FromName(command[0]);
            if (target == null)
            {
                UnturnedChat.Say(caller, "ไม่พบผู้เล่น | Player not found: " + command[0], Color.red);
                return;
            }

            var plugin = VaultBackpackPlugin.Instance;
            if (plugin == null) return;

            var callerPlayer = caller as UnturnedPlayer;
            if (callerPlayer != null)
                plugin.AdminSetSize(callerPlayer, target, w, h);
            else
            {
                // console caller
                plugin.Database?.SetSize(target.CSteamID.m_SteamID, w, h);
                Rocket.Core.Logging.Logger.Log("[VaultBackpack] Admin set " + target.CharacterName + " to " + w + "x" + h);
            }
        }
    }
}
