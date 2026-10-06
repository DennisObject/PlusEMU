namespace Plus.HabboHotel.Users.Messenger;

// Every field the friend list writes for one buddy, captured before any packet is composed.
public sealed record MessengerBuddySnapshot(int Id, string Username, int Gender, bool Online, bool ShowInRoom, string Look, string Motto, short Relationship)
{
    public static MessengerBuddySnapshot Capture(MessengerBuddy buddy) =>
        new(buddy.Id, buddy.Username, buddy.Gender, !buddy.AppearOffline, !buddy.HideInRoom, buddy.Look, buddy.Motto, (short)buddy.Relationship);
}

// One change to a buddy. Added and updated changes carry the buddy snapshot; removed changes only need the id.
public sealed record MessengerBuddyModification(BuddyModificationType Type, int BuddyId, MessengerBuddySnapshot? Buddy)
{
    public static MessengerBuddyModification Capture(MessengerBuddy buddy, BuddyModificationType type) =>
        new(type, buddy.Id, type is BuddyModificationType.Added or BuddyModificationType.Updated ? MessengerBuddySnapshot.Capture(buddy) : null);
}
