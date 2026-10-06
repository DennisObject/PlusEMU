using System.Drawing;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.Rooms.AI.Speech;

namespace Plus.HabboHotel.Rooms.AI;

public class RoomBot
{
    public bool IsTemporary { get; init; }

    public BotAiType AiType;

    public bool AutomaticChat;
    public int BotId;

    public int DanceId;
    public string Gender;
    public int Id;

    public string Look;
    public int MaxX;
    public int MaxY;
    public int MinX;
    public int MinY;
    public bool MixSentences;
    public string Motto;
    public string Name;

    public int OwnerId;
    public List<RandomSpeech> RandomSpeech;
    public uint RoomId;

    public RoomUser RoomUser;
    public int Rot;
    public int SpeakingInterval;
    public int VirtualId;


    public string WalkingMode;

    public int X;
    public int Y;
    public double Z;

    public RoomBot(int id, uint roomId, string type, string walkingMode, string name, string motto, string look, int x, int y, double z, int rotation,
        int minX, int minY, int maxX, int maxY, ref List<RandomSpeech> speeches, string gender, int dance, int ownerId,
        bool automaticChat, int speakingInterval, bool mixSentences, int chatBubble)
    {
        Id = id;
        BotId = id;
        RoomId = roomId;
        Name = name;
        Motto = motto;
        Look = look;
        Gender = gender.ToUpper();
        AiType = BotUtility.GetAiFromString(type);
        WalkingMode = walkingMode;
        X = x;
        Y = y;
        Z = z;
        Rot = rotation;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        VirtualId = -1;
        RoomUser = null;
        DanceId = dance;
        LoadRandomSpeech(speeches);
        OwnerId = ownerId;
        AutomaticChat = automaticChat;
        SpeakingInterval = speakingInterval;
        MixSentences = mixSentences;
        ChatBubble = chatBubble;
        ForcedMovement = false;
        TargetCoordinate = new();
        TargetUser = 0;
    }

    public bool ForcedMovement { get; set; }
    public int ForcedUserTargetMovement { get; set; }
    public Point TargetCoordinate { get; set; }

    public int TargetUser { get; set; }

    public bool IsPet => AiType == BotAiType.Pet;

    public int ChatBubble { get; set; }

    public void LoadRandomSpeech(List<RandomSpeech> speeches)
    {
        RandomSpeech = new();

        foreach (var speech in speeches) {
            if (speech.BotId == BotId) {
                RandomSpeech.Add(speech);
            }
        }
    }


    public RandomSpeech GetRandomSpeech()
    {
        if (RandomSpeech.Count < 1) {
            return new("", 0);
        }

        return RandomSpeech[Random.Shared.Next(0, RandomSpeech.Count)];
    }

}
