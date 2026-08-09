using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.Persistence.EntityFramework;

sealed class RequireExplicitLengthConvention(bool applyFixedLength) : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(IConventionModelBuilder builder,
		IConventionContext<IConventionModelBuilder> context)
	{
		IList<string> violations = [];

		foreach (var property in builder.Metadata.GetEntityTypes().SelectMany(static t => t.GetProperties()))
		{
			if (property.DeclaringType is IConventionEntityType entityType && entityType.IsMappedToJson())
				continue;

			var storageType = property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
			if (storageType != typeof(string) && storageType != typeof(byte[]))
				continue;

			var maxLengthAttr =
				property.PropertyInfo?.GetCustomAttribute<MaxLengthAttribute>() ??
				property.PropertyInfo?.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
			if (maxLengthAttr is not null && maxLengthAttr.Length <= 0)
				property.Builder.HasMaxLength(maxLengthAttr.Length, fromDataAnnotation: true);

			// Fixed-length storage only pays off on SQL Server -- see FixedLengthAttribute's remarks.
			if (applyFixedLength && property.PropertyInfo?.GetCustomAttribute<FixedLengthAttribute>() is not null)
				property.Builder.IsFixedLength(true, fromDataAnnotation: true);

			if (property.GetMaxLength() is null)
				violations.Add($"{property.DeclaringType.ClrType.FullName}.{property.Name} ({storageType.Name})");
		}

		if (violations.Count == 0)
			return;

		throw new InvalidOperationException(
			$"{violations.Count} propert{(violations.Count == 1 ? "y has" : "ies have")} no explicit length declared. Decorate with [MaxLength(n)]/[FixedLength(n)], configure HasMaxLength(n) in the entity's Configure method, or declare HasMaxLength(-1) if truly unbounded:\n  - {string.Join("\n  - ", violations)}");
	}
}
