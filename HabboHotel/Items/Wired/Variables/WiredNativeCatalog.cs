using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Local native-wire capabilities, independent of scalar menu descriptions.</summary>
public sealed record WiredNativeVariable(string Id, int Type, string Name, int Availability, int Target,
    bool AlwaysAvailable, bool CanCreateAndDelete, bool HasValue, bool CanWriteValue,
    bool CanInterceptChanges, bool IsInvisible, bool CanReadCreationTime, bool CanReadLastUpdateTime,
    ImmutableArray<KeyValuePair<int, string>>? Connector)
{
    /// <summary>The AIR variable record: the catalog diff leads it with the row hash, a shared-variable row does not.</summary>
    public void Write(Plus.HabboHotel.GameClients.IOutgoingPacket packet, bool withHash)
    {
        if (withHash) {
            packet.WriteInteger(Hash);
        }

        packet.WriteString(Id);
        packet.WriteInteger(Type);
        packet.WriteString(Name);
        packet.WriteInteger(Availability);
        packet.WriteInteger(Target);
        packet.WriteBoolean(AlwaysAvailable);
        packet.WriteBoolean(CanCreateAndDelete);
        packet.WriteBoolean(HasValue);
        packet.WriteBoolean(CanWriteValue);
        packet.WriteBoolean(CanInterceptChanges);
        packet.WriteBoolean(IsInvisible);
        packet.WriteBoolean(CanReadCreationTime);
        packet.WriteBoolean(CanReadLastUpdateTime);
        packet.WriteBoolean(Connector.HasValue);

        if (Connector is { } connector) {
            packet.WriteInteger(connector.Length);

            foreach (var pair in connector) {
                packet.WriteInteger(pair.Key);
                packet.WriteString(pair.Value);
            }
        }
    }

    public int Hash
    {
        get
        {
            var hash = TextHash(Id);

            foreach (var value in new[] { Type, TextHash(Name), Availability, Target, AlwaysAvailable ? 1 : 0,
                CanCreateAndDelete ? 1 : 0, HasValue ? 1 : 0, CanWriteValue ? 1 : 0, CanInterceptChanges ? 1 : 0,
                IsInvisible ? 1 : 0, CanReadCreationTime ? 1 : 0, CanReadLastUpdateTime ? 1 : 0, Connector.HasValue ? 1 : 0 }) {
                hash = unchecked(hash * 31 + value);
            }

            if (Connector is { } connector) {
                hash = unchecked(hash * 31 + connector.Length);

                foreach (var pair in connector) {
                    hash = unchecked(hash * 31 + pair.Key);
                    hash = unchecked(hash * 31 + TextHash(pair.Value));
                }
            }

            return hash;
        }
    }

    private static int TextHash(string text)
    {
        var hash = 0;

        foreach (var character in text) {
            hash = unchecked(hash * 31 + character);
        }

        return hash;
    }
}

public sealed record WiredNativeCatalogDiff(int Hash, bool LastChunk, ImmutableArray<string> Removed,
    ImmutableArray<WiredNativeVariable> Changed);

/// <summary>Frozen rows are the sole source of both catalog hashes and response bodies.</summary>
public sealed class WiredNativeCatalog(ImmutableArray<WiredNativeVariable> variables)
{
    public ImmutableArray<WiredNativeVariable> Variables { get; } = variables;
    public int Hash => Variables.Aggregate(Variables.Length, (hash, variable) => unchecked(hash * 31 + variable.Hash));

    public ImmutableArray<WiredNativeCatalogDiff> Diff(IReadOnlyDictionary<string, int> known)
    {
        var current = Variables.ToDictionary(variable => variable.Id, StringComparer.Ordinal);
        var removed = known.Keys.Where(id => !current.ContainsKey(id)).Order(StringComparer.Ordinal).ToImmutableArray();
        var changed = Variables.Where(variable => !known.TryGetValue(variable.Id, out var hash) || hash != variable.Hash).ToArray();
        var count = Math.Max(1, (changed.Length + 99) / 100);

        return Enumerable.Range(0, count).Select(index => new WiredNativeCatalogDiff(Hash, index == count - 1,
            index == 0 ? removed : [], changed.Skip(index * 100).Take(100).ToImmutableArray())).ToImmutableArray();
    }
}

/// <summary>One shared-variable row of the editor's reference list: the room it lives in and the variable block.</summary>
public sealed record WiredNativeSharedVariable(uint RoomId, string RoomName, WiredNativeVariable Variable)
{
    /// <summary>
    /// A shared source offered from another room: the capabilities of its own ordinary domain (a user variable can be created and
    /// deleted, a room variable cannot; writes need a value). Whether this room's reference is read-only is the reference's own choice.
    /// </summary>
    public static WiredNativeSharedVariable Project(WiredSharedVariable shared)
    {
        var definition = shared.Definition;
        var endpoint = new WiredNativeEndpoint(new(definition, definition.HasValue, false), definition, null, true, false);
        var variable = WiredNativeCatalogProjection.Project(endpoint, null) with { Id = WiredVariableDefinitions.SharedId(shared.RoomId, definition.Target, definition.ItemId) };

        return new(shared.RoomId, shared.RoomName, variable);
    }
}

internal sealed record WiredNativeEndpoint(WiredVariableDescription Description, WiredVariableDefinition? Terminal,
    WiredVariableReference? Builtin, bool Writable, bool Converted);

internal static class WiredNativeCatalogProjection
{
    internal static WiredNativeVariable Project(WiredNativeEndpoint endpoint, ImmutableArray<KeyValuePair<int, string>>? connector)
    {
        var description = endpoint.Description;
        var definition = description.Definition;
        var derived = description.IsDerived || endpoint.Converted;
        var builtin = endpoint.Builtin;
        var hasValue = description.HasValue;
        var writable = !derived && endpoint.Writable;
        var presence = writable && (builtin is { } reference ? RoomWiredBuiltinVariables.SupportsPresenceMutation(reference)
            : endpoint.Terminal?.Target is WiredVariableTarget.User or WiredVariableTarget.Furni or WiredVariableTarget.Context);
        var timestamps = !derived && builtin is null && endpoint.Terminal is not null;
        var target = definition.Target switch
        {
            WiredVariableTarget.Furni => 0,
            WiredVariableTarget.User => 1,
            WiredVariableTarget.Global => -10,
            _ => -20
        };

        return new(description.CatalogId, description.IsDerived ? 3 : definition.Link is null ? 0 : 2,
            definition.Name, builtin is not null || definition.Target == WiredVariableTarget.Context ? 999
                : (int)(endpoint.Terminal?.Availability ?? definition.Availability), target,
            !derived && builtin is null && endpoint.Terminal is { Target: WiredVariableTarget.Global, HasValue: true },
            presence, hasValue, writable && hasValue && (builtin is null || RoomWiredBuiltinVariables.SupportsNumericWrite(builtin)),
            !derived && definition.Link is null && writable && builtin is null, false, timestamps, timestamps, connector);
    }
}
