using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Norse.Persistence.EntityFramework.Design.Generator.Shared;

static class CSharpEmit
{
	// Identical to StringBuilder.AppendLine at runtime; the [StringSyntax("C#")] annotation
	// is what Visual Studio and Rider use to syntax-highlight the raw-string content at each
	// call site as C# instead of as opaque text.
	public static StringBuilder AppendCSharp(this StringBuilder sb, [StringSyntax("C#")] string code) =>
		sb.AppendLine(code);
}
