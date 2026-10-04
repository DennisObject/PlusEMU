namespace Plus.Communication.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RequiresPermissionAttribute(params string[] permissions) : Attribute
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}
