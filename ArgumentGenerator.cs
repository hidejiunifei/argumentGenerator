using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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
        private string GetQualifiedName(QualifiedNameSyntax nameSyntax)
        {
            var list = new List<string>();
            var aux = nameSyntax;
            while (aux.Right != null)
            {
                list.Add(aux.Right.Identifier.Text);
                if (aux.Left is QualifiedNameSyntax qualified)
                    aux = qualified;
                else if (aux.Left is IdentifierNameSyntax identifierNameSyntax)
                {
                    list.Add(identifierNameSyntax.Identifier.Text);
                    break;
                }
                else
                    break;
            }

            return string.Join(".", list);
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var isConstructor = false;
            var isMethod = false;

            // Find the type declaration identified by the diagnostic.
            var node = root.FindNode(diagnosticSpan) as ArgumentListSyntax;
            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().
                Where(x => x.Name is QualifiedNameSyntax).
                Select(y => GetQualifiedName(y.Name as QualifiedNameSyntax)).ToList();

            usings.AddRange(
            root.DescendantNodes().OfType<UsingDirectiveSyntax>().
                Where(x => x.Name is IdentifierNameSyntax).
                Select(y => (y.Name as IdentifierNameSyntax).Identifier.Text));

            if (node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>().Name is IdentifierNameSyntax identifierNameSyntax6)
                usings.Add(identifierNameSyntax6.Identifier.Text);

            string name = null;
            string methodName = null;

            if (node.Parent is ObjectCreationExpressionSyntax objectCreationExpressionSyntax &&
                objectCreationExpressionSyntax.Type is IdentifierNameSyntax identifierNameSyntax) {
                name = identifierNameSyntax.Identifier.Text;
                isConstructor = true;
            }

            if (
                node.Parent is ImplicitObjectCreationExpressionSyntax implicitObjectCreationExpressionSyntax &&
                implicitObjectCreationExpressionSyntax.Parent.Parent is VariableDeclaratorSyntax variableDeclaratorSyntax &&
                variableDeclaratorSyntax.Parent is VariableDeclarationSyntax variableDeclarationSyntax &&
                variableDeclarationSyntax.Type is IdentifierNameSyntax identifierNameSyntax1)
            {
                name = identifierNameSyntax1.Identifier.Text;
                isConstructor = true;
            }

            if (node.Parent is InvocationExpressionSyntax invocationExpressionSyntax &&
                invocationExpressionSyntax.Expression is MemberAccessExpressionSyntax memberAccessExpressionSyntax &&
                memberAccessExpressionSyntax.Expression is IdentifierNameSyntax identifierNameSyntax2 &&
                memberAccessExpressionSyntax.Name is IdentifierNameSyntax identifierNameSyntax3)
            {
                if (root.DescendantNodes().OfType<VariableDeclaratorSyntax>().
                    Where(x => x.Identifier.Text == identifierNameSyntax2.Identifier.Text && x.Span.End < node.SpanStart).
                        OrderBy(y => y.Span.End).Last().Parent is VariableDeclarationSyntax variableDeclarationSyntax1 &&
                    variableDeclarationSyntax1.Type is IdentifierNameSyntax identifierNameSyntax4)
                    if (identifierNameSyntax4.Identifier.Text == "var")
                    {
                        try
                        {
                            if (variableDeclarationSyntax1.Variables.Single(x => x.Identifier.Text == identifierNameSyntax2.Identifier.Text).
                                    Initializer.Value is ObjectCreationExpressionSyntax objectCreationExpressionSyntax1 &&
                                    objectCreationExpressionSyntax1.Type is IdentifierNameSyntax identifierNameSyntax5)
                            {
                                name = identifierNameSyntax5.Identifier.Text;
                            }

                        }
                        catch (Exception)
                        {
                        }
                    }
                    else
                        name = identifierNameSyntax4.Identifier.Text;

                isMethod = true;
                methodName = identifierNameSyntax3.Identifier.Text;
            }

            if (!string.IsNullOrWhiteSpace(name)) 
            {
                var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(context.Document.Project.Solution.FilePath);
                var syntaxTrees = new List<SyntaxTree>();

                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();

                    syntaxTrees.AddRange(compilation.SyntaxTrees.Where(x => x.GetCompilationUnitRoot().Members.Any() &&
                        x.GetCompilationUnitRoot().Members.First() is NamespaceDeclarationSyntax namespaceDeclarationSyntax &&
                        namespaceDeclarationSyntax.Name is QualifiedNameSyntax qualifiedNameSyntax &&
                        usings.Contains(GetQualifiedName(qualifiedNameSyntax))));
                    syntaxTrees.AddRange(compilation.SyntaxTrees.Where(x => x.GetCompilationUnitRoot().Members.Any() &&
                        x.GetCompilationUnitRoot().Members.First() is NamespaceDeclarationSyntax namespaceDeclarationSyntax1 &&
                        namespaceDeclarationSyntax1.Name is IdentifierNameSyntax identifierNameSyntax4 &&
                        usings.Contains(identifierNameSyntax4.Identifier.Text)));
                }

                var list = new List<CodeAction>();

                if (isMethod)
                {
                    foreach (var method in syntaxTrees.SelectMany(x => x.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).
                        Where(y => y.Identifier.Text == methodName && y.ParameterList.Parameters.Any() && 
                        y.Parent is ClassDeclarationSyntax classDeclarationSyntax && classDeclarationSyntax.Identifier.Text == name))
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
                }
                list.Clear();

                if (isConstructor)
                {
                    foreach (var constructor in syntaxTrees.SelectMany(x => x.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>()).
                        Where(y => y.Identifier.Text == name && y.ParameterList.Parameters.Any()))
                    {
                        list.Add(CodeAction.Create(
                                    title: "Populate parameters",
                                    createChangedDocument: c => PopulateArguments(context.Document, constructor, node, c)));
                    }

                    if (list.Any())
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Populate parameters", list.ToImmutableArray(), true)
                            , diagnostic);
                    }
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
                            result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(syntax.Identifier.Text)));
                            break;
                        case "byte":
                        case "short":
                        case "int":
                        case "double":
                        case "decimal":
                        case "long":
                        case "float":
                        case "ulong":
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
                        case "Int16":
                        case "Int32":
                        case "Int64":
                        case "Decimal":
                        case "Double":
                        case "UInt16":
                        case "UInt32":
                        case "UInt64":
                            result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(count++)));
                            break;
                        case "String":
                            result = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(syntax.Identifier.Text)));
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
