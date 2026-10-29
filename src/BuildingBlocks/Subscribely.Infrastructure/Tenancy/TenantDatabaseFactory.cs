using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Subscribely.Infrastructure.Tenancy;

public sealed class TenantDatabaseOptions
{
    public const string Section = "TenantDatabase";

    /// <summary>Connection string com o placeholder <c>{slug}</c>, ex.: <c>Host=postgres;Database=subscribely_t_{slug};...</c>.</summary>
    public string ConnectionTemplate { get; set; } = string.Empty;
}

/// <summary>Monta a connection string do banco de um tenant a partir do template; valida o slug antes (anti-injeção).</summary>
public sealed partial class TenantDatabaseFactory(IOptions<TenantDatabaseOptions> options)
{
    private const string Placeholder = "{slug}";

    private readonly TenantDatabaseOptions _options = options.Value;

    public static bool IsValidSlug(string? slug) => slug is not null && SlugRegex().IsMatch(slug);

    public string ConnectionStringFor(string slug)
    {
        if (!IsValidSlug(slug))
        {
            throw new ArgumentException($"Invalid tenant slug '{slug}'.", nameof(slug));
        }

        if (!_options.ConnectionTemplate.Contains(Placeholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"TenantDatabase:ConnectionTemplate must contain the {Placeholder} placeholder.");
        }

        // Hífen vira underscore: nomes de banco ficam sem necessidade de aspas.
        return _options.ConnectionTemplate.Replace(Placeholder, slug.Replace('-', '_'), StringComparison.Ordinal);
    }

    public string DatabaseNameFor(string slug) =>
        new NpgsqlConnectionStringBuilder(ConnectionStringFor(slug)).Database
        ?? throw new InvalidOperationException("TenantDatabase:ConnectionTemplate must define Database.");

    [GeneratedRegex("^[a-z0-9-]{3,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
