namespace Plus.HabboHotel.Navigator;

public class SearchResultList
{
    public SearchResultList(int id, string category, string categoryIdentifier, string publicName, bool canDoActions, int colour, string requiredPermission, NavigatorViewMode viewMode, string categoryType,
        string searchAllowance, int orderId)
    {
        Id = id;
        Category = category;
        CategoryIdentifier = categoryIdentifier;
        PublicName = publicName;
        CanDoActions = canDoActions;
        Colour = colour;
        RequiredPermission = requiredPermission;
        ViewMode = viewMode;
        CategoryType = NavigatorCategoryTypeUtility.GetCategoryTypeByString(categoryType);
        SearchAllowance = NavigatorSearchAllowanceUtility.GetSearchAllowanceByString(searchAllowance);
        OrderId = orderId;
    }

    public int Id { get; set; }

    //TODO: Make an enum?
    public string Category { get; set; }

    public string CategoryIdentifier { get; set; }

    public string PublicName { get; set; }

    public bool CanDoActions { get; set; }

    public int Colour { get; set; }

    public string RequiredPermission { get; set; }

    public NavigatorViewMode ViewMode { get; set; }

    public NavigatorCategoryType CategoryType { get; set; }

    public NavigatorSearchAllowance SearchAllowance { get; set; }

    public int OrderId { get; set; }
}