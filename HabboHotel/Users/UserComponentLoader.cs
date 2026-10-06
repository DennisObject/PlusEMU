using Plus.HabboHotel.Users.Clothing.Parts;
using Plus.HabboHotel.Users.Clothing;
using Plus.HabboHotel.Users.Effects;

namespace Plus.HabboHotel.Users;

public interface IUserComponentLoader
{
    UserComponentData Load(int userId);
}
public sealed record UserComponentData(IReadOnlyList<ClothingParts> Clothing, IReadOnlyList<AvatarEffect> Effects);

public sealed class UserComponentLoader(IClothingStore clothing, IAvatarEffectStore effects) : IUserComponentLoader
{
    public UserComponentData Load(int userId)
    {
        return new(clothing.Load(userId), effects.Load(userId));
    }

}
