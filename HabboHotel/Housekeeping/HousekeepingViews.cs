namespace Plus.HabboHotel.Housekeeping;

public sealed record HousekeepingUserDetail(int Id, string Username, string Motto, string Figure, int Rank, string RankName, bool Online,
    int LastOnlineAt, int Credits, int Duckets, int Diamonds, string Email, string IpLast, bool IsBanned, bool IsMuted, bool IsTradeLocked);

public sealed record HousekeepingRoom(int Id, string Name, string Description, int OwnerId, string OwnerName, int UserCount, int MaxUsers,
    bool IsLocked, bool IsMuted, bool IsPublic, int CreatedAt);

public sealed record HousekeepingDashboard(int OnlineUsers, int TotalUsers, int ActiveRooms, int TotalRooms, int PeakOnlineToday,
    int PeakOnlineAllTime, int PendingTickets, int SanctionsLast24h, int ServerUptimeSeconds, string ServerVersion);
