using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.Persistence.EntityFramework;

sealed class RequireEntityConfigurationConvention : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
	{
		List<string> violations = [];

		foreach (var entityType in builder.Metadata.GetEntityTypes())
		{
			if (entityType.IsMappedToJson())
				continue;

			var clrType = entityType.ClrType;
			var implementsSelf = clrType.GetInterfaces().Any(i =>
				i.IsGenericType &&
				i.GetGenericTypeDefinition() == typeof(INorseEntity<>) &&
				i.GetGenericArguments()[0] == clrType);

			if (!implementsSelf)
				violations.Add(clrType.FullName!);
		}

		if (violations.Count == 0)
			return;

		throw new InvalidOperationException(
			$"{violations.Count} entit{(violations.Count == 1 ? "y does" : "ies do")} not implement " +
			"INorseEntity<TSelf>. Every Norse entity is its own configuration:\n  - " +
			string.Join("\n  - ", violations));
	}
}
