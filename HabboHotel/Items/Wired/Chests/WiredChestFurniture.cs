namespace Plus.HabboHotel.Items.Wired.Chests;

public static class WiredChestFurniture
{
    public static bool IsChest(ItemDefinition definition) => definition.InteractionType is InteractionType.WiredChestFurni or InteractionType.WiredChestCoins;
    public static bool IsContract(ItemDefinition definition) => definition.InteractionType is InteractionType.WiredContractPayment or InteractionType.WiredContractTrade or InteractionType.WiredContractReward;
    public static WiredChestKind Kind(ItemDefinition definition) => definition.InteractionType == InteractionType.WiredChestCoins ? WiredChestKind.Coins : WiredChestKind.Furni;
    public static WiredContractKind ContractKind(ItemDefinition definition) => definition.InteractionType switch
    {
        InteractionType.WiredContractPayment => WiredContractKind.Payment,
        InteractionType.WiredContractReward => WiredContractKind.Reward,
        _ => WiredContractKind.Trade
    };
}
