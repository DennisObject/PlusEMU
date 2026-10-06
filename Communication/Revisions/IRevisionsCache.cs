namespace Plus.Communication.Revisions;

public interface IRevisionsCache
{
    IReadOnlyDictionary<string, Revision> Revisions
    {
        get; set;
    }
    Revision InternalRevision
    {
        get;
    }
}
