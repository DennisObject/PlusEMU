using System.Collections.Immutable;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Navigator.New;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class NavigatorPresentationTests
{
    private const string Allowed = "navigator.allowed";
    private const string Denied = "navigator.denied";

    [Fact]
    public async Task HandlersOnlyDelegateAndDecodeNothing()
    {
        var presentation = new RecordingPresentation();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        await new InitializeNewNavigatorEvent(presentation).Parse(client, HabbiconTestSupport.Incoming());
        await new GetUserFlatCatsEvent(presentation).Parse(client, HabbiconTestSupport.Incoming());
        await new GetEventCategoriesEvent(presentation).Parse(client, HabbiconTestSupport.Incoming());

        Assert.Equal(new[] { "initialize", "user", "events" }, presentation.Calls);
        Assert.Empty(sent);
    }

    [Fact]
    public void InitializeSendsMetadataThenLiftedThenCollapsedThenPreferences()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        new NavigatorPresentationService(Manager([new TopLevelItem(1, "hotel_view", "", "")], [], [])).InitializeNewNavigator(client);

        Assert.Equal(new[]
        {
            ServerPacketHeader.NavigatorMetaDataParserComposer,
            ServerPacketHeader.NavigatorLiftedRoomsComposer,
            ServerPacketHeader.NavigatorCollapsedCategoriesComposer,
            ServerPacketHeader.NavigatorPreferencesComposer,
        }, sent.Select(packet => packet.Header));
        var metadata = new FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal((1, "hotel_view", 0), (metadata.ReadInt(), metadata.ReadString(), metadata.ReadInt()));
    }

    [Fact]
    public void MetadataWritesEachSearchCodeWithItsZeroSavedSearchCount()
    {
        Assert.Equal(new object[] { 2, "hotel_view", 0, "myworld", 0 }, Write(new NavigatorMetaDataParserComposer(ImmutableArray.Create("hotel_view", "myworld"))));
        Assert.Equal(new object[] { 0 }, Write(new NavigatorMetaDataParserComposer(ImmutableArray<string>.Empty)));
    }

    [Fact]
    public void UserFlatCategoriesWriteTheExactRowLayout()
    {
        var rows = ImmutableArray.Create(new NavigatorCategoryRow(1, "Public", true), new NavigatorCategoryRow(2, "Staff", false));

        Assert.Equal(new object[]
        {
            2,
            1, "Public", true, false, "", "", false,
            2, "Staff", false, false, "", "", false,
        }, Write(new UserFlatCatsComposer(rows)));
        Assert.Equal(new object[] { 0 }, Write(new UserFlatCatsComposer(ImmutableArray<NavigatorCategoryRow>.Empty)));
    }

    [Fact]
    public void EventCategoriesWriteIdNameAndSelectability()
    {
        Assert.Equal(new object[] { 1, 3, "Events", true }, Write(new NavigatorFlatCatsComposer(ImmutableArray.Create(new NavigatorCategoryRow(3, "Events", true)))));
        Assert.Equal(new object[] { 0 }, Write(new NavigatorFlatCatsComposer(ImmutableArray<NavigatorCategoryRow>.Empty)));
    }

    [Fact]
    public void AccessIsCheckedOnceForPublicAllowedAndDeniedCategories()
    {
        var categories = new List<SearchResultList> { Category(1, "Public", ""), Category(2, "Allowed", Allowed), Category(3, "Denied", Denied) };

        Assert.Equal(new[] { (1, "Public", true), (2, "Allowed", true), (3, "Denied", false) }, Categories(Payload(new Habbo { Id = 7, Access = Access(Allowed) }, categories)));
        Assert.Equal(new[] { (1, "Public", true), (2, "Allowed", false), (3, "Denied", false) }, Categories(Payload(new Habbo { Id = 8, Access = Access() }, categories)));
    }

    [Fact]
    public void CapturedRowsSurviveSourceMutationAndRecomposeIdentically()
    {
        var category = Category(1, "Public", "");
        var categories = new List<SearchResultList> { category };
        var rows = categories.Select(item => NavigatorCategoryRow.CaptureForUser(item, Access())).ToImmutableArray();
        var first = Write(new UserFlatCatsComposer(rows));

        category.PublicName = "Changed";
        category.RequiredPermission = Denied;
        categories.Clear();

        Assert.Equal(first, Write(new UserFlatCatsComposer(rows)));
        Assert.Equal(first, Write(new UserFlatCatsComposer(rows)));
    }

    [Fact]
    public void TopLevelSearchCodesSurviveSourceMutation()
    {
        var item = new TopLevelItem(1, "hotel_view", "", "");
        var items = new List<TopLevelItem> { item };
        var codes = items.Select(entry => entry.SearchCode).ToImmutableArray();
        var first = Write(new NavigatorMetaDataParserComposer(codes));

        item.SearchCode = "changed";
        items.Clear();

        Assert.Equal(first, Write(new NavigatorMetaDataParserComposer(codes)));
    }

    private static List<object> Write(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes;
    }

    // Reads the flat-category packet layout: count, then id, name, selectable and the five fixed fields per entry.
    private static (int Id, string Name, bool CanSelect)[] Categories(byte[] payload)
    {
        var reader = new FlashIncomingPacket { Buffer = payload };
        var count = reader.ReadInt();
        var rows = new (int, string, bool)[count];
        for (var index = 0; index < count; index++)
        {
            rows[index] = (reader.ReadInt(), reader.ReadString(), reader.ReadBool());
            reader.ReadBool();
            reader.ReadString();
            reader.ReadString();
            reader.ReadBool();
        }
        return rows;
    }

    private static SearchResultList Category(int id, string name, string required) =>
        new(id, "category", "identifier", name, true, 0, required, NavigatorViewMode.Regular, "category", "nothing", id);

    private static UserAccess Access(params string[] rights) => EditorTestSupport.Access(rights);

    private static byte[] Payload(Habbo habbo, List<SearchResultList> categories)
    {
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        new NavigatorPresentationService(Manager([], categories, [])).ShowUserFlatCategories(client);
        return Assert.Single(sent).Payload;
    }

    private static INavigatorManager Manager(IReadOnlyCollection<TopLevelItem> topLevel, IReadOnlyCollection<SearchResultList> flat, IReadOnlyCollection<SearchResultList> events) =>
        CatalogSnapshotTestSupport.Proxy<INavigatorManager>((method, _) => method switch
        {
            "get_TopLevelItems" => topLevel,
            "get_FlatCategories" => flat,
            "get_EventCategories" => events,
            _ => throw new NotSupportedException(method),
        });

    private sealed class RecordingPresentation : INavigatorPresentationService
    {
        public List<string> Calls { get; } = new();
        public void InitializeNewNavigator(GameClient session) => Calls.Add("initialize");
        public void ShowUserFlatCategories(GameClient session) => Calls.Add("user");
        public void ShowEventCategories(GameClient session) => Calls.Add("events");
    }
}
