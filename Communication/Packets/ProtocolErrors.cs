namespace Plus.Communication.Packets;

public enum PurchaseError
{
    Unavailable = 0,
    Rejected = 1,
    InsufficientCredits = 2,
    InsufficientActivityPoints = 3,
    AlreadyOwned = 4,
    DeliveryFailed = 5
}

public enum VoucherRedeemError
{
    InvalidCode = 0
}

public enum PetNameError
{
    None = 0,
    TooLong = 1,
    TooShort = 2,
    InvalidCharacters = 3,
    Filtered = 4
}

public enum RoomConnectionError
{
    Full = 1,
    Banned = 4
}

public enum GenericError
{
    AuthenticationFailed = -3,
    ConnectionFailed = -400,
    KickedFromRoom = 4008,
    ClubRequired = 4009,
    UnacceptableRoomName = 4010,
    CannotBanGroupMember = 4011,
    WrongRoomPassword = -100002,
    TradeLocked = -13001
}

public enum TradingError
{
    PartnerUnavailable = 4,
    RoomDisallowsTrading = 6,
    AlreadyTrading = 7,
    PartnerAlreadyTrading = 8
}

public enum PetPlacementError
{
    RoomDisallowsPets = 1,
    RoomLimitReached = 2,
    InvalidTile = 4
}

public enum MarketplaceOfferEligibility
{
    Allowed = 1,
    TradingLocked = 6
}

public enum MarketplaceOfferResult
{
    Rejected = 0,
    Accepted = 1
}

public enum FriendFollowError
{
    Unavailable = 2
}

public enum IgnoreStatus
{
    Added = 1,
    Removed = 3
}

public enum PetPackageNameError
{
    None = 0,
    InvalidName = 1
}

public enum BullyReportResult
{
    Sent = 0,
    Blocked = 1,
    NoChat = 2,
    AlreadyReported = 3
}

public enum SupportTicketResult
{
    Useless = 1,
    Abusive = 2,
    Resolved = 3
}

public enum NameChangeError
{
    None = 0,
    TooShort = 2,
    TooLong = 3,
    InvalidName = 4,
    InUse = 5
}

public enum HabbiconActionError
{
    InvalidRequest = 1,
    InsufficientCredits = 2,
    InsufficientActivityPoints = 3,
    AlreadyOwned = 4,
    DeliveryFailed = 5,
    MessageForbidden = 6,
    MessageRateLimited = 7
}
