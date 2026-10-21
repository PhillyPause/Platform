using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace ManyHands.Data;

/// <summary>
/// Data Protection's key ring, in Postgres. Without it the keys live in the
/// container, and a deploy replaces the container: every sign-in cookie and
/// every half-filled form would stop working on each merge to main.
///
/// The interface is synchronous, so these calls are too. The ring is read once
/// and cached, and a key is written every few months, so nothing waits on this
/// in a request.
/// </summary>
public sealed class DataProtectionKeyStore(NpgsqlDataSource db) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var conn = db.OpenConnection();
        return conn.Query<string>("SELECT xml FROM data_protection_keys ORDER BY created_at")
            .Select(XElement.Parse)
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var conn = db.OpenConnection();
        conn.Execute(
            """
            INSERT INTO data_protection_keys (friendly_name, xml) VALUES (@friendlyName, @xml)
            ON CONFLICT (friendly_name) DO UPDATE SET xml = EXCLUDED.xml
            """,
            new { friendlyName, xml = element.ToString(SaveOptions.DisableFormatting) });
    }
}
