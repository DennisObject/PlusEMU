using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Talents;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Talents;

public interface ITalentTrackProgressionService
{
    void Progress(Habbo habbo, IReadOnlyDictionary<string, Achievement> achievements);
}

public sealed class TalentTrackProgressionService(ITalentTrackManager talents, IItemDataManager items, ITalentTrackRewardStore rewards, ILogger<TalentTrackProgressionService> logger) : ITalentTrackProgressionService
{
    private readonly ConditionalWeakTable<Habbo, HashSet<(string Type, int Level)>> _claimed = new();

    public void Progress(Habbo habbo, IReadOnlyDictionary<string, Achievement> achievements)
    {
        lock (habbo.WalletSync) {
            if (habbo.WalletClosed) {
                return;
            }

            var claimed = _claimed.GetOrCreateValue(habbo);

            foreach (var track in talents.GetLevels().GroupBy(level => level.Type)) {
                var snapshot = TalentTrackSnapshot.Capture(track, habbo, achievements);

                foreach (var level in snapshot.Where(level => level.State == 2)) {
                    if (habbo.WalletClosed) {
                        return;
                    }

                    if (claimed.Contains((track.Key, level.Level))) {
                        continue;
                    }

                    // Unresolved configuration must neither grant an invented gift nor consume the claim.
                    var definitions = level.Gifts.Select(items.GetItemByName).ToArray();

                    if (definitions.Any(definition => definition == null || definition.ProductType is not ("s" or "i"))) {
                        logger.LogWarning("Unresolved or unsupported furniture reward in talent track {Type} level {Level}", track.Key, level.Level);
                        break;
                    }

                    var awarded = rewards.Claim(habbo.Id, track.Key, level.Level, definitions.Select(definition => definition!).ToArray());
                    claimed.Add((track.Key, level.Level));

                    if (awarded == null) {
                        continue;
                    }

                    foreach (var item in awarded) {
                        habbo.Inventory.Furniture.AddItem(item);
                    }

                    // Publish all committed inventory before any send can synchronously disconnect.
                    foreach (var item in awarded) {
                        habbo.Client?.Send(new FurniListNotificationComposer(item.Id, item.IsFloorItem ? 1 : 2));
                    }

                    if (awarded.Count != 0) {
                        habbo.Client?.Send(new FurniListUpdateComposer());
                    }

                    habbo.Client?.Send(new TalentLevelUpComposer(track.Key, level));
                }
            }
        }
    }
}
