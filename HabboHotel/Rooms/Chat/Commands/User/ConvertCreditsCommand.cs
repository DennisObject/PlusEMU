using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class ConvertCreditsCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "convertcredits";

    public string Parameters => "";

    public string Description => "Convert your exchangeable furniture into actual credits.";

    public ConvertCreditsCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var totalValue = 0;
        try
        {
            using var connection = _database.Connection();
            var itemIds = connection.Query<uint>("SELECT id FROM items WHERE user_id=@userId AND room_id=0",
                new { userId = session.GetHabbo().Id }).ToArray();
            if (itemIds.Length == 0)
            {
                session.SendWhisper("You currently have no items in your inventory!");
                return;
            }
            var exchangeItems = itemIds.Select(session.GetHabbo().Inventory.Furniture.GetItem)
                .Where(item => item?.Definition.InteractionType == InteractionType.Exchange)
                .ToArray();
            if (exchangeItems.Length > 0)
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var item in exchangeItems)
                        connection.Execute("DELETE FROM items WHERE id=@id LIMIT 1", new { item.Id }, transaction);
                    transaction.Commit();
                }
                foreach (var item in exchangeItems)
                {
                    session.GetHabbo().Inventory.Furniture.RemoveItem(item.Id);
                    session.Send(new FurniListRemoveComposer(item.Id));
                    var value = item.Definition.BehaviourData;
                    totalValue += value;
                    if (value > 0)
                    {
                        lock (session.GetHabbo().WalletSync)
                        {
                            session.GetHabbo().Credits += value;
                            session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
                        }
                    }
                }
            }
            if (totalValue > 0)
                session.SendNotification($"All credits have successfully been converted!\r\r(Total value: {totalValue} credits!");
            else
                session.SendNotification("It appears you don't have any exchangeable items!");
        }
        catch
        {
            session.SendNotification("Oops, an error occoured whilst converting your credits!");
        }
    }
}
