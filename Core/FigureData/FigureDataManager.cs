using System.Xml;
using Microsoft.Extensions.Logging;
using Plus.Core.FigureData.Types;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users.Clothing.Parts;
using Plus.Utilities;

namespace Plus.Core.FigureData;

public class FigureDataManager : IFigureDataManager
{
    private readonly ICatalogManager _catalogManager;
    private readonly ILogger<FigureDataManager> _logger;
    private readonly Dictionary<int, Palette> _palettes; //pallet id, Pallet

    private readonly List<string> _requirements;
    private readonly Dictionary<string, FigureSet> _setTypes; //type (hr, ch, etc), Set

    public FigureDataManager(ICatalogManager catalogManager, ILogger<FigureDataManager> logger)
    {
        _catalogManager = catalogManager;
        _logger = logger;
        _palettes = new();
        _setTypes = new();
        _requirements = new()
        {
            "hd",
            "ch",
            "lg"
        };
    }

    public void Init()
    {
        if (_palettes.Count > 0)
            _palettes.Clear();
        if (_setTypes.Count > 0)
            _setTypes.Clear();
        var projectSolutionPath = Directory.GetCurrentDirectory();
        var xDoc = new XmlDocument();
        xDoc.Load($"{projectSolutionPath}//Config//figuredata.xml");
        var colors = xDoc.GetElementsByTagName("colors");
        foreach (XmlNode node in colors)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                _palettes.Add(Convert.ToInt32(child.Attributes["id"].Value), new(Convert.ToInt32(child.Attributes["id"].Value)));
                foreach (XmlNode sub in child.ChildNodes)
                {
                    _palettes[Convert.ToInt32(child.Attributes["id"].Value)].Colors.Add(Convert.ToInt32(sub.Attributes["id"].Value),
                        new(Convert.ToInt32(sub.Attributes["id"].Value), Convert.ToInt32(sub.Attributes["index"].Value), Convert.ToInt32(sub.Attributes["club"].Value),
                            Convert.ToInt32(sub.Attributes["selectable"].Value) == 1, Convert.ToString(sub.InnerText)));
                }
            }
        }
        var sets = xDoc.GetElementsByTagName("sets");
        foreach (XmlNode node in sets)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                _setTypes.Add(child.Attributes["type"].Value, new(SetTypeUtility.GetSetType(child.Attributes["type"].Value), Convert.ToInt32(child.Attributes["paletteid"].Value)));
                foreach (XmlNode sub in child.ChildNodes)
                {
                    _setTypes[child.Attributes["type"].Value].Sets.Add(Convert.ToInt32(sub.Attributes["id"].Value),
                        new(Convert.ToInt32(sub.Attributes["id"].Value), Convert.ToString(sub.Attributes["gender"].Value), Convert.ToInt32(sub.Attributes["club"].Value),
                            Convert.ToInt32(sub.Attributes["colorable"].Value) == 1, Convert.ToInt32(sub.Attributes["selectable"].Value) == 1,
                            Convert.ToInt32(sub.Attributes["preselectable"].Value) == 1));
                    foreach (XmlNode subb in sub.ChildNodes)
                    {
                        if (subb.Attributes["type"] != null)
                        {
                            _setTypes[child.Attributes["type"].Value].Sets[Convert.ToInt32(sub.Attributes["id"].Value)].Parts.Add(
                                $"{Convert.ToInt32(subb.Attributes["id"].Value)}-{subb.Attributes["type"].Value}",
                                new(Convert.ToInt32(subb.Attributes["id"].Value), SetTypeUtility.GetSetType(child.Attributes["type"].Value),
                                    Convert.ToInt32(subb.Attributes["colorable"].Value) == 1, Convert.ToInt32(subb.Attributes["index"].Value), Convert.ToInt32(subb.Attributes["colorindex"].Value)));
                        }
                    }
                }
            }
        }

        //Faceless.
        _setTypes["hd"].Sets.Add(99999, new(99999, "U", 0, true, false, false));
        _logger.LogInformation("Loaded " + _palettes.Count + " Color Palettes");
        _logger.LogInformation("Loaded " + _setTypes.Count + " Set Types");
    }

    public string ProcessFigure(string figure, string gender, ICollection<ClothingParts> clothingParts, int clubLevel)
    {
        gender = gender.ToUpperInvariant();
        var rebuilt = new Dictionary<string, string>();
        var owned = clothingParts?.Select(part => part.PartId).ToHashSet();
        var purchased = owned == null ? new HashSet<int>() : _catalogManager.ClothingManager.GetClothingAllParts
            .SelectMany(part => part.PartIds).ToHashSet();
        foreach (var part in figure.ToLowerInvariant().Split('.'))
        {
            var pieces = part.Split('-');
            if (pieces.Length < 2 || !int.TryParse(pieces[1], out var id) || !_setTypes.TryGetValue(pieces[0], out var type)) continue;
            if (!type.Sets.TryGetValue(id, out var set)) continue;
            bool Allowed(Set candidate) => (candidate.Gender == gender || candidate.Gender == "U") && candidate.ClubLevel <= clubLevel &&
                (!purchased.Contains(candidate.Id) || owned!.Contains(candidate.Id));
            if (!Allowed(set)) set = type.Sets.Values.FirstOrDefault(candidate => candidate.Selectable && Allowed(candidate));
            if (set == null) continue;
            var color = pieces.Length > 2 && int.TryParse(pieces[2], out var c) ? c : 0;
            var second = pieces.Length > 3 && int.TryParse(pieces[3], out var c2) ? c2 : 0;
            int ValidateColor(int value) => _palettes.TryGetValue(type.PalletId, out var palette) &&
                palette.Colors.TryGetValue(value, out var entry) && entry.ClubLevel <= clubLevel ? value : GetRandomColor(type.PalletId, clubLevel);
            if (set.Colorable) { color = ValidateColor(color); if (pieces.Length > 3) second = ValidateColor(second); }
            else if (pieces[0] is not ("ca" or "wa")) color = 0;
            rebuilt[pieces[0]] = $"{pieces[0]}-{set.Id}-{color}" + (second != 0 ? $"-{second}" : "");
        }
        foreach (var requirement in _requirements)
        {
            if (rebuilt.ContainsKey(requirement) || requirement == "ch" && gender == "M" || !_setTypes.TryGetValue(requirement, out var type)) continue;
            var set = type.Sets.Values.FirstOrDefault(candidate => candidate.Selectable && (candidate.Gender == gender || candidate.Gender == "U") &&
                candidate.ClubLevel <= clubLevel && (!purchased.Contains(candidate.Id) || owned!.Contains(candidate.Id)));
            if (set != null) rebuilt[requirement] = $"{requirement}-{set.Id}-{GetRandomColor(type.PalletId, clubLevel)}";
        }
        return string.Join('.', rebuilt.Values) + (rebuilt.Count > 0 ? "." : "");
    }

    public Palette GetPalette(int colorId)
    {
        return _palettes.FirstOrDefault(x => x.Value.Colors.ContainsKey(colorId)).Value;
    }

    public bool TryGetPalette(int palletId, out Palette palette) => _palettes.TryGetValue(palletId, out palette);

    public int GetRandomColor(int palletId, int clubLevel = 0) => _palettes[palletId].Colors.Values.FirstOrDefault(color => color.Selectable && color.ClubLevel <= clubLevel)?.Id ?? 0;

    public string FilterFigure(string figure)
    {
        return StringCharFilter.IsValid(figure) ? figure : IFigureDataManager.DefaultFigure;
    }
}