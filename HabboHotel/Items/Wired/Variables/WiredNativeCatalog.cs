using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Local native-wire capabilities, independent of scalar menu descriptions.</summary>
public sealed record WiredNativeVariable(string Id, int Type, string Name, int Availability, int Target,
    bool AlwaysAvailable, bool CanCreateAndDelete, bool HasValue, bool CanWriteValue,
    bool CanInterceptChanges, bool IsInvisible, bool CanReadCreationTime, bool CanReadLastUpdateTime,
    ImmutableArray<KeyValuePair<int, string>>? Connector)
{
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
