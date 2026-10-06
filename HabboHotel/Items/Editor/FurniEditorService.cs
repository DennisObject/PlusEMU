using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.Database;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Editor;

// ItemId is the furniture the request named (0 when it named none), so the editor can match the refusal to it.
public sealed class FurniEditorRejected(string message, uint itemId = 0) : Exception(message)
{
    public uint ItemId { get; } = itemId;
}

public interface IFurniEditorService
{
    // Reads throw FurniEditorRejected when the actor may not use the editor or the furniture does not exist.
    FurniEditorSearchResult Search(Habbo actor, string query, string type, int page, string sortField, string sortDirection);
    FurniEditorDetail Detail(Habbo actor, uint id);
    FurniEditorDetail DetailBySprite(Habbo actor, int spriteId);
    IReadOnlyList<string> Interactions(Habbo actor);

    FurniEditorResult Update(Habbo actor, uint id, string json);
    FurniEditorResult Delete(Habbo actor, uint id);
    FurniEditorResult UpdateFurnidata(Habbo actor, uint id, string json);
    FurniEditorResult RevertFurnidata(Habbo actor, uint id);
    Task<FurniEditorImportResult> ImportText(Habbo actor, uint id);
}

public sealed class FurniEditorService : IFurniEditorService
{
    private static readonly TimeSpan FurnidataCooldown = TimeSpan.FromSeconds(1);
    private static readonly string[] TextFields = ["name", "description"];

    private readonly IDatabase _database;
    private readonly IFurnidataStore _furnidata;
    private readonly IFurniEditorTextImporter _importer;
    private readonly ICatalogCacheRefresher _refresher;
    private readonly IGameClientManager _gameClientManager;
    private readonly ILogger<FurniEditorService> _logger;
    private readonly ConcurrentDictionary<int, DateTimeOffset> _lastFurnidataEdit = new();
    private readonly object _sync = new();
    private readonly TimeProvider _clock;

    public FurniEditorService(IDatabase database, IFurnidataStore furnidata, IFurniEditorTextImporter importer, ICatalogCacheRefresher refresher,
        IGameClientManager gameClientManager, ILogger<FurniEditorService> logger, TimeProvider clock)
    {
        _database = database;
        _furnidata = furnidata;
        _importer = importer;
        _refresher = refresher;
        _gameClientManager = gameClientManager;
        _logger = logger;
        _clock = clock;
    }

    public FurniEditorSearchResult Search(Habbo actor, string query, string type, int page, string sortField, string sortDirection)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();

        return new FurniEditorRepository(connection).Search(query, type, page, sortField, sortDirection);
    }

    public FurniEditorDetail Detail(Habbo actor, uint id)
    {
        RequireEditor(actor, id);
        using var connection = _database.Connection();
        var repository = new FurniEditorRepository(connection);
        var item = repository.Item(id) ?? throw new FurniEditorRejected($"Item not found: {id}", id);

        // Only offers on pages the actor may open are shown; delete still checks every reference.
        return new(item, repository.UsageCount(id), repository.CatalogRefs(id, actor.Access), _furnidata.Lookup(item.ItemName, item.SpriteId));
    }

    public FurniEditorDetail DetailBySprite(Habbo actor, int spriteId)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();
        var id = new FurniEditorRepository(connection).ItemBySprite(spriteId) ?? throw new FurniEditorRejected($"No item uses sprite id {spriteId}");

        return Detail(actor, id);
    }

    public IReadOnlyList<string> Interactions(Habbo actor)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();

        return new FurniEditorRepository(connection).InteractionTypes().Select(type => type.ToLowerInvariant())
            .Concat(WiredBoxRegistry.All.Select(descriptor => descriptor.CanonicalName.ToLowerInvariant()))
            .Where(KnownInteraction).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    public FurniEditorResult Update(Habbo actor, uint id, string json)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true)
        {
            return Denied(id);
        }

        lock (_sync)
        {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var repository = new FurniEditorRepository(connection, transaction);

            if (repository.Item(id) is not { } current)
            {
                return NotFound(id);
            }

            var (changes, error) = FurniEditorUpdatePayload.Validate(json, current, KnownInteraction);

            if (error != null)
            {
                return new(false, error, id);
            }

            if (changes.Count == 0)
            {
                return new(true, "No changes", id);
            }

            repository.Update(id, changes);
            repository.Log(actor.Id, actor.Username, "update", id, current.ItemName,
                Json(changes.ToDictionary(change => change.Field, change => change.Before)), Json(changes.ToDictionary(change => change.Field, change => change.Value)));
            transaction.Commit();
            _logger.LogInformation("Furni editor: {User} updated furniture #{Id} ({Fields})", actor.Username, id, string.Join(", ", changes.Select(change => change.Field)));
        }

        _refresher.Schedule(reloadItems: true);

        return new(true, "Item updated", id);
    }

    public FurniEditorResult Delete(Habbo actor, uint id)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true || !actor.Access.Can(PermissionKeys.FurniDelete))
        {
            return Denied(id);
        }

        lock (_sync)
        {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var repository = new FurniEditorRepository(connection, transaction);

            // The row lock makes a concurrent catalog save that uses this furniture wait, then see it gone.
            if (repository.Item(id, forUpdate: true) is not { } current)
            {
                return NotFound(id);
            }

            if (repository.References(id) is { Count: > 0 } references)
            {
                return new(false, $"Cannot delete: still used by {string.Join(", ", references)}", id);
            }

            repository.Delete(id);
            repository.Log(actor.Id, actor.Username, "delete", id, current.ItemName, Json(current), null);
            transaction.Commit();
            _logger.LogInformation("Furni editor: {User} deleted furniture #{Id} ({Classname})", actor.Username, id, current.ItemName);
        }

        _refresher.Schedule(reloadItems: true);

        return new(true, "Item deleted", id);
    }

    public FurniEditorResult UpdateFurnidata(Habbo actor, uint id, string json)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true || !actor.Access.Can(PermissionKeys.FurniEdit))
        {
            return Denied(id);
        }

        if (!TakeFurnidataTurn(actor))
        {
            return new(false, "Too many requests", id);
        }

        var (payload, error) = FurnidataEditPayload.Parse(json);

        if (payload == null)
        {
            return new(false, error!, id);
        }

        return WriteFurnidata(actor, id, "furnidata_update", (repository, item) =>
            (_furnidata.Edit(new FurnidataTarget(item.ItemName, item.SpriteId, item.Type != "s"), entry => Apply(entry, payload)), null));
    }

    public FurniEditorResult RevertFurnidata(Habbo actor, uint id)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true || !actor.Access.Can(PermissionKeys.FurniEdit))
        {
            return Denied(id);
        }

        if (!TakeFurnidataTurn(actor))
        {
            return new(false, "Too many requests", id);
        }

        // Only while the entry still holds exactly what that edit wrote: anything later (another furniture sharing
        // the entry, a manual edit) would otherwise be erased.
        return WriteFurnidata(actor, id, "furnidata_revert", (repository, _) =>
        {
            var last = repository.LastFurnidataEdit(id) ?? throw new FurnidataException("Nothing to revert");
            var target = new FurnidataTarget(last.Classname, last.EntryId, last.EntrySection == FurniEditorRepository.WallSection);

            return (_furnidata.Restore(target, last.AfterJson, last.BeforeJson), last.Id);
        });
    }

    public async Task<FurniEditorImportResult> ImportText(Habbo actor, uint id)
    {
        RequireEditor(actor, id);

        if (!_importer.IsConfigured)
        {
            throw new FurniEditorRejected("Import from Habbo is not configured", id);
        }

        string classname;

        using (var connection = _database.Connection())
        {
            classname = new FurniEditorRepository(connection).Item(id)?.ItemName ?? throw new FurniEditorRejected($"Item not found: {id}", id);
        }

        return await _importer.Find(classname) ?? throw new FurniEditorRejected("Import from Habbo is unavailable right now", id);
    }

    // The audit row is written in the same transaction as the public name mirror; if that fails after the file
    // was written, the entry is put back so the file never holds an unaudited change.
    private FurniEditorResult WriteFurnidata(Habbo actor, uint id, string action, Func<FurniEditorRepository, FurniEditorItem, (FurnidataEdit Edit, int? RevertedLogId)> write)
    {
        FurnidataEdit edit;

        lock (_sync)
        {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var repository = new FurniEditorRepository(connection, transaction);

            if (repository.Item(id) is not { } item)
            {
                return NotFound(id);
            }

            int? revertedLogId;

            try
            {
                (edit, revertedLogId) = write(repository, item);
            }
            catch (FurnidataException e)
            {
                return new(false, e.Message, id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // IO messages carry server paths; the log keeps them, the editor gets a plain sentence.
                _logger.LogError(e, "Furni editor: furnidata write for furniture #{Id} failed", id);

                return new(false, "The furnidata file could not be written", id);
            }

            if (!edit.Changed && revertedLogId == null)
            {
                return new(true, "No changes", id);
            }

            try
            {
                if (revertedLogId is { } logId)
                {
                    repository.MarkReverted(logId);
                }

                repository.Log(actor.Id, actor.Username, action, id, edit.Classname, edit.Before, edit.After,
                    edit.Id, edit.IsWallItem ? FurniEditorRepository.WallSection : FurniEditorRepository.FloorSection);
                var name = edit.Name.Length > 56 ? edit.Name[..56] : edit.Name;

                if (name != item.PublicName && TextChanged(edit))
                {
                    repository.SetPublicName(id, name);
                }

                transaction.Commit();
            }
            catch
            {
                if (edit.Changed)
                {
                    _furnidata.Restore(new FurnidataTarget(edit.Classname, edit.Id, edit.IsWallItem), edit.After, edit.Before);
                }

                throw;
            }

            _logger.LogInformation("Furni editor: {User} {Action} for {Classname} (furniture #{Id})", actor.Username, action, edit.Classname, id);
            // Sent before the lock is released, so clients get furnidata changes in the order they were written.
            Broadcast(edit);
        }

        _refresher.Schedule(reloadItems: true);

        return new(true, action == "furnidata_revert" ? "Furnidata reverted" : "Furnidata updated", id);
    }

    private static void Apply(JsonObject entry, FurnidataEditPayload payload)
    {
        if (payload.Name != null)
        {
            entry["name"] = payload.Name;
        }

        if (payload.Description != null)
        {
            entry["description"] = payload.Description;
        }

        foreach (var (key, value) in payload.Structure)
        {
            if (!entry.ContainsKey(key))
            {
                throw new FurnidataException($"The furnidata entry has no {key}");
            }

            entry[key] = value.DeepClone();
        }
    }

    // Names and descriptions travel as a delta; any other change needs clients to reload the furnidata.
    private void Broadcast(FurnidataEdit edit)
    {
        if (!edit.Changed)
        {
            return;
        }

        bool onlyText = WithoutText(edit.Before) == WithoutText(edit.After);
        var composer = onlyText
            ? new FurnitureDataReloadComposer(FurnitureDataReloadComposer.Delta, [edit])
            : new FurnitureDataReloadComposer(FurnitureDataReloadComposer.ReloadHint, []);

        // Only Octane revisions map this packet; sending it to other clients would fail on the missing id.
        foreach (var client in _gameClientManager.GetClients.ToList())
        {
            if (client?.GetHabbo() != null && client.Revision?.InternalIdToOutgoingIdMapping.ContainsKey(composer.MessageId) == true)
            {
                client.Send(composer);
            }
        }
    }

    private static bool TextChanged(FurnidataEdit edit)
    {
        var before = JsonNode.Parse(edit.Before)?["name"]?.ToJsonString();
        var after = JsonNode.Parse(edit.After)?["name"]?.ToJsonString();

        return before != after;
    }

    private static string WithoutText(string entryJson)
    {
        var entry = (JsonObject)JsonNode.Parse(entryJson)!;

        foreach (var field in TextFields)
        {
            entry.Remove(field);
        }

        return entry.ToJsonString();
    }

    private bool TakeFurnidataTurn(Habbo actor)
    {
        lock (_sync)
        {
            var now = _clock.GetUtcNow();

            if (_lastFurnidataEdit.TryGetValue(actor.Id, out var last) && now - last < FurnidataCooldown)
            {
                return false;
            }

            _lastFurnidataEdit[actor.Id] = now;

            return true;
        }
    }

    internal static bool KnownInteraction(string type) => type == "default" || InteractionTypes.GetTypeFromString(type) != InteractionType.None;

    private static void RequireEditor(Habbo actor, uint itemId = 0)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true)
        {
            throw new FurniEditorRejected("No permission", itemId);
        }
    }

    private static FurniEditorResult Denied(uint itemId) => new(false, "No permission", itemId);

    private static FurniEditorResult NotFound(uint itemId) => new(false, $"Item not found: {itemId}", itemId);

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
