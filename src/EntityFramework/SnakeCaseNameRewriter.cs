using System.Globalization;
using System.Text;

namespace Norse.EntityFramework;

/// <summary>
/// Rewrites an identifier (table name, column name, constraint name, ...) to snake_case. Ported from
/// prior art's Unicode-category-aware rewrite algorithm — handles acronym runs, embedded digits, and
/// pre-existing underscores correctly, unlike a naive case-boundary regex. Culture is fixed to
/// <see cref="CultureInfo.InvariantCulture"/>: nothing on this platform plumbs a locale through for
/// database identifier casing today, and adding one later is a small, easy change if that ever changes.
/// </summary>
static class SnakeCaseNameRewriter
{
	internal static string RewriteName(string name)
	{
		var builder = new StringBuilder(name.Length + Math.Min(2, name.Length / 5));
		var previousCategory = default(UnicodeCategory?);

		for (var currentIndex = 0; currentIndex < name.Length; currentIndex++)
		{
			var currentChar = name[currentIndex];
			if (currentChar == '_')
			{
				builder.Append('_');
				previousCategory = null;
				continue;
			}

			var currentCategory = char.GetUnicodeCategory(currentChar);
			switch (currentCategory)
			{
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
					if (previousCategory == UnicodeCategory.SpaceSeparator ||
						previousCategory == UnicodeCategory.LowercaseLetter ||
						previousCategory != UnicodeCategory.DecimalDigitNumber &&
						previousCategory != null &&
						currentIndex > 0 &&
						currentIndex + 1 < name.Length &&
						char.IsLower(name[currentIndex + 1]))
					{
						builder.Append('_');
					}

					currentChar = char.ToLower(currentChar, CultureInfo.InvariantCulture);
					break;

				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.DecimalDigitNumber:
					if (previousCategory == UnicodeCategory.SpaceSeparator)
					{
						builder.Append('_');
					}
					break;

				default:
					if (previousCategory != null)
					{
						previousCategory = UnicodeCategory.SpaceSeparator;
					}
					continue;
			}

			builder.Append(currentChar);
			previousCategory = currentCategory;
		}

		return builder.ToString();
	}
}
