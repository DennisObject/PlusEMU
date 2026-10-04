using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Core;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Permissions;

public sealed class ClientAccessLists(IAccessControl permissions, IChatStyleManager styles, IRoomManager rooms) : IStartable, IDisposable
{
    public Task Start()
    {
        permissions.AccessChanged += Send;
        return Task.CompletedTask;
    }

    public void Send(Habbo habbo)
    {
        habbo.Client.Send(new AllowedChatStylesComposer(styles.GetAllowedStyleIds(habbo.Access)));
        habbo.Client.Send(new CreatableRoomModelsComposer(rooms.GetCreatableModels(habbo.Access)));
    }

    public void Dispose() => permissions.AccessChanged -= Send;
}
