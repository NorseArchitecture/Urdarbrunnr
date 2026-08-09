using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Pii;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Wires <see cref="ProtectedPiiValueConverter{TPii}" /> onto every scalar property whose CLR type is
///     an <see cref="IPiiScalar{TSelf}" /> implementer. Call after <c>base.OnModelCreating</c>. One-time
///     model-build reflection — the sanctioned kind.
/// </summary>
public static class PiiProtectionModelExtensions
{
	/// <summary>Assigns the protecting converter to every direct PII scalar in the model.</summary>
	public static ModelBuilder ProtectPiiScalars(this ModelBuilder builder, IPersonalDataProtector protector)
	{
		ArgumentNullException.ThrowIfNull(protector);
		foreach (var entityType in builder.Model.GetEntityTypes())
			foreach (var property in entityType.GetProperties())
			{
				var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
				if (!typeof(IMaskedValue).IsAssignableFrom(clrType) || !clrType.IsValueType)
					continue;
				var converterType = typeof(ProtectedPiiValueConverter<>).MakeGenericType(clrType);
				property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType, protector)!);
			}

		return builder;
	}
}
