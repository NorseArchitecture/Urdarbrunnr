using System.Diagnostics.CodeAnalysis;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

/// <summary>
///     The collection every test needing a real SQL Server joins, so one (slow-starting) container serves all
///     of them instead of one per class.
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage("Design", "CA1711:Identifiers should not have incorrect suffix",
	Justification = "xUnit collection fixture naming convention")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
	public const string Name = "SqlServer";
}
