using System.Text;
using System.Text.RegularExpressions;

namespace KHost.UnitTests.Conventions;

// Scoped SCSS owns every visual rule. A runtime value reaches it as a --custom-property set
// inline (SongControlSlider.razor), so a literal property in markup is always a rule in the wrong file.
public partial class RazorStyleConventionTests
{
    [Fact]
    public void Razor_HasNoStyleElementsInlineCodeBlocksOrLiteralInlineStyles()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in RazorFiles(Path.Combine(root, "src")))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(root, file);

            if (StyleElement().IsMatch(text))
                offenders.Add($"{name}: <style> element");
            if (CodeBlock().IsMatch(text))
                offenders.Add($"{name}: inline @code block; use a code-behind partial");
            foreach (var declaration in LiteralDeclarations(text))
                offenders.Add($"{name}: literal inline style `{declaration}`");
        }

        Assert.True(
            offenders.Count == 0,
            $"Move these into the component's .razor.scss; inline style may only set --custom-properties:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    // A bare `@Expr` value is built in C# and cannot be read from markup; the code-behind owns it.
    private static IEnumerable<string> LiteralDeclarations(string text)
    {
        foreach (Match start in StyleAttribute().Matches(text))
        {
            var value = ReadValue(text, start.Index + start.Length);
            foreach (var declaration in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (declaration.StartsWith("--", StringComparison.Ordinal) || BareExpression().IsMatch(declaration))
                    continue;
                yield return declaration;
            }
        }
    }

    // Razor expressions can hold quotes of their own (`@(x.ToString("F1"))`), so the closing quote
    // is the first one outside a balanced `@( ... )`. Each expression collapses to `@()`.
    private static string ReadValue(string text, int index)
    {
        var value = new StringBuilder();
        while (index < text.Length && text[index] != '"')
        {
            if (text[index] == '@' && index + 1 < text.Length && text[index + 1] == '(')
            {
                var depth = 0;
                index++;
                do
                {
                    if (text[index] == '(') depth++;
                    else if (text[index] == ')') depth--;
                    index++;
                } while (depth > 0 && index < text.Length);
                value.Append("@()");
                continue;
            }

            value.Append(text[index++]);
        }

        return value.ToString();
    }

    private static IEnumerable<string> RazorFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj" or "node_modules"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!.FullName;
    }

    [GeneratedRegex(@"<style[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex StyleElement();

    [GeneratedRegex(@"^\s*@code\s*\{", RegexOptions.Multiline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\sstyle\s*=\s*""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"^@[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex BareExpression();
}
