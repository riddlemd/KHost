using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KHost.Analyzers;

/// <summary>
/// Enforced only where an <c>.editorconfig</c> raises it: KHost.Abstractions declares contracts and
/// computes nothing, so a static method there is logic that belongs in KHost.Common.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoStaticMethodsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "KH0001";

    // Hidden by default so the analyzer can ship solution-wide and bite in one project only.
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Static methods are not allowed in this project",
        "'{0}' is static; this project forbids static methods",
        "Design",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "This project declares contracts. Behaviour belongs in a project that may hold it.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var entryPoint = start.Compilation.GetEntryPoint(start.CancellationToken);

            start.RegisterSymbolAction(ctx =>
            {
                var method = (IMethodSymbol)ctx.Symbol;

                if (!method.IsStatic || method.IsImplicitlyDeclared)
                    return;

                // Constructors, operators, conversions and property accessors are static by
                // language rule, not by choice — flagging them would make the project uncompilable.
                if (method.MethodKind != MethodKind.Ordinary && method.MethodKind != MethodKind.LocalFunction)
                    return;

                if (SymbolEqualityComparer.Default.Equals(method, entryPoint))
                    return;

                if (method.IsExtensionMethod)
                    return;

                if (method.GetAttributes().Any(a => a.AttributeClass?.Name == "ModuleInitializerAttribute"))
                    return;

                ctx.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0], method.Name));
            }, SymbolKind.Method);
        });
    }
}
