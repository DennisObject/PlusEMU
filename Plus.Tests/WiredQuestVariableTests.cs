using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class WiredQuestVariableTests
{
    private static Quest Q(int id, string category, int number, QuestType type, int goal, string name) =>
        new(id, category, number, type, goal, name, 0, "", 0, null, null);

    private static readonly Quest[] Table =
    [
        Q(1, "social", 1, QuestType.SocialChat, 5, "chat"),
        Q(2, "social", 2, QuestType.SocialWave, 3, "wave"),
        Q(3, "social", 3, QuestType.ExploreFindItem, 99, "find"),
        Q(4, "solo", 1, QuestType.SocialDance, 2, "dance"),
        Q(5, "dupes", 1, QuestType.SocialChat, 1, "twin"),
        Q(6, "dupes", 2, QuestType.SocialChat, 1, "twin"),
        Q(7, "clash", 1, QuestType.SocialChat, 1, "a"),
        Q(8, "clash", 1, QuestType.SocialChat, 1, "b")
    ];

    private static Habbo Player(params (int Quest, int Progress)[] progress)
    {
        var habbo = (Habbo)RuntimeHelpers.GetUninitializedObject(typeof(Habbo));
        habbo.Quests = progress.ToDictionary(pair => pair.Quest, pair => pair.Progress);

        return habbo;
    }

    [Fact]
    public void NamesResolveOnlyToOneExactQuestOrChain()
    {
        var quests = new Quests(Table);
        Assert.Equal(1, WiredQuestVariables.ResolveQuest(quests, "chat")!.Id);
        Assert.Null(WiredQuestVariables.ResolveQuest(quests, "Chat")); // exact, case sensitive
        Assert.Null(WiredQuestVariables.ResolveQuest(quests, "twin")); // ambiguous never picks the first
        Assert.Null(WiredQuestVariables.ResolveQuest(quests, "missing"));
        Assert.Equal(new[] { 1, 2, 3 }, WiredQuestVariables.ResolveChain(quests, "social")!.Select(step => step.Id));
        Assert.Null(WiredQuestVariables.ResolveChain(quests, "clash")); // two steps at one position
        Assert.Null(WiredQuestVariables.ResolveChain(quests, "nothing"));
    }

    [Fact]
    public void QuestValueIsTheRealProgressAndChainValueCountsCompletedSteps()
    {
        var quests = new Quests(Table);
        var player = Player((1, 5), (2, 1), (3, 1));
        Assert.Equal(5, WiredQuestVariables.Read(quests, new(WiredQuestKind.Quest, "chat"), player));
        Assert.Equal(0, WiredQuestVariables.Read(quests, new(WiredQuestKind.Quest, "dance"), player)); // no progress is a real zero
        Assert.Null(WiredQuestVariables.Read(quests, new(WiredQuestKind.Quest, "twin"), player));
        Assert.Null(WiredQuestVariables.Read(quests, new(WiredQuestKind.Quest, "chat"), null)); // offline or bot
        // Step 1 (5/5) and the find-item step (1 completes it) are done; step 2 is 1/3.
        Assert.Equal(2, WiredQuestVariables.Read(quests, new(WiredQuestKind.Chain, "social"), player));
        Assert.Null(WiredQuestVariables.Read(quests, new(WiredQuestKind.Chain, "clash"), player));
    }

    [Fact]
    public void DerivedKeysUseTheQuestTablesIncludingTheFindItemTarget()
    {
        var quests = new Quests(Table);
        var chat = new WiredQuestBinding(WiredQuestKind.Quest, "chat");
        Assert.Equal([2L, 5, 0, 40, 3], Enumerable.Range(0, 5).Select(sub => WiredQuestVariables.Derive(quests, chat, 2, sub)!.Value));
        Assert.Equal(1, WiredQuestVariables.Derive(quests, chat, 5, 2));
        var find = new WiredQuestBinding(WiredQuestKind.Quest, "find");
        Assert.Equal(1, WiredQuestVariables.Derive(quests, find, 0, 1)); // effective target, not the stored 99
        Assert.Equal(1, WiredQuestVariables.Derive(quests, find, 1, 2));
        var chain = new WiredQuestBinding(WiredQuestKind.Chain, "social");
        // The current step is player-specific, so only the count-based keys derive from the completed count.
        Assert.Equal([3L, 0, 66], Enumerable.Range(1, 3).Select(sub => WiredQuestVariables.Derive(quests, chain, 2, sub)!.Value));
        Assert.Null(WiredQuestVariables.Derive(quests, chain, 2, 0));
        Assert.Equal(1, WiredQuestVariables.Derive(quests, chain, 3, 2));
        Assert.Null(WiredQuestVariables.Derive(quests, new(WiredQuestKind.Quest, "missing"), 1, 0));
    }

    [Fact]
    public void CurrentStepIsTheActualFirstIncompleteStepEvenWhenCompletionIsOutOfOrderOrNumbersHaveGaps()
    {
        var quests = new Quests([Q(11, "gap", 2, QuestType.SocialChat, 1, "g2"), Q(12, "gap", 5, QuestType.SocialChat, 1, "g5"), Q(13, "gap", 9, QuestType.SocialChat, 1, "g9")]);
        var chain = new WiredQuestBinding(WiredQuestKind.Chain, "gap");
        Assert.Equal(2, WiredQuestVariables.CurrentStep(quests, chain, Player()));
        // Steps at numbers 5 and 9 are done, step 2 is not: two complete, yet the current step is 2, not the third position.
        var outOfOrder = Player((12, 1), (13, 1));
        Assert.Equal(2, WiredQuestVariables.Read(quests, chain, outOfOrder));
        Assert.Equal(2, WiredQuestVariables.CurrentStep(quests, chain, outOfOrder));
        Assert.Equal(9, WiredQuestVariables.CurrentStep(quests, chain, Player((11, 1), (12, 1), (13, 1)))); // finished: the last step
        Assert.Null(WiredQuestVariables.CurrentStep(quests, chain, null));
        Assert.Null(WiredQuestVariables.CurrentStep(quests, new(WiredQuestKind.Quest, "g2"), outOfOrder));
        Assert.True(WiredQuestVariables.TryParseKey("@quest.7.current_step", out var id, out var part) && id == 7 && part == "current_step");
        Assert.False(WiredQuestVariables.TryParseKey("@quest.7.other", out _, out _));
    }

    [Fact]
    public void DefinitionIsAReadOnlyUserVariableOverItsOwnTokenAndBlankStaysInactive()
    {
        Assert.True(WiredBoxRegistry.TryGet("wf_var_quest", out var descriptor));
        var blank = WiredNativeEditorProjection.DefaultNative(descriptor);
        Assert.True(WiredNativeEditorProjection.TryCompile(7, descriptor, blank, out var inactive));
        Assert.False(WiredVariableDefinitions.TryDecode("wf_var_quest", 7, 1, 5, inactive, out _, out _));
        Assert.True(WiredNativeEditorProjection.TryCompile(7, descriptor, blank with { Text = "tasks\tchat" }, out var runtime));
        Assert.True(WiredVariableDefinitions.TryDecode("wf_var_quest", 7, 1, 5, runtime, out var definition, out _));
        Assert.Equal(WiredVariableTarget.User, definition!.Target);
        Assert.Equal("tasks", definition.Name);
        Assert.True(definition.HasValue);
        Assert.Equal(new WiredVariableReference(WiredVariableTarget.User, "internal:@quest.7"), definition.Link!.Source);
        Assert.True(definition.Link.ReadOnly);
        // The builtin token is not a picker id anyone can name directly.
        Assert.False(WiredVariableDescription.TryParseCatalogId("user:internal:@quest.7", out _, out _));
        Assert.True(WiredNativeEditorProjection.TryCompile(7, descriptor, blank with { Text = "tasks only" }, out var malformed));
        Assert.False(WiredVariableDefinitions.TryDecode("wf_var_quest", 7, 1, 5, malformed, out _, out _));
    }

    public sealed class Quests(Quest[] table) : IQuestManager
    {
        public IReadOnlyList<Quest> GetQuests() => table;
        public Quest GetQuest(int id) => table.Single(quest => quest.Id == id);
        public void Init() { }
        public int GetAmountOfQuestsInCategory(string category) => table.Count(quest => quest.Category == category);
        public void ProgressUserQuest(GameClient session, QuestType type, int data = 0) => throw new NotSupportedException();
        public Quest? GetNextQuestInSeries(string category, int number) => throw new NotSupportedException();
        public void GetList(GameClient session, ClientPacket? message) => throw new NotSupportedException();
        public void QuestReminder(GameClient session, int questId) => throw new NotSupportedException();
    }
}
