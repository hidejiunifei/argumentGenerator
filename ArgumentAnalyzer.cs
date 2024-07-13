using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace ArgumentGenerator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ArgumentAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ArgumentAnalyzer";
        internal static readonly LocalizableString Title = "ArgumentAnalyzer Title";
        internal static readonly LocalizableString MessageFormat = "ArgumentAnalyzer '{0}'";
        internal const string Category = "ArgumentAnalyzer Category";

        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.ArgumentList);
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var localDeclaration = (ArgumentListSyntax)context.Node;

            if (!localDeclaration.Arguments.Any())
            {
                if (context.Node.Parent is ObjectCreationExpressionSyntax &&
                    (context.Node.Parent as ObjectCreationExpressionSyntax).Type is IdentifierNameSyntax)
                {
                    var diagnostic = Diagnostic.Create(Rule, localDeclaration.GetLocation(), 
                        new Dictionary<string, string>() { { "name", 
                            ((IdentifierNameSyntax)((ObjectCreationExpressionSyntax)context.Node.Parent).Type).Identifier.Text } }
                        .ToImmutableDictionary());

                    context.ReportDiagnostic(diagnostic);
                }
                else if (context.Node.Parent is InvocationExpressionSyntax &&
                    (context.Node.Parent as InvocationExpressionSyntax).Expression is IdentifierNameSyntax)
                {
                    var diagnostic = Diagnostic.Create(Rule, localDeclaration.GetLocation(),
                        new Dictionary<string, string>() { { "name",
                            ((IdentifierNameSyntax)((InvocationExpressionSyntax)context.Node.Parent).Expression).Identifier.Text } }
                        .ToImmutableDictionary());

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}
