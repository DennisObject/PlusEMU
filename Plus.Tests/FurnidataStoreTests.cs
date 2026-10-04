using System.Text.Json;
using Microsoft.Extensions.Options;
using Plus.HabboHotel.Items.Editor;
using Xunit;

namespace Plus.Tests;

// Works on a copy in a temp directory; never on the hotel's furnidata.
public sealed class FurnidataStoreTests : IDisposable
{
    private const string Furnidata = """
        {
          "roomitemtypes": {
            "furnitype": [
              {
                "id": 13,
                "classname": "shelves_norja",
                "name": "Beige Bookcase",
                "description": "For nic naks and books.",
                "xdim": 1,
                "ydim": 1,
                "canstandon": false,
                "price": 1.50
              },
              {
                "id": 14,
                "classname": "rare_dragonlamp*1",
                "name": "Fire Dragon Lamp",
                "description": "Ölampe & <fire>",
                "xdim": 1,
                "ydim": 1
              }
            ]
          },
          "wallitemtypes": {
            "furnitype": [
              {
                "id": 4001,
                "classname": "poster",
                "name": "Poster",
                "description": ""
              }
            ]
          }
        }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("furnidata-tests-").FullName;
    private readonly string _path;

    public FurnidataStoreTests()
    {
        _path = Path.Combine(_directory, "FurnitureData.json");
        File.WriteAllText(_path, Furnidata);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private FurnidataStore Store(string? path = null) => new(Options.Create(new FurniEditorConfiguration { FurnidataPath = path ?? _path }));

    [Fact]
    public void LooksUpByClassnameThenStrippedClassnameThenSpriteId()
    {
        var store = Store();
        Assert.Contains("matched_classname", store.Lookup("SHELVES_NORJA", 0).DiagnosticJson);
        Assert.Equal("Beige Bookcase", JsonDocument.Parse(store.Lookup("shelves_norja", 0).EntryJson).RootElement.GetProperty("name").GetString());
        Assert.Contains("matched_classname_stripped", store.Lookup("shelves_norja*3", 0).DiagnosticJson);
        Assert.Contains("matched_id", store.Lookup("unknown", 4001).DiagnosticJson);
        var missing = store.Lookup("unknown", 1);
        Assert.Equal("{}", missing.EntryJson);
        Assert.Contains("not_found", missing.DiagnosticJson);
    }

    private static FurnidataTarget Floor(string classname, int id) => new(classname, id, false);

    private static FurnidataTarget Wall(string classname, int id) => new(classname, id, true);

    [Fact]
    public void DiagnosticsNameTheFileButNeverTheServerPath()
    {
        using var diagnostic = JsonDocument.Parse(Store().Lookup("shelves_norja", 0).DiagnosticJson);
        Assert.Equal("FurnitureData.json", diagnostic.RootElement.GetProperty("sourcePath").GetString());
        Assert.DoesNotContain(_directory, Store().Lookup("x", 0).DiagnosticJson);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/FurnitureData.json")]
    [InlineData("/tmp/does-not-exist/FurnitureData.json")]
    [InlineData("/etc/passwd")]
    public void FailsSafelyWithoutAUsableSource(string path)
    {
        var store = Store(path);
        var lookup = store.Lookup("shelves_norja", 13);
        Assert.Equal("{}", lookup.EntryJson);
        Assert.Contains("source_missing", lookup.DiagnosticJson);
        Assert.DoesNotContain("/tmp/", lookup.DiagnosticJson);
        Assert.Throws<FurnidataException>(() => store.Edit(Floor("shelves_norja", 13), entry => entry["name"] = "x"));
    }

    [Fact]
    public void EditWritesOnlyThatEntryKeepsFormattingAndABackup()
    {
        var store = Store();
        var edit = store.Edit(Floor("shelves_norja", 13), entry => entry["name"] = "Pine Bookcase");

        Assert.True(edit.Changed);
        Assert.Equal((false, 13, "shelves_norja", "Pine Bookcase"), (edit.IsWallItem, edit.Id, edit.Classname, edit.Name));
        Assert.Equal(Furnidata, File.ReadAllText(_path + ".bak"));
        var written = File.ReadAllText(_path);
        Assert.Equal(Furnidata.Replace("\"Beige Bookcase\"", "\"Pine Bookcase\""), written);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
        Assert.Contains("Pine Bookcase", store.Lookup("shelves_norja", 0).EntryJson);
    }

    [Fact]
    public void RestorePutsTheLoggedEntryBackOnlyWhileItIsTheLoggedAfterImage()
    {
        var store = Store();
        var first = store.Edit(Wall("poster", 4001), entry => entry["description"] = "A poster");
        Assert.True(first.IsWallItem);
        var second = store.Edit(Wall("poster", 4001), entry => entry["name"] = "Big poster");

        var conflict = Assert.Throws<FurnidataException>(() => store.Restore(Wall("poster", 4001), first.After, first.Before));
        Assert.Equal("The furnidata entry changed since that edit; revert refused", conflict.Message);
        Assert.Contains("\"Big poster\"", File.ReadAllText(_path));

        store.Restore(Wall("poster", 4001), second.After, second.Before);
        var revert = store.Restore(Wall("poster", 4001), first.After, first.Before);
        Assert.Equal(first.Before, revert.After);
        Assert.Equal(Furnidata, File.ReadAllText(_path));
    }

    [Fact]
    public void EntriesSharingAClassnameAreToldApartBySpriteIdWithinTheItemsSection()
    {
        File.WriteAllText(_path, """{"roomitemtypes":{"furnitype":[{"id":50,"classname":"dup","name":"a"},{"id":51,"classname":"dup","name":"b"}]},"wallitemtypes":{"furnitype":[{"id":52,"classname":"dup","name":"c"}]}}""");
        var store = Store();

        Assert.Equal(51, store.Edit(Floor("dup", 51), entry => entry["name"] = "b2").Id);
        Assert.Equal("Several furnidata entries share this classname and sprite id", Assert.Throws<FurnidataException>(() => store.Edit(Floor("dup", 99), entry => entry["name"] = "x")).Message);
        Assert.Equal(52, store.Edit(Wall("dup", 99), entry => entry["name"] = "c2").Id);
        Assert.Throws<FurnidataException>(() => store.Edit(Floor("poster", 4001), entry => entry["name"] = "x"));
        Assert.Contains("\"name\":\"a\"", File.ReadAllText(_path));
    }

    [Fact]
    public void RefusesUnknownClassnamesRenamesAndNoOpWrites()
    {
        var store = Store();
        Assert.Throws<FurnidataException>(() => store.Edit(Floor("missing", 1), entry => entry["name"] = "x"));
        Assert.Throws<FurnidataException>(() => store.Edit(Floor("", 1), entry => entry["name"] = "x"));
        Assert.Throws<FurnidataException>(() => store.Edit(Wall("poster", 4001), entry => entry["classname"] = "other"));
        Assert.Throws<FurnidataException>(() => store.Edit(Wall("poster", 4001), entry => entry["id"] = 9));
        Assert.Throws<FurnidataException>(() => store.Restore(Wall("poster", 4001), store.Edit(Wall("poster", 4001), _ => { }).Before, "[1]"));
        var noOp = store.Edit(Wall("poster", 4001), entry => entry["name"] = "Poster");
        Assert.False(noOp.Changed);
        Assert.False(File.Exists(_path + ".bak"));
    }

    [Fact]
    public void FollowsASymlinkSoTheLinkSurvives()
    {
        var link = Path.Combine(_directory, "linked.json");
        File.CreateSymbolicLink(link, _path);

        Store(link).Edit(Floor("shelves_norja", 13), entry => entry["name"] = "Linked");

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Contains("\"Linked\"", File.ReadAllText(_path));
    }

    [Fact]
    public void RefusesFilesAboveTheSizeLimit()
    {
        var store = new FurnidataStore(Options.Create(new FurniEditorConfiguration { FurnidataPath = _path, FurnidataMaxBytes = 100 }));
        Assert.Contains("error", store.Lookup("poster", 0).DiagnosticJson);
        Assert.Throws<FurnidataException>(() => store.Edit(Wall("poster", 4001), entry => entry["name"] = "x"));
    }
}
