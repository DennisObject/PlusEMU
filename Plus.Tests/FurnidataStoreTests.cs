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
        Assert.Throws<FurnidataException>(() => store.Edit("shelves_norja", entry => entry["name"] = "x"));
    }

    [Fact]
    public void EditWritesOnlyThatEntryKeepsFormattingAndABackup()
    {
        var store = Store();
        var edit = store.Edit("shelves_norja", entry => entry["name"] = "Pine Bookcase");

        Assert.True(edit.Changed);
        Assert.Equal((false, 13, "shelves_norja", "Pine Bookcase"), (edit.IsWallItem, edit.Id, edit.Classname, edit.Name));
        Assert.Equal(Furnidata, File.ReadAllText(_path + ".bak"));
        var written = File.ReadAllText(_path);
        Assert.Equal(Furnidata.Replace("\"Beige Bookcase\"", "\"Pine Bookcase\""), written);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
        Assert.Contains("Pine Bookcase", store.Lookup("shelves_norja", 0).EntryJson);
    }

    [Fact]
    public void ReplacePutsTheLoggedEntryBack()
    {
        var store = Store();
        var edit = store.Edit("poster", entry => entry["description"] = "A poster");
        Assert.True(edit.IsWallItem);

        var revert = store.Replace("poster", edit.Before);

        Assert.Equal(edit.After, revert.Before);
        Assert.Equal(edit.Before, revert.After);
        Assert.Equal(Furnidata, File.ReadAllText(_path));
    }

    [Fact]
    public void RefusesUnknownClassnamesRenamesAndNoOpWrites()
    {
        var store = Store();
        Assert.Throws<FurnidataException>(() => store.Edit("missing", entry => entry["name"] = "x"));
        Assert.Throws<FurnidataException>(() => store.Edit("", entry => entry["name"] = "x"));
        Assert.Throws<FurnidataException>(() => store.Edit("poster", entry => entry["classname"] = "other"));
        Assert.Throws<FurnidataException>(() => store.Replace("poster", "[1]"));
        var noOp = store.Edit("poster", entry => entry["name"] = "Poster");
        Assert.False(noOp.Changed);
        Assert.False(File.Exists(_path + ".bak"));
    }

    [Fact]
    public void FollowsASymlinkSoTheLinkSurvives()
    {
        var link = Path.Combine(_directory, "linked.json");
        File.CreateSymbolicLink(link, _path);

        Store(link).Edit("shelves_norja", entry => entry["name"] = "Linked");

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Contains("\"Linked\"", File.ReadAllText(_path));
    }

    [Fact]
    public void RefusesFilesAboveTheSizeLimit()
    {
        var store = new FurnidataStore(Options.Create(new FurniEditorConfiguration { FurnidataPath = _path, FurnidataMaxBytes = 100 }));
        Assert.Contains("error", store.Lookup("poster", 0).DiagnosticJson);
        Assert.Throws<FurnidataException>(() => store.Edit("poster", entry => entry["name"] = "x"));
    }
}
