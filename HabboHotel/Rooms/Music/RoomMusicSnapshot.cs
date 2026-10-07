using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms.Music
{
    public sealed record MusicSong(int Id, string Code, string Name, string Creator, string Data, int LengthMs)
    {
        public bool IsValid => Id > 0 && LengthMs is > 0 and <= int.MaxValue / 20
            && !string.IsNullOrWhiteSpace(Code) && Code.Length <= 255 && Name.Length <= 255 && Creator.Length <= 255
            && !string.IsNullOrWhiteSpace(Data) && Encoding.UTF8.GetByteCount(Data) <= 32768;
    }

    public sealed record MusicTrack(uint DiscId, uint OwnerId, MusicSong Song);
    public sealed record MusicPlayerState(ImmutableArray<MusicTrack> Tracks, int StartIndex, DateTimeOffset? StartedAt, long Version, bool Usable);
    public sealed record MusicPlayback(int SongId, int Position, int NextSongId, int NextPosition, int ElapsedMs)
    {
        public static MusicPlayback Stopped { get; } = new(-1, -1, -1, -1, -1);
    }
    public sealed record MusicPlaylist(int Capacity, ImmutableArray<MusicTrack> Tracks, int ElapsedMs);

    internal static class RoomMusicDefinition
    {
        public static bool IsMachine(ItemDefinition definition) => definition.Type == ItemType.Floor
            && (definition.ItemName is "sound_machine" or "sound_machine_pro" or "ads_idol_trax" or "traxsilver" or "traxgold" or "traxbronze"
                || definition.ItemName.StartsWith("sound_machine*", StringComparison.Ordinal));
        public static bool IsPlayer(ItemDefinition definition) => definition.Type == ItemType.Floor
            && (definition.InteractionType == InteractionType.Jukebox || IsMachine(definition)
                || definition.ItemName is "jukebox*1" or "jukebox_ptv*1" or "ads_idol_jukebox*1" or "jukebox_big");
        public static bool IsDisc(ItemDefinition definition) => definition.Type == ItemType.Floor
            && (definition.InteractionType == InteractionType.MusicDisc || definition.ItemName == "song_disk");
        public static int Capacity(ItemDefinition definition) => definition.ItemName == "jukebox_big" || IsMachine(definition) ? 20 : 10;
        public static int SongId(string extraData)
        {
            var fields = extraData.Split('\n');
            var value = fields.Length >= 7 ? fields[6] : extraData;

            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
        }
        public static InventoryItem Inventory(uint id, uint ownerId, ItemDefinition definition, string data, uint number, uint series) => new()
        {
            Id = id,
            OwnerId = ownerId,
            Definition = definition,
            ExtraData = new LegacyDataFormat { Data = data },
            UniqueNumber = number,
            UniqueSeries = series
        };
    }
}
