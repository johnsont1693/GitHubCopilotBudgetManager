namespace BudgetManager.Infrastructure.Identity;

public sealed record GraphDirectoryOptions
{
    public GraphDirectoryOptions(string githubLoginProperty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(githubLoginProperty);
        if (!githubLoginProperty.All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                "Graph GitHub login property may contain only letters, numbers, and underscores.",
                nameof(githubLoginProperty));
        }

        GitHubLoginProperty = githubLoginProperty;
    }

    public string GitHubLoginProperty { get; }
}
