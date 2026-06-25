using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Player;

namespace VaultBackpack
{
    /// <summary>/backpackupgrade — pay coins + items for +1 height row.</summary>
    public sealed class CommandBackpackUpgrade : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name        => "backpackupgrade";
        public string Help        => "Upgrade your backpack size (+1 row).";
        public string Syntax      => "";
        public List<string> Aliases     => new List<string> { "bpu" };
        public List<string> Permissions => new List<string> { "vaultbackpack.use" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            VaultBackpackPlugin.Instance?.TryUpgrade((UnturnedPlayer)caller);
        }
    }
}
