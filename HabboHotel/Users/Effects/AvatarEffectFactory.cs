namespace Plus.HabboHotel.Users.Effects;
using Dapper;

internal static class AvatarEffectFactory
{
    /// <summary>
    /// Creates a new AvatarEffect with the specified details.
    /// </summary>
    /// <param name="habbo"></param>
    /// <param name="spriteId"></param>
    /// <param name="duration"></param>
    /// <returns></returns>
    public static AvatarEffect CreateNullable(Habbo habbo, int spriteId, double duration)
    {
        return new AvatarEffectStore(PlusEnvironment.DatabaseManager).Create(habbo.Id, spriteId, duration);
    }
}
