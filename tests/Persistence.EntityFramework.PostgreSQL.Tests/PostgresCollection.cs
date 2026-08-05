using System.Diagnostics.CodeAnalysis;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
/// The collection every test needing a real PostgreSQL server joins, so one container serves all of
/// them instead of one per class.
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage("Design", "CA1711:Identifiers should not have incorrect suffix",
	Justification = "xUnit collection fixture naming convention")]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
	public const string Name = "Postgres";
}
