using Plus.HabboHotel.Users.Inventory.Furniture;
using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed partial class WiredRoomVariables
{
    private readonly object _membershipGate = new();
    private long _membershipEpoch;

    /// <summary>Observational capture: detected changes refuse the whole reply, never an empty replacement.</summary>
    public bool TryCaptureNativeCatalog(out WiredNativeCatalog? catalog)
    {
        catalog = null;

        try {
            var roomId = _room.Id;

            if (roomId != Module.RoomId) {
                return false;
            }

            KeyValuePair<uint, WiredVariableDefinitionBox>[] definitions;
            KeyValuePair<uint, WiredVariableMetadataBox>[] metadata;
            KeyValuePair<uint, WiredVariableTextInputBox>[] inputs;
            Dictionary<IWiredConfiguredItem, WiredConfiguration> configurations;
            long epoch;

            lock (_membershipGate) {
                epoch = _membershipEpoch;
                definitions = _definitions.ToArray();
                metadata = _metadata.ToArray();
                inputs = _textInputs.ToArray();
                configurations = definitions.Select(pair => (IWiredConfiguredItem)pair.Value)
                    .Concat(metadata.Select(pair => pair.Value)).Concat(inputs.Select(pair => pair.Value))
                    .Distinct().ToDictionary(box => box, box => box.Configuration);
            }

            var ownerId = _room.OwnerId;
            var boxes = definitions.Select(pair => (pair.Key, Box: (IWiredConfiguredItem)pair.Value))
                .Concat(metadata.Select(pair => (pair.Key, Box: (IWiredConfiguredItem)pair.Value)))
                .Concat(inputs.Select(pair => (pair.Key, Box: (IWiredConfiguredItem)pair.Value)))
                .Select(pair => NativeBoxInput.Capture(this, pair.Key, pair.Box, configurations[pair.Box])).ToArray();

            if (boxes.Any(box => box is null)) {
                return false;
            }

            var captured = boxes.OfType<NativeBoxInput>().ToArray();
            var decoded = new List<WiredVariableDefinition>();

            foreach (var pair in definitions) {
                if (!pair.Value.HasPersistedConfiguration || !WiredVariableDefinitions.TryDecode(pair.Value.Descriptor.CanonicalName,
                    pair.Key, _room.Id, (uint)_room.OwnerId, configurations[pair.Value], out var definition, out _)) {
                    return false;
                }

                decoded.Add(definition!);
            }

            // Metadata is decoded from the one captured Configuration, never the separately updated box caches.
            var passive = captured.Where(box => box.Box is WiredVariableMetadataBox)
                .Select(box => NativeMetadata.Decode(box)).ToArray();
            var derivations = new Dictionary<WiredVariableReference, WiredVariableDerivation>();
            var derivedDefinitions = new List<WiredVariableDefinition>();
            var probes = new Dictionary<uint, Item?>();
            var connectors = new Dictionary<uint, ImmutableArray<KeyValuePair<int, string>>?>();
            var zone = TimeZone();

            foreach (var definition in decoded.Take(4096)) {
                var owning = captured.Single(box => box.Key == definition.ItemId && box.Box is WiredVariableDefinitionBox);
                var associated = passive.Where(box => box.Input.X == owning.X && box.Input.Y == owning.Y)
                    .OrderBy(box => box.Input.Z).ThenBy(box => box.Input.Key).ToArray();
                connectors[definition.ItemId] = associated.FirstOrDefault(box => box.Input.Name == "wf_xtra_var_text_connector")?.Connector;

                if (definition.Target == WiredVariableTarget.Context) {
                    continue;
                }

                if (owning.Name is "wf_var_quest" or "wf_var_quest_chain" && _quests is not null
                    && WiredQuestVariables.Binding(owning.Name, owning.Configuration.Text) is { } quest) {
                    var questKeys = WiredQuestVariables.Keys(quest.Kind);

                    foreach (var sub in Enumerable.Range(0, questKeys.Length)) {
                        // A chain's current step is read holder-aware through its builtin part, not from the completed count.
                        AddDerived(sub, false, questKeys[sub], true, false, quest.Kind == WiredQuestKind.Chain && sub == 0 ? value => value
                            : value => WiredQuestVariables.Derive(_quests, quest, value.Value, sub) is { } derived ? new(derived, null, null) : null,
                            quest.Kind == WiredQuestKind.Chain && sub == 0 ? new(WiredVariableTarget.User, WiredQuestVariables.Token(definition.ItemId, WiredQuestVariables.CurrentStepPart)) : null);
                    }
                }

                var level = associated.FirstOrDefault(box => box.Keys.Length != 0);

                if (level is not null) {
                    foreach (var sub in level.Subs) {
                        AddDerived(sub, false, level.Keys[sub], true, false, value => new(level.Read(value.Value, sub), null, null));
                    }
                }

                var time = associated.FirstOrDefault(box => box.Time is not null)?.Time;

                if (time is not null) {
                    foreach (var sub in time.Selected.ToArray()) {
                        AddDerived(sub, true, WiredVariableTimeUtilities.Key(sub), time.Mode == 0, time.Mode != 0,
                            value => time.Read(value, sub, zone) is { } result ? new(result, null, null) : null);
                    }
                }

                void AddDerived(int sub, bool isTime, string key, bool requiresValue, bool requiresTimestamps,
                    Func<WiredVariableValue, WiredVariableValue?> read, WiredVariableReference? source = null)
                {
                    if (SyntheticId(definition.Target, definition.ItemId, sub, isTime) is not { } id) {
                        return;
                    }

                    var collision = _room.GetRoomItemHandler().GetItem(id);
                    probes[id] = collision;

                    if (collision is not null) {
                        return;
                    }

                    var derived = definition with { ItemId = id, Name = definition.Name + "." + key };
                    derivations.Add(new(derived.Target, derived.Token), new(source ?? new(definition.Target, definition.Token), read, requiresValue, requiresTimestamps));
                    derivedDefinitions.Add(derived);
                }
            }

            // No map lock is held across Module, directory SQL, metadata readers or callbacks.
            var endpoints = Module.DescribeNativeDefinitions(decoded, derivations, derivedDefinitions);

            if (endpoints is null) {
                return false;
            }

            var variables = endpoints.Select(endpoint => WiredNativeCatalogProjection.Project(endpoint,
                endpoint.Description.IsDerived ? null : connectors.GetValueOrDefault(endpoint.Description.Definition.ItemId)))
                .OrderBy(variable => variable.Target).ThenBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(variable => variable.Id, StringComparer.Ordinal).ToImmutableArray();

            if (variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).Count() != variables.Length
                || _room.Id != roomId || _room.OwnerId != ownerId || captured.Any(box => !box.Matches(this))
                || probes.Any(pair => !ReferenceEquals(_room.GetRoomItemHandler().GetItem(pair.Key), pair.Value))) {
                return false;
            }

            lock (_membershipGate) {
                if (epoch != _membershipEpoch || !SameMap(definitions, _definitions) || !SameMap(metadata, _metadata)
                    || !SameMap(inputs, _textInputs) || configurations.Any(pair => !ReferenceEquals(pair.Key.Configuration, pair.Value))) {
                    return false;
                }
            }

            catalog = new(variables);

            return true;
        }
        catch (Exception) {
            return false;
        }
    }

    private static bool SameMap<T>(KeyValuePair<uint, T>[] captured, Dictionary<uint, T> current) where T : class =>
        captured.Length == current.Count && captured.All(pair => current.TryGetValue(pair.Key, out var value) && ReferenceEquals(value, pair.Value));

    private sealed record NativeBoxInput(uint Key, IWiredConfiguredItem Box, Item Item, ItemDefinition Definition,
        WiredBoxDescriptor Descriptor, WiredConfiguration Configuration, int X, int Y, double Z, uint Owner, bool Temporary,
        uint DefinitionId, ItemType DefinitionType, WiredBoxType WiredType, WiredBoxDescriptor? DefinitionDescriptor)
    {
        public string Name => Descriptor.CanonicalName;

        public static NativeBoxInput? Capture(WiredRoomVariables owner, uint key, IWiredConfiguredItem box, WiredConfiguration configuration)
        {
            var item = box.Item;

            lock (item.NavSync) {
                var captured = new NativeBoxInput(key, box, item, item.Definition, box.Descriptor, configuration,
                    item.GetX, item.GetY, item.GetZ, item.OwnerId, item.IsTemporary,
                    item.Definition.Id, item.Definition.Type, item.Definition.WiredType, item.Definition.WiredDescriptor);

                return captured.Matches(owner) ? captured : null;
            }
        }

        public bool Matches(WiredRoomVariables owner) => Key == Item.Id && ReferenceEquals(Box.Item, Item)
            && ReferenceEquals(Box.Instance, owner._room) && Item.RoomId == owner._room.Id
            && ReferenceEquals(Item.Definition, Definition) && Definition.Id == DefinitionId && Definition.Type == DefinitionType
            && Definition.WiredType == WiredType && ReferenceEquals(Definition.WiredDescriptor, DefinitionDescriptor)
            && ReferenceEquals(Box.Descriptor, Descriptor)
            && ReferenceEquals(Box.Configuration, Configuration) && Item.OwnerId == Owner && Item.IsTemporary == Temporary
            && Item.GetX == X && Item.GetY == Y && Item.GetZ == Z && owner.IsAttached(Item);
    }

    private sealed record NativeMetadata(NativeBoxInput Input, ImmutableArray<KeyValuePair<int, string>>? Connector,
        WiredVariableTimeUtilities? Time, WiredVariableLevelSystem? Level, string[] Keys, int[] Subs)
    {
        public static NativeMetadata Decode(NativeBoxInput input)
        {
            var configuration = input.Configuration;

            if (input.Name == "wf_xtra_var_text_connector") {
                return new(input, WiredVariableMetadataBox.ParseConnector(configuration.Text).OrderBy(pair => pair.Key).ToImmutableArray(), null, null, [], []);
            }

            if (input.Name == "wf_xtra_var_time_util") {
                if (configuration.IntParams.Length != 2 || configuration.Text.Length != 0
                    || configuration.IntParams[1] is < 0 or > 2
                    || (configuration.IntParams[0] & WiredVariableTimeUtilities.ValidMask) != configuration.IntParams[0]) {
                    throw new ArgumentException("Invalid captured time metadata.");
                }

                return new(input, null, new(configuration.IntParams[0], configuration.IntParams[1]), null, [], []);
            }

            if (input.Name == "wf_xtra_var_lvlup_system") {
                if (!WiredVariableLevelSystem.TryParse(configuration.Text, out var level)) {
                    throw new ArgumentException("Invalid captured level metadata.");
                }

                var keys = new[] { "current_level", "current_xp", "level_progress", "level_progress_percent", "total_xp_required", "xp_remaining", "is_at_max", "max_level" };

                return new(input, null, null, level, keys, Enumerable.Range(0, 8).Where(sub => (level!.SubvariableMask & (1 << sub)) != 0).ToArray());
            }

            return new(input, null, null, null, [], []);
        }

        public long Read(long value, int sub) => Level!.Read(value, sub);
    }
}
