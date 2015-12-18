using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Octokit.CodeAnalysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class OctokitCodeAnalysisAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "Octokit.CodeAnalysis";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Naming";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCodeBlockAction(AnalyzeCodeBlock);
        }

        private void AnalyzeCodeBlock(CodeBlockAnalysisContext codeBlockContext)
        {
            // We only care about method bodies.
            if (codeBlockContext.OwningSymbol.Kind != SymbolKind.Method)
            {
                return;
            }

            // to verify our current API implementation, we're going to
            // look for an [Endpoint] attribute on this method
            var attributeUrl = ResolveUrlFromAttribute(codeBlockContext);
            if (attributeUrl == null)
            {
                // method has not been updated to match conventions
                // -> don't worry about analyzing further
                return;
            }

            var inlineUrl = ResolveDefinedUrlInMethod(codeBlockContext);
            if (inlineUrl == null)
            {
                var method = (IMethodSymbol)codeBlockContext.OwningSymbol;
                var block = (BlockSyntax)codeBlockContext.CodeBlock.ChildNodes().First(n => n.Kind() == SyntaxKind.Block);
                var tree = block.SyntaxTree;
                var location = method.Locations.First(l => tree.Equals(l.SourceTree));

                var diagnostic = Diagnostic.Create(Rule, location, method.Name);

                // we should raise an issue here
                codeBlockContext.ReportDiagnostic(diagnostic);
                return;
            }
            
            var formattedAttributeUrl = attributeUrl;
            var valuesDefined = Regex.Matches(formattedAttributeUrl, ":[a-z]*");

            // reverse through the found results and manipulate the string
            for (var i = valuesDefined.Count - 1; i >= 0; i--)
            {
                var replace = "{" + i + "}";
                var result = valuesDefined[i];
                formattedAttributeUrl = formattedAttributeUrl.Replace(result.Value, replace);
            }

            if (formattedAttributeUrl != inlineUrl)
            {
                var method = (IMethodSymbol)codeBlockContext.OwningSymbol;
                var block = (BlockSyntax)codeBlockContext.CodeBlock.ChildNodes().First(n => n.Kind() == SyntaxKind.Block);
                var tree = block.SyntaxTree;
                var location = method.Locations.First(l => tree.Equals(l.SourceTree));

                var diagnostic = Diagnostic.Create(Rule, location, method.Name);

                // we should raise an issue here
                codeBlockContext.ReportDiagnostic(diagnostic);
            }
        }

        static string ResolveUrlFromAttribute(CodeBlockAnalysisContext codeBlockContext)
        {
            var childNodes = codeBlockContext.CodeBlock.ChildNodes();

            // no attributes defined, abort
            var attributes = childNodes.First(n => n.Kind() == SyntaxKind.AttributeList) as AttributeListSyntax;

            return attributes?.Attributes
                .Where(AttributeIsEndpoint)
                .Select(FindFirstArgument)
                .FirstOrDefault();
        }

        private static string FindFirstArgument(AttributeSyntax arg)
        {
            var firstArgument = arg.ArgumentList.Arguments.FirstOrDefault();

            var literal = firstArgument?.Expression as LiteralExpressionSyntax;

            return literal?.Token.Text;
        }

        static bool AttributeIsEndpoint(AttributeSyntax arg)
        {
            var identifier = arg.Name as IdentifierNameSyntax;
            if (identifier == null)
            {
                return false;
            }

            return identifier.Identifier.Text == "Endpoint";
        }


        static string ResolveDefinedUrlInMethod(CodeBlockAnalysisContext codeBlockContext)
        {
            var childNodes = codeBlockContext.CodeBlock.ChildNodes();

            var codeBlock = childNodes.First(n => n.Kind() == SyntaxKind.Block) as BlockSyntax;
            if (codeBlock == null)
            {
                return null;
            }

            var localAssignments = codeBlock.Statements.Where(s => s.Kind() == SyntaxKind.LocalDeclarationStatement)
                .Cast<LocalDeclarationStatementSyntax>();

            foreach (var statement in localAssignments)
            {
                var uriVariable = statement.Declaration.Variables
                    .FirstOrDefault(v => v.Identifier.ValueText == "uri");

                var formattedUri = uriVariable?.Initializer.Value as InvocationExpressionSyntax;

                var formatMethod = formattedUri?.Expression as MemberAccessExpressionSyntax;

                var sourceUri = formatMethod?.Expression as LiteralExpressionSyntax;
                if (sourceUri == null)
                {
                    continue;
                }

                return sourceUri.Token.Text;
            }

            return null;
        }
    }
}
