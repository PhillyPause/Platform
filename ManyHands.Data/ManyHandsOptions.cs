namespace ManyHands.Data;

/// <summary>
/// The <c>ManyHands</c> configuration section. Off by default, so the site
/// builds, tests and runs with no database at all; switching it on is what
/// needs <c>ConnectionStrings:ManyHands</c>.
/// </summary>
public sealed class ManyHandsOptions
{
    public const string SectionName = "ManyHands";
    public const string ConnectionStringName = "ManyHands";

    public bool Enabled { get; set; }

    /// <summary>
    /// Runs the <c>_seed_</c> scripts: fictional volunteers and activities for
    /// local development. Never set in production.
    /// </summary>
    public bool Seed { get; set; }

    /// <summary>
    /// Addresses that are admins the moment they sign in. How the first admin
    /// exists at all; after that, admins make each other on the members page.
    /// </summary>
    public string[] Admins { get; set; } = [];
}
