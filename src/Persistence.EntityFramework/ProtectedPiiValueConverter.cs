using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives;
using Norse.Primitives.Pii;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The one composed converter for a struct-typed PII scalar: canonical wire string ∘ protect on
/// write; unprotect ∘ parse on read. There is no converter ordering problem because there is no
/// second converter — the protector composes inside this one. The captured
/// <see cref="IPersonalDataProtector"/> must be a singleton over singleton seam dependencies: EF
/// caches the model per context type, so the first-resolved instance serves every request
/// (Identity's own <c>PersonalDataConverter</c> capture pattern).
/// </summary>
sealed class ProtectedPiiValueConverter<TPii>(IPersonalDataProtector protector) :
	ValueConverter<TPii, string>(
		pii => protector.Protect(pii.WireValue),
		stored => FromStore(protector, stored))
	where TPii : struct, IPiiScalar<TPii>
{
	static TPii FromStore(IPersonalDataProtector protector, string stored)
	{
		var wire = protector.Unprotect(stored);
		if (TPii.Parse(wire).TryGetValue(out Success<TPii> success))
			return success.Value;
		throw new InvalidOperationException($"Decrypted {typeof(TPii).Name} no longer parses — storage corruption; failing loudly.");
	}
}
