using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;

namespace Plus.HabboHotel.Rooms.PathFinding;

// §6.6: game furniture and Wired boxes read tile-keyed user positions that are not yet surface-aware,
// so a room containing any of them compiles at K=1. Walk on/off triggers are migrated: they react to
// the landing surface's contacts only.
public static class LayeringEligibility
{
    private static readonly HashSet<InteractionType> GameFurniture =
    [
        InteractionType.FootballGate, InteractionType.Football,
        InteractionType.FootballGoalGreen, InteractionType.FootballGoalYellow, InteractionType.FootballGoalBlue, InteractionType.FootballGoalRed,
        InteractionType.Footballcountergreen, InteractionType.Footballcounteryellow, InteractionType.Footballcounterblue, InteractionType.Footballcounterred,
        InteractionType.Banzaigateblue, InteractionType.Banzaigatered, InteractionType.Banzaigateyellow, InteractionType.Banzaigategreen,
        InteractionType.Banzaifloor, InteractionType.Banzaiscoreblue, InteractionType.Banzaiscorered, InteractionType.Banzaiscoreyellow,
        InteractionType.Banzaiscoregreen, InteractionType.Banzaicounter, InteractionType.Banzaitele, InteractionType.Banzaipuck,
        InteractionType.Banzaipyramid, InteractionType.Freezetimer, InteractionType.Freezeexit, InteractionType.Freezeredcounter,
        InteractionType.Freezebluecounter, InteractionType.Freezeyellowcounter, InteractionType.Freezegreencounter,
        InteractionType.FreezeYellowGate, InteractionType.FreezeRedGate, InteractionType.FreezeGreenGate, InteractionType.FreezeBlueGate,
        InteractionType.FreezeTileBlock, InteractionType.FreezeTile
    ];

    public static bool RequiresSingleSurface(NavItemRecord record) => record.Interaction switch
    {
        InteractionType.WiredTrigger => record.WiredType is not (WiredBoxType.TriggerWalkOnFurni or WiredBoxType.TriggerWalkOffFurni),
        InteractionType.WiredEffect or InteractionType.WiredCondition or InteractionType.WiredSelector
            or InteractionType.WiredAddon or InteractionType.WiredVariable => true,
        _ => GameFurniture.Contains(record.Interaction)
    };
}
