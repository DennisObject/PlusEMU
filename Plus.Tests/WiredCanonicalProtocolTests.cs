using System.Buffers.Binary;
using System.Text;
using Dapper;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public sealed class WiredCanonicalProtocolTests
{
    [Theory]
    [InlineData(WiredBoxCategory.Trigger)]
    [InlineData(WiredBoxCategory.Action)]
    [InlineData(WiredBoxCategory.Condition)]
    [InlineData(WiredBoxCategory.Selector)]
    [InlineData(WiredBoxCategory.Addon)]
    [InlineData(WiredBoxCategory.Variable)]
    public async Task ActualNativeHandlersRetainEveryCountedTail(WiredBoxCategory category)
    {
        var service = new Saves();
        SaveWiredConfigEvent handler = category switch
        {
            WiredBoxCategory.Trigger => new SaveWiredTriggerConfigEvent(service),
            WiredBoxCategory.Action => new SaveWiredEffectConfigEvent(service),
            WiredBoxCategory.Condition => new SaveWiredConditionConfigEvent(service),
            WiredBoxCategory.Selector => new SaveWiredSelectorConfigEvent(service),
            WiredBoxCategory.Addon => new SaveWiredAddonConfigEvent(service),
            _ => new SaveWiredVariableConfigEvent(service)
        };
        await handler.Parse(null!, Body(7, category));
        var request = Assert.Single(service.Requests);
        Assert.Equal(category, request.Envelope);
        Assert.Equal(new[] { 100, 101 }, request.Native!.FurniSourceTypes);
        Assert.Equal(new[] { 201 }, request.Native.UserSourceTypes);
        Assert.Equal(new[] { 31, 32 }, request.Configuration.IntParams);
        Assert.Equal(new uint[] { 8, 9 }, request.Configuration.SelectedItems);
        Assert.Equal(new uint[] { 10 }, request.Configuration.SecondarySelectedItems);
        Assert.Equal(new[] { "custom:17", "internal:@id" }, request.Configuration.VariableIds);
    }

    [Theory]
    [InlineData("SaveWiredSelectorConfigEvent", 268)]
    [InlineData("SaveWiredAddonConfigEvent", 1692)]
    [InlineData("SaveWiredVariableConfigEvent", 2836)]
    public void NativeExtraCategoriesHaveDirectUniqueCoreHeaders(string name, int expected)
    {
        var headers = new Plus.Communication.Revisions.RevisionsCache().InternalRevision.IncomingHeaders;
        Assert.True(headers.TryGetValue(name, out var header));
        Assert.Equal((uint)expected, header);
        Assert.Single(headers.Where(pair => pair.Value == (uint)expected));
    }

    [Fact]
    public async Task OldScalarAndTruncatedNativeFramesDoNotReachSave()
    {
        var saves = new Saves();
        var handler = new SaveWiredEffectConfigEvent(saves);
        await handler.Parse(null!, Packet(7, 0, "", 0, 0, 0));
        var body = Body(7, WiredBoxCategory.Action);
        body.Buffer = body.Buffer[..^1];
        await handler.Parse(null!, body);
        Assert.Empty(saves.Requests);
    }

    [WiredChestDatabaseFact]
    public void NativeVersionTwoRowLoadsAsItsDerivedRuntimeWithoutRewritingStoredAuthority()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");
        const string json = """
            {"Version":2,"Category":1,"NativeCode":6,"OwnedIntParams":[17,2],"Text":"","PrimaryItems":[],"SecondaryItems":[],"FurniSourceTypes":[],"UserSourceTypes":[0],"VariableIds":[],"Delay":0,"SavedState":{"Snapshots":[]}}
            """;
        db.Connection.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES(601,'wf_act_give_score',2,@Json)", new { Json = json });
        Assert.True(WiredBoxRegistry.TryGet("wf_act_give_score", out var descriptor));
        var loaded = new WiredConfigurationStore(db.Database).Load(601, descriptor)!;
        Assert.Equal(2, loaded.ScoreQuotaPerGame);
        Assert.Equal(new[] { 17, 0, 0, 2 }, loaded.IntParams);
        Assert.Equal(json, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601"));
    }

    [WiredChestDatabaseFact]
    public void NativeStoreRejectsVersionCodeAndAuthorityShapeCorruptionWithoutDowngrading()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE wired_item_configurations ADD schema_version INT NOT NULL DEFAULT 1");
        Assert.True(WiredBoxRegistry.TryGet("wf_act_give_score", out var descriptor));
        const string valid = """
            {"Version":2,"Category":1,"NativeCode":6,"OwnedIntParams":[17,2],"Text":"","PrimaryItems":[],"SecondaryItems":[],"FurniSourceTypes":[],"UserSourceTypes":[0],"VariableIds":[],"Delay":0,"SavedState":{"Snapshots":[]}}
            """;

        foreach (var (rowVersion, json) in new[] {
            (1, valid), (2, valid.Replace("\"Version\":2", "\"Version\":1")),
            (2, valid.Replace("\"NativeCode\":6", "\"NativeCode\":28")),
            (2, valid.Replace("\"Snapshots\":[]", "\"Snapshots\":null")),
            (2, valid.Replace("\"Text\":\"\"", "\"Text\":null")),
            (2, valid.Replace("\"Delay\":0", "\"Delay\":0,\"IntParams\":[17,0,0]"))
        }) {
            db.Connection.Execute("REPLACE INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES(601,'wf_act_give_score',@Version,@Json)", new { Version = rowVersion, Json = json });
            Assert.ThrowsAny<Exception>(() => new WiredConfigurationStore(db.Database).Load(601, descriptor));
            Assert.Equal(json, db.Connection.QuerySingle<string>("SELECT configuration FROM wired_item_configurations WHERE item_id=601"));
        }
    }

    internal static FlashIncomingPacket Body(int id, WiredBoxCategory category)
    {
        var values = new List<object> { id, 2, 31, 32, "caption", 2, 8, 9 };

        if (category == WiredBoxCategory.Action || category == WiredBoxCategory.Condition) {
            values.Add(1);
        }
        else if (category == WiredBoxCategory.Selector) {
            values.Add(true);
            values.Add(false);
        }

        values.AddRange(new object[] { 2, 100, 101, 1, 201, 2, "custom:17", "internal:@id", 1, 10 });

        return Packet(values.ToArray());
    }

    internal static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];

        foreach (var value in values) {
            if (value is bool flag) {
                stream.WriteByte(flag ? (byte)1 : (byte)0);
            }
            else if (value is int integer) {
                BinaryPrimitives.WriteInt32BigEndian(number, integer);
                stream.Write(number);
            }
            else {
                var bytes = Encoding.UTF8.GetBytes((string)value);
                BinaryPrimitives.WriteUInt16BigEndian(number, checked((ushort)bytes.Length));
                stream.Write(number[..2]);
                stream.Write(bytes);
            }
        }

        return new() { Buffer = stream.ToArray() };
    }

    private sealed class Saves : IWiredConfigurationService
    {
        public List<WiredConfigurationSaveRequest> Requests { get; } = [];
        public void Save(GameClient session, WiredConfigurationSaveRequest request) => Requests.Add(request);
    }
}
