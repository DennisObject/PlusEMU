using System.Collections.Immutable;

namespace Plus.HabboHotel.Users.Clothing;

public sealed record WardrobeSlot(int SlotId, string Look, string Gender);

public sealed record WardrobeSnapshot(ImmutableArray<WardrobeSlot> Slots);
