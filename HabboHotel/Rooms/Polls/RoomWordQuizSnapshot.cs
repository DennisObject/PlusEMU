namespace Plus.HabboHotel.Rooms.Polls;

public sealed record RoomWordQuizSnapshot(int QuestionId, string Question, int DurationMilliseconds, int No, int Yes);
