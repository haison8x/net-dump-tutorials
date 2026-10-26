namespace CrashLab;

public sealed class Customer(string name)
{
    public string Name { get; } = name;

    // BUG: the property calls itself instead of returning a field -> infinite recursion.
    public string DisplayName => string.IsNullOrEmpty(Name) ? "<unknown>" : DisplayName;
}
