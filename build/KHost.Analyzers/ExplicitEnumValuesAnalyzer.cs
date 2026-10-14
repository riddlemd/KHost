using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KHost.Analyzers;

/// <summary>Enforced only where an .editorconfig raises it: KH0002 is Hidden severity otherwise.</summary>
/// <remarks>Plugins compile enum members as numbers, so an implicit value shifts when a member is inserted above it.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitEnumValuesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "KH0002";

    // Hidden by default so the analyzer can ship solution-wide and bite in one project only.
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Enum members must declare their value",
        "'{0}' has an implicit value; this project requires an explicit '= value'",
        "Design",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Consumers compile enum members as numbers. An explicit value keeps a member's number fixed when the enum changes.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(ctx =>
        {
            var member = (EnumMemberDeclarationSyntax)ctx.Node;

            if (member.EqualsValue is not null)
                return;

            ctx.ReportDiagnostic(Diagnostic.Create(Rule, member.Identifier.GetLocation(), member.Identifier.ValueText));
        }, SyntaxKind.EnumMemberDeclaration);
    }
}
