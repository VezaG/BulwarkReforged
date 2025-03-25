using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;


namespace BulwarkReforged
{
    public class FortificationModSystem : ModSystem {

        //=======================
        // D E F I N I T I O N S
        //=======================
            
            protected HashSet<Stronghold> strongholds = new();
            protected ICoreAPI api;

            public delegate void NewStrongholdDelegate(Stronghold stronghold);
            public event NewStrongholdDelegate StrongholdAdded;


        //===============================
        // I N I T I A L I Z A T I O N S
        //===============================

            public override void Start(ICoreAPI api) {
                base.Start(api);
                this.api = api;
            }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            api.Event.CanPlaceOrBreakBlock += this.CanPlaceOrBreakBlockHandler;
            api.Event.PlayerDeath += this.PlayerDeathEvent;

            api.ChatCommands
                .Create("stronghold")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("name")
                    .WithDescription("Name the claimed area you are in")
                    .WithArgs(api.ChatCommands.Parsers.Word("name"))
                    .HandleWith(HandleNameCommand)
                .EndSubCommand()
                .BeginSubCommand("league")
                    .WithDescription("Affiliate the claimed area you are in with a group")
                    .WithArgs(api.ChatCommands.Parsers.Word("group name"))
                    .HandleWith(HandleLeagueCommand)
                .EndSubCommand()
                .BeginSubCommand("stopleague")
                    .WithDescription("Stops the affiliation with a group")
                    .WithArgs(api.ChatCommands.Parsers.Word("group name"))
                    .HandleWith(HandleStopLeagueCommand);
        }
        private TextCommandResult ProcessStronghold(TextCommandCallingArgs args, Func<Stronghold, TextCommandResult> process)
        {
            var stronghold = GetCallerStronghold(args.Caller.Player);
            if (stronghold == null)
            {
                return TextCommandResult.Success(Lang.Get("You're not in a stronghold you claimed"));
            }
            var result = process(stronghold);
            if (result.Status == EnumCommandStatus.Success)
            {
                this.api.World.BlockAccessor.GetBlockEntity(stronghold.Center).MarkDirty();
            }
            return result;
        }

        private TextCommandResult HandleNameCommand(TextCommandCallingArgs args)
        {
            return ProcessStronghold(args, stronghold =>
            {
                stronghold.Name = args[0].ToString();
                return TextCommandResult.Success();
            });
        }

        private TextCommandResult HandleLeagueCommand(TextCommandCallingArgs args)
        {
            return ProcessStronghold(args, stronghold =>
            {
                var groupName = args[0].ToString();
                var playerGroup = ((ICoreServerAPI)this.api).Groups.GetPlayerGroupByName(groupName);
                if (playerGroup == null)
                {
                    return TextCommandResult.Success(Lang.Get("No such group found"));
                }

                stronghold.ClaimGroup(playerGroup);
                return TextCommandResult.Success();
            });
        }

        private TextCommandResult HandleStopLeagueCommand(TextCommandCallingArgs args)
        {
            return ProcessStronghold(args, stronghold =>
            {
                var groupName = args[0].ToString();
                var playerGroup = ((ICoreServerAPI)this.api).Groups.GetPlayerGroupByName(groupName);
                if (playerGroup == null)
                {
                    return TextCommandResult.Success(Lang.Get("No such group found"));
                }

                stronghold.UnclaimGroup();
                return TextCommandResult.Success();
            });
        }

        private Stronghold GetCallerStronghold(IPlayer player)
        {
            return this.strongholds?.FirstOrDefault(stronghold =>
                stronghold.PlayerUID == player.PlayerUID &&
                stronghold.Area.Contains(player.Entity.ServerPos.AsBlockPos));
        }
        //===============================
        // I M P L E M E N T A T I O N S
        //===============================

        public bool CanPlaceOrBreakBlockHandler(IServerPlayer byPlayer, BlockSelection blockSel, out string claimant)
        {
            claimant = null;
            LogCallStart(byPlayer, blockSel);

            if (!TryGetTargetBlock(byPlayer, blockSel, out Block block))
                return false;

            if (IsSiegeEquipmentBlock(block) || HasStrongholdPrivilege(byPlayer, blockSel, out claimant))
                return true;

            if (IsUsingSiegeEquipment(byPlayer))
                return true;

            DenyAction(byPlayer);
            return false;
        }

        private void LogCallStart(IServerPlayer player, BlockSelection selection)
        {
            api.Logger.Debug($"Block modification attempt by {player?.PlayerName} at {selection?.Position}");
        }

        private bool TryGetTargetBlock(IServerPlayer player, BlockSelection selection, out Block block)
        {
            block = player?.Entity?.World?.BlockAccessor?.GetBlock(selection?.Position);

            if (block != null)
                return true;

            api.Logger.Error("Invalid block access parameters");
            return false;
        }

        private bool IsSiegeEquipmentBlock(Block block)
        {
            return block.Attributes?["siegeEquipment"]?.AsBool() == true;
        }

        private bool HasStrongholdPrivilege(IServerPlayer player, BlockSelection selection, out string claimant)
        {
            claimant = null;

            if (!HasPrivilege(player, selection, out Stronghold stronghold))
                return false;

            claimant = stronghold?.PlayerName;
            api.Logger.Debug($"Privilege granted in {claimant}'s stronghold");
            return true;
        }

        private bool IsUsingSiegeEquipment(IServerPlayer player)
        {
            var item = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible;
            bool isSiege = item?.Attributes?["siegeEquipment"]?.AsBool() == true;

            if (isSiege)
                api.Logger.Debug($"Siege equipment detected: {item.Code}");

            return isSiege;
        }

        private void DenyAction(IServerPlayer player)
        {
            api.Logger.Debug($"Privilege denied for {player.PlayerName}");
            player.SendIngameError("stronghold-nobuildprivilege");
        }

        private void PlayerDeathEvent(
                IServerPlayer forPlayer,
                DamageSource damageSource
            ) {
                if (this.strongholds.FirstOrDefault(
                        area => area.PlayerUID == forPlayer.PlayerUID
                        || (forPlayer.Groups?.Any(group => group.GroupUid == area.GroupUID) ?? false), null
                    ) is Stronghold stronghold
                ) {

                    Entity byEntity = damageSource.CauseEntity ?? damageSource.SourceEntity;

                    if (byEntity is EntityPlayer playerCause
                        && stronghold.Area.Contains(byEntity.ServerPos.AsBlockPos)
                        && !(playerCause.Player.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false
                            || playerCause.PlayerUID == stronghold.PlayerUID)
                    ) stronghold.IncreaseSiegeIntensity(1f, byEntity);

                    else if (byEntity.WatchedAttributes.GetString("guardedPlayerUid") is string playerUid
                        && this.api.World.PlayerByUid(playerUid) is IPlayer byPlayer
                        && stronghold.Area.Contains(byEntity.ServerPos.AsBlockPos)
                        && !(byPlayer.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false
                            || byPlayer.PlayerUID == stronghold.PlayerUID)
                        ) stronghold.IncreaseSiegeIntensity(1f, damageSource.CauseEntity);
                }
            }


            public bool TryRegisterStronghold(Stronghold stronghold) {

                stronghold.Api = this.api;
                if (this.strongholds.Contains(stronghold))                              return true;
                else if (this.strongholds.Any(x => x.Area.Intersects(stronghold.Area))) return false;
                else this.strongholds.Add(stronghold);

                stronghold.UpdateRef = stronghold.Api
                    .Event
                    .RegisterGameTickListener(stronghold.Update, 2000, 1000);

                this.StrongholdAdded?.Invoke(stronghold);
                return true;
            }


            public void RemoveStronghold(Stronghold stronghold) {
                if (stronghold is not null) {
                    if (stronghold.UpdateRef.HasValue) stronghold.Api.Event.UnregisterGameTickListener(stronghold.UpdateRef.Value);
                    this.strongholds.Remove(stronghold);
                }
            }


            public bool TryGetStronghold(BlockPos pos, out Stronghold value) {
                if (this.strongholds?.FirstOrDefault(stronghold => stronghold.Area.Contains(pos), null) is Stronghold area) {
                    value = area;
                    return true;
                } else  {
                    value = null;
                    return false;
                }
            }


        public bool HasPrivilege(IPlayer byPlayer, BlockSelection blockSel, out Stronghold area)
        {
            area = null;

            if (this.strongholds == null || !this.strongholds.Any())
                return true;

            foreach (var stronghold in this.strongholds)
            {
                if (!stronghold.Area.Contains(blockSel.Position))
                    continue;

                area = stronghold;

                return CheckStrongholdAccess(stronghold, byPlayer);
            }

            return true;
        }

        private bool CheckStrongholdAccess(Stronghold stronghold, IPlayer player)
        {
            if (stronghold.PlayerUID == null)
                return true;

            if (stronghold.PlayerUID == player.PlayerUID)
                return true;

            return player.Groups?.Any(g => g.GroupUid == stronghold.GroupUID) ?? false;
        }
    }
}
