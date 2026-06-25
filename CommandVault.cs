using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Player;

namespace VaultBackpack
{
    public sealed class CommandVault : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name        => "backpack";
        public string Help        => "Open your backpack.";
        public string Syntax      => "";
        public List<string> Aliases     => new List<string> { "bp" };
        public List<string> Permissions => new List<string> { "vaultbackpack.use" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            VaultBackpackPlugin.Instance?.OpenBackpack((UnturnedPlayer)caller);
        }
    }
}
