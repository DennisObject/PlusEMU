using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Campaigns
{
    [Singleton]
    public interface ICampaignCalendarService
    {
        void Present(GameClient session);
        void Open(GameClient session, string campaignName, int day, bool staff = false);
    }

    public sealed record CalendarCampaign(int Id, string Name, string Image, DateTimeOffset StartsAt, int Days,
        bool LockExpired, double ClubDucketsMultiplier)
    {
        public int CurrentDay(DateTimeOffset now) => (int)Math.Floor((now - StartsAt).TotalDays);
        public bool Active(DateTimeOffset now) => Days is > 0 and <= 366 && now >= StartsAt && CurrentDay(now) < Days;
        public bool CanOpen(DateTimeOffset now, int day, bool staff) => Active(now) && day >= 0 && day < Days &&
            (staff || day <= CurrentDay(now) && (!LockExpired || day >= CurrentDay(now) - 2));
        public CalendarDataSnapshot Capture(DateTimeOffset now, IEnumerable<int> opened)
        {
            var days = opened.Where(day => day >= 0 && day < Days).Distinct().Order().ToImmutableArray();
            var missed = Enumerable.Range(0, Math.Max(0, CurrentDay(now) - 2)).Where(day => LockExpired && !days.Contains(day)).ToImmutableArray();

            return new(Name, Image, CurrentDay(now), Days, days, missed);
        }
    }

    public sealed record CalendarReward(int Id, int CampaignId, string ProductName, string CustomImage, int Credits,
        int Duckets, int Diamonds, string Badge, uint ItemId, int ClubDays);
    public sealed record CalendarOffer(CalendarCampaign Campaign, ImmutableArray<CalendarReward> Rewards);
    public sealed record CalendarDataSnapshot(string Name, string Image, int CurrentDay, int Days,
        ImmutableArray<int> Opened, ImmutableArray<int> Missed);
    public sealed record CalendarGrant(uint ItemId, int Credits, int Duckets, int Diamonds, string Badge, int ClubDays);

    [Singleton]
    public interface ICampaignCalendarStore
    {
        CalendarOffer? Find(string? name, DateTimeOffset now);
        IReadOnlyList<int> Opened(int userId, int campaignId);
        CalendarGrant? Claim(int userId, CalendarCampaign campaign, CalendarReward reward, int day, bool staff,
            DateTimeOffset now, int credits, int duckets, int diamonds, string badge);
    }
}
