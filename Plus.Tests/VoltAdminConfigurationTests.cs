using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Incoming.Catalog.Admin;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Packets.Incoming.Housekeeping;
using Xunit;

namespace Plus.Tests;

public class VoltAdminConfigurationTests
{
    [Fact]
    public void ExternalAuthOmitsAdminHandlersButKeepsGameAuthenticationAndCatalog()
    {
        Type[] types = [typeof(HousekeepingGetDashboardEvent), typeof(CatalogAdminPublishEvent), typeof(SSOTicketEvent), typeof(GetCatalogPageEvent)];
        var handlers = types.Select(type => (IPacketEvent)RuntimeHelpers.GetUninitializedObject(type));
        using var manager = new PacketManager(handlers, NullLogger<PacketManager>.Instance, Options.Create(new AuthApiConfiguration { Enabled = false }));

        Assert.False(manager.IsRegistered(Header(typeof(HousekeepingGetDashboardEvent))));
        Assert.False(manager.IsRegistered(Header(typeof(CatalogAdminPublishEvent))));
        Assert.True(manager.IsRegistered(Header(typeof(SSOTicketEvent))));
        Assert.True(manager.IsRegistered(Header(typeof(GetCatalogPageEvent))));
    }

    private static uint Header(Type type) => (uint)typeof(ClientPacketHeader).GetField(type.Name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
}
