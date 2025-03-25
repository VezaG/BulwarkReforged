using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;


namespace BulwarkReforged
{
    public class BulwarkReforgedModSystem : ModSystem {

        public static float ClaimDurationPerSatiety     { get; private set; }
        public static int   UndergroundClaimLimit       { get; private set; }
        public static bool  AllStoneBlockRequirePickaxe { get; private set; }

        public override bool ShouldLoad(EnumAppSide forSide) => true;
        public override void Start(ICoreAPI api) {

            base.Start(api);
            api.RegisterBlockClass("BlockBarricade", typeof(BlockBarricade));
            api.RegisterBlockBehaviorClass("Flag", typeof(BlockBehaviorFlag));
            api.RegisterBlockBehaviorClass("Logistic", typeof(BlockBehaviorLogistic));
            api.RegisterBlockEntityBehaviorClass("FlagEntity", typeof(BlockEntityBehaviorFlag));
            api.RegisterBlockEntityBehaviorClass("LogisticEntity", typeof(BlockEntityBehaviorLogistic));

            JsonObject modConfig = api.LoadModConfig("BulwarkReforgedModConfig.json");
            BulwarkReforgedModSystem.ClaimDurationPerSatiety     = modConfig?["claimDurationPerSatiety"]?.AsFloat(0.0025f) ?? 0.0025f;
            BulwarkReforgedModSystem.UndergroundClaimLimit       = modConfig?["undergroundClaimLimit"]?.AsInt(8)           ?? 8;
            BulwarkReforgedModSystem.AllStoneBlockRequirePickaxe = modConfig?["allStoneBlockRequirePickaxe"]?.AsBool(true) ?? true;

        } // void ..
        

        public override void AssetsFinalize(ICoreAPI api) {
            base.AssetsFinalize(api);
            foreach (Block block in api.World.Blocks) {
                if (BulwarkReforgedModSystem.AllStoneBlockRequirePickaxe
                    && block.BlockMaterial      == EnumBlockMaterial.Stone
                    && block.Replaceable        <= 200
                    && block.CollisionBoxes     != null
                    && block.RequiredMiningTier <  2
                ) block.RequiredMiningTier = 2;
                
                if (block is BlockDoor || block.HasBehavior<BlockBehaviorDoor>()) {
                    if (block.BlockMaterial == EnumBlockMaterial.Metal && block.RequiredMiningTier < 3)
                        block.RequiredMiningTier = 3;
                    else if (block.BlockMaterial == EnumBlockMaterial.Wood && block.RequiredMiningTier < 2)
                        block.RequiredMiningTier = block.Code.EndVariant() == "crude" ? 1 : 2;
                } // if ..
            } // foreach ..
        } // void ..
    } // class ..
} // namespace ..
