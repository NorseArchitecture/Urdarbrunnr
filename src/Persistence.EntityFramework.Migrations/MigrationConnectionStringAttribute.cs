namespace Norse.Persistence.EntityFramework.Migrations;

/// <summary>
///     Annotates an <see cref="EfMigrationContributor{TContext}" /> subclass with the Aspire connection
///     string name the source generator reads to emit <c>GetConnectionString(name)</c> and
///     <c>AddDbContext&lt;TContext&gt;</c> calls in the migrations service.
/// </summary>
/// <param name="connectionStringName">The Aspire resource / connection-string name.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MigrationConnectionStringAttribute(string connectionStringName) : Attribute
{
	/// <summary>Gets the Aspire connection string name supplied at construction.</summary>
	public string ConnectionStringName { get; } = connectionStringName;
}
