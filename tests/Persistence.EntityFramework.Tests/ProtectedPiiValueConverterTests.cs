using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.Primitives.Pii;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class ProtectedPiiValueConverterTests
{
	// Deterministic fake: "P:" prefix marks protected payloads.
	sealed class FakeProtector : IPersonalDataProtector
	{
		public string? Protect(string? data) => data is null ? null : $"P:{data}";
		public string? Unprotect(string? data) => data is not null && data.StartsWith("P:", StringComparison.Ordinal) ?
			data[2..] :
			throw new InvalidOperationException("Not protected.");
	}

	// A distinct, unrelated exception type — never InvalidOperationException — so a test asserting
	// pass-through can't accidentally pass against the converter's own parse-failure exception.
	sealed class SentinelException : Exception;

	// A protector whose Unprotect always fails with something other than unparseable data, standing
	// in for a real subject-keyed protector's own exception types (e.g. a future KeyDestroyedException).
	sealed class ThrowingProtector : IPersonalDataProtector
	{
		public string? Protect(string? data) => data;
		public string? Unprotect(string? data) => throw new SentinelException();
	}

	[Fact]
	void To_provider_protects_the_wire_value()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new FakeProtector());
		EmailAddress.TryParse("buvy@example.com", out var email).ShouldBeTrue();
		converter.ConvertToProvider(email).ShouldBe("P:buvy@example.com");
	}

	[Fact]
	void From_provider_unprotects_and_parses_the_wire_value()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new FakeProtector());
		var email = (EmailAddress)converter.ConvertFromProvider("P:buvy@example.com")!;
		email.WireValue.ShouldBe("buvy@example.com");
	}

	[Fact]
	void From_provider_throws_loudly_when_decrypted_data_no_longer_parses()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new FakeProtector());
		Should.Throw<InvalidOperationException>(() => converter.ConvertFromProvider("P:not-an-email"));
	}

	[Fact]
	void From_provider_lets_a_non_parse_protector_exception_pass_through_undistorted()
	{
		ProtectedPiiValueConverter<EmailAddress> converter = new(new ThrowingProtector());
		Should.Throw<SentinelException>(() => converter.ConvertFromProvider("anything"));
	}

	[Fact]
	void Protect_pii_scalars_assigns_the_converter_to_direct_pii_properties()
	{
		// Model-level: a minimal context whose entity carries an EmailAddress scalar. This project
		// has no Microsoft.EntityFrameworkCore.InMemory reference, so model inspection rides Sqlite
		// in-memory — the same approach every other model-level test here uses (see
		// SequentialGuidConversionWiringTests/NorseDbContextTests).
		var options = new DbContextOptionsBuilder<PiiFixtureContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;
		using PiiFixtureContext context = new(options, new FakeProtector());
		var property = context.Model.FindEntityType(typeof(PiiFixtureEntity))!.FindProperty(nameof(PiiFixtureEntity.Email))!;
		property.GetValueConverter().ShouldBeOfType<ProtectedPiiValueConverter<EmailAddress>>();

		// Nullable branch: ProtectPiiScalars unwraps Nullable<T> before checking IMaskedValue, so an
		// EmailAddress? property must get the same converter as a non-nullable EmailAddress property.
		var secondaryEmailProperty = context.Model.FindEntityType(typeof(PiiFixtureEntity))!.FindProperty(nameof(PiiFixtureEntity.SecondaryEmail))!;
		secondaryEmailProperty.GetValueConverter().ShouldBeOfType<ProtectedPiiValueConverter<EmailAddress>>();

		// Negative case: a plain non-PII scalar must never receive a PII converter — proves the guard
		// has a working false side, not just a working true side.
		var idProperty = context.Model.FindEntityType(typeof(PiiFixtureEntity))!.FindProperty(nameof(PiiFixtureEntity.Id))!;
		idProperty.GetValueConverter().ShouldBeNull();
	}

	sealed record PiiFixtureEntity(
		Guid Id,
		[property: MaxLength(400)][property: RetentionPolicy(RetentionBasis.SubjectKey)] EmailAddress Email,
		[property: MaxLength(400)][property: RetentionPolicy(RetentionBasis.SubjectKey)] EmailAddress? SecondaryEmail) :
		NorseEntityBase<PiiFixtureEntity>, INorseEntity<PiiFixtureEntity>
	{
		// EmailAddress carries no default EF type mapping, so without this explicit .Property call
		// EF's convention-based discovery never creates a model Property for it at all (there is no
		// generator running in this test project to invoke Configure automatically the way a real
		// consumer's ConfigureNorseEntities override would) — leaving nothing for
		// ProtectPiiScalars to find or attach a converter to.
		public static void Configure(EntityTypeBuilder<PiiFixtureEntity> builder)
		{
			builder.HasKey(e => e.Id);
			builder.Property(e => e.Email);
			builder.Property(e => e.SecondaryEmail);
		}
	}

	sealed class PiiFixtureContext(DbContextOptions<PiiFixtureContext> options, IPersonalDataProtector protector) :
		NorseDbContext(options)
	{
		public DbSet<PiiFixtureEntity> Entities => Set<PiiFixtureEntity>();

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);
			PiiFixtureEntity.Configure(modelBuilder.Entity<PiiFixtureEntity>());
			modelBuilder.ProtectPiiScalars(protector);
		}
	}
}
