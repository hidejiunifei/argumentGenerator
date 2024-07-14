using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Package;
using Newtonsoft.Json.Linq;
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
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var node = root.FindNode(diagnosticSpan) as ArgumentListSyntax;

            if (node.Parent is ObjectCreationExpressionSyntax objectCreationExpressionSyntax &&
                objectCreationExpressionSyntax.Type is IdentifierNameSyntax identifierNameSyntax)
            {
                var nodeType = objectCreationExpressionSyntax.Type;
                var name = identifierNameSyntax.Identifier.Text;
                var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(context.Document.Project.Solution.FilePath);
                var syntaxTrees = new List<SyntaxTree>();

                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    syntaxTrees.AddRange(compilation.SyntaxTrees);
                }

                var list = new List<CodeAction>();

                foreach (var method in syntaxTrees.SelectMany(x => x.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).
                    Where(y => y.Identifier.Text == name && y.ParameterList.Parameters.Any()))
                {
                    list.Add(CodeAction.Create(
                                title: "Preencher argumentos",
                                createChangedDocument: c => PopulateArguments(context.Document, method, node, c)));
                }

                if (list.Any())
                {
                    context.RegisterCodeFix(
                            CodeAction.Create(
                                "Preencher argumentos", list.ToImmutableArray(), true)
                            , diagnostic);
                }

                list.Clear();

                foreach (var constructor in syntaxTrees.SelectMany(x => x.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>()).
                    Where(y => y.Identifier.Text == name && y.ParameterList.Parameters.Any()))
                {
                    list.Add(CodeAction.Create(
                                title: "Preencher argumentos",
                                createChangedDocument: c => PopulateArguments(context.Document, constructor, node, c)));
                }

                if (list.Any())
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Preencher argumentos", list.ToImmutableArray(), true)
                        , diagnostic);
                }
            }
        }

        private async Task<Document> PopulateArguments(Document document, MemberDeclarationSyntax memberDeclaration, ArgumentListSyntax node, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync();
            int count = 1;

            if (memberDeclaration is MethodDeclarationSyntax methodDeclarationSyntax)
            {
                var list = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                methodDeclarationSyntax.ParameterList.Parameters.
                    Select(x => GenerateArgument(x, ref count)).Where(y => y != null)
                );

                document = document.WithSyntaxRoot(root.ReplaceNode(node, node.WithArguments(list)));
            }
            else if (memberDeclaration is ConstructorDeclarationSyntax constructorDeclarationSyntax)
            {
                var list = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                constructorDeclarationSyntax.ParameterList.Parameters.
                    Select(x => GenerateArgument(x, ref count)).Where(y => y != null)
                );

                document = document.WithSyntaxRoot(root.ReplaceNode(node, node.WithArguments(list)));
            }

            return document;
        }

        private ArgumentSyntax GenerateArgument(ParameterSyntax syntax, ref int count)
        {
            ArgumentSyntax result = null;

            if (syntax.Type is PredefinedTypeSyntax predefinedTypeSyntax)
            {
                if (syntax.Default == null)
                {
                    var name = predefinedTypeSyntax.Keyword.Text;
                    switch (name)
                    {
                        case "string":
                        case "String":
                            result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(syntax.Identifier.Text)));
                            break;
                        case "byte":
                        case "short":
                        case "int":
                        case "Int16":
                        case "Int32":
                        case "Int64":
                        case "double":
                        case "Double":
                        case "decimal":
                        case "Decimal":
                            result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(count++)));
                            break;
                        default:
                            result = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name));
                            break;
                    }
                }
            }
            else if (syntax.Type is IdentifierNameSyntax identifierNameSyntax)
            {
                if (syntax.Default == null)
                {
                    var name = identifierNameSyntax.Identifier.Text;
                    switch (name)
                    {
                        case "Guid":
                            result = SyntaxFactory.Argument(SyntaxFactory.IdentifierName("Guid.Empty"));
                            break;
                        case "DateTime":
                            result = SyntaxFactory.Argument(SyntaxFactory.IdentifierName("DateTime.Today"));
                            break;
                        default:
                            result = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name));
                            break;
                    }
                }
            }
            else if (syntax.Type is NullableTypeSyntax)
            {
                if (syntax.Default == null)
                {
                    result = SyntaxFactory.Argument(SyntaxFactory.IdentifierName("null"));
                }
            }

            return result;
        }
    }
}
