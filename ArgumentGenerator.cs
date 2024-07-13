using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Package;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace ArgumentGenerator
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ArgumentGenerator)), Shared]
    public class ArgumentGenerator : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(ArgumentAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            if (context.Diagnostics.First().Properties.TryGetValue("name", out string name))
            {
                var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

                // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
                var diagnostic = context.Diagnostics.First();
                var diagnosticSpan = diagnostic.Location.SourceSpan;

                // Find the type declaration identified by the diagnostic.
                var node = root.FindNode(diagnosticSpan) as ArgumentListSyntax;
                var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(context.Document.Project.Solution.FilePath);
                var syntaxTrees = new List<SyntaxTree>();

                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    syntaxTrees.AddRange(compilation.SyntaxTrees);
                }

                foreach (var method in syntaxTrees.SelectMany(x => x.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).
                    Where(y => y.Identifier.Text == name))
                {
                    context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Preencher argumentos",
                                createChangedDocument: c => PopulateArguments(context.Document, method, node, c),
                                equivalenceKey: method.Identifier.Text)
                            ,diagnostic);
                }

                foreach (var constructor in syntaxTrees.SelectMany(x => x.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>()).
                    Where(y => y.Identifier.Text == name))
                {
                    context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Preencher argumentos",
                                createChangedDocument: c => PopulateArguments(context.Document, constructor, node, c),
                                equivalenceKey: constructor.Identifier.Text)
                            ,diagnostic);
                }
            }
        }

        private async Task<Document> PopulateArguments(Document document, MemberDeclarationSyntax memberDeclaration, ArgumentListSyntax node, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync();
            int count = 1;

            if (memberDeclaration is MethodDeclarationSyntax)
            {
                var list = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                (memberDeclaration as MethodDeclarationSyntax).ParameterList.Parameters.
                    Select(x => GenerateArgument(x, ref count))
                );

                document = document.WithSyntaxRoot(root.ReplaceNode(node, node.WithArguments(list)));
            }
            else if (memberDeclaration is ConstructorDeclarationSyntax)
            {
                var list = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                (memberDeclaration as ConstructorDeclarationSyntax).ParameterList.Parameters.
                    Select(x => GenerateArgument(x, ref count))
                );

                document = document.WithSyntaxRoot(root.ReplaceNode(node, node.WithArguments(list)));
            }

            return document;
        }

        private ArgumentSyntax GenerateArgument(ParameterSyntax syntax, ref int count)
        {
            ArgumentSyntax result = null;

            if (syntax.Type is PredefinedTypeSyntax)
            {
                var name = ((PredefinedTypeSyntax)syntax.Type).Keyword.Text;
                switch (name)
                {
                    case "string":
                    case "String":
                        result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(syntax.Identifier.Text)));
                        break;
                    default:
                        result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(count++)));
                        break;
                }
            }
            else if (syntax.Type is IdentifierNameSyntax)
            {
                var name = ((IdentifierNameSyntax)syntax.Type).Identifier.Text;
                switch (name)
                {
                    case "DateTime":
                        result = SyntaxFactory.Argument(SyntaxFactory.IdentifierName("DateTime.Today"));
                        break;
                    default:
                        result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(count++)));
                        break;
                }
            }

            return result;
        }
    }
}
