using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Plus.Communication.Flash;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

namespace Plus.Tests;

internal static class EditorTestSupport
{
    public const string RestrictedPagePermission = "catalog.pages.owner";

    public static Habbo Owner() => Staff(90, PermissionKeys.CatalogEdit, PermissionKeys.FurniEdit, PermissionKeys.FurniDelete, RestrictedPagePermission);

    public static Habbo Staff(int weight = 90, params string[] rights) => new()
    {
        Id = 7001,
        Username = "editor",
        Access = Access(rights.Length == 0
            ? [PermissionKeys.CatalogEdit, PermissionKeys.FurniEdit, PermissionKeys.FurniDelete]
            : rights, weight)
    };

    public static Habbo Player() => new() { Id = 7002, Username = "player", Access = Access([]) };

    public static UserAccess Access(string[] rights, int weight = 0, IEnumerable<UserPermissionOverride>? overrides = null) => UserAccess.Create(
        [new RoleAssignment(new AccessRole(9, "editor", "Editor", weight, 1, "", true, rights, new Dictionary<string, int>()))],
        overrides: overrides, registry: PermissionKeys.All.Select(permission => permission.Key).Append(RestrictedPagePermission).Concat(rights).Distinct());

    // Writes values the way Octane's EvaWire encoder does: int32, int16-prefixed UTF-8 string, one byte boolean.
    public static FlashIncomingPacket Incoming(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            switch (value)
            {
                case int number:
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                    stream.Write(bytes);
                    break;
                case bool flag:
                    stream.WriteByte(flag ? (byte)1 : (byte)0);
                    break;
                case string text:
                    var encoded = Encoding.UTF8.GetBytes(text);
                    var length = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)encoded.Length));
                    stream.Write(length);
                    stream.Write(encoded);
                    break;
                default:
                    throw new ArgumentException($"Unsupported value {value}");
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    // A database that fails the test if anything touches it: denied requests must stop before SQL.
    public static IDatabase UntouchableDatabase() => DispatchProxy.Create<IDatabase, Untouchable>();

    public class Untouchable : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Database used: {targetMethod?.Name}");
    }
}
