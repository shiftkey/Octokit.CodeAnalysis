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

        static readonly LocalizableString UnableToVerifyEndpointTitle = new LocalizableResourceString(nameof(Resources.UnableToVerifyEndpointAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        static readonly LocalizableString UnableToVerifyEndpointMessageFormat = new LocalizableResourceString(nameof(Resources.UnableToVerifyEndpointAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        static readonly LocalizableString UnableToVerifyEndpointDescription = new LocalizableResourceString(nameof(Resources.UnableToVerifyEndpointAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        // TODO: this probably isn't the right category for this sort of warning
        const string Category = "Naming";

        static readonly DiagnosticDescriptor unableToVerifyEndpointRule = new DiagnosticDescriptor(
            DiagnosticId,
            UnableToVerifyEndpointTitle,
            UnableToVerifyEndpointMessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: UnableToVerifyEndpointDescription);

        static readonly LocalizableString EndpointMismatchTitle = new LocalizableResourceString(nameof(Resources.EndpointMismatchAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        static readonly LocalizableString EndpointMismatchMessageFormat = new LocalizableResourceString(nameof(Resources.EndpointMismatchAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        static readonly LocalizableString EndpointMismatchDescription = new LocalizableResourceString(nameof(Resources.EndpointMismatchAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        static readonly DiagnosticDescriptor endpointMismatchRule = new DiagnosticDescriptor(
            DiagnosticId,
            EndpointMismatchTitle,
            EndpointMismatchMessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: EndpointMismatchDescription);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(unableToVerifyEndpointRule);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCodeBlockAction(AnalyzeCodeBlock);
        }

        static void AnalyzeCodeBlock(CodeBlockAnalysisContext codeBlockContext)
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

            // now we interrogate the method body for the Uri it passes
            // to the connection
            var inlineUrl = ResolveDefinedUrlInMethod(codeBlockContext);
            if (inlineUrl == null)
            {
                // it isn't defined locally -> raise a warning
                ReportNotFoundDiagnostic(codeBlockContext);
                return;
            }

            var formattedAttributeUrl = attributeUrl;
            var valuesDefined = Regex.Matches(formattedAttributeUrl, ":[a-z]*");

            // reverse through the found Regex results and manipulate the string
            // so that it's consumable by string.Format i.e the format we expect
            // the local variable to be in
            for (var i = valuesDefined.Count - 1; i >= 0; i--)
            {
                var replace = "{" + i + "}";
                var result = valuesDefined[i];
                formattedAttributeUrl = formattedAttributeUrl.Replace(result.Value, replace);
            }

            if (formattedAttributeUrl != inlineUrl)
            {
                // there's a mismatch -> raise a warning
                ReportMismatchDiagnostic(codeBlockContext, attributeUrl, inlineUrl);
            }
        }

        static void ReportNotFoundDiagnostic(CodeBlockAnalysisContext codeBlockContext)
        {
            var method = (IMethodSymbol) codeBlockContext.OwningSymbol;
            var location = GetLocation(codeBlockContext, method);

            var diagnostic = Diagnostic.Create(unableToVerifyEndpointRule, location, method.Name);

            codeBlockContext.ReportDiagnostic(diagnostic);
        }

        static Location GetLocation(CodeBlockAnalysisContext codeBlockContext, IMethodSymbol method)
        {
            var block = (BlockSyntax) codeBlockContext.CodeBlock.ChildNodes().First(n => n.Kind() == SyntaxKind.Block);
            var tree = block.SyntaxTree;
            var location = method.Locations.First(l => tree.Equals(l.SourceTree));
            return location;
        }

        static void ReportMismatchDiagnostic(CodeBlockAnalysisContext codeBlockContext, string attributeUrl, string inlineUrl)
        {
            var method = (IMethodSymbol) codeBlockContext.OwningSymbol;
            var location = GetLocation(codeBlockContext, method);

            var diagnostic = Diagnostic.Create(endpointMismatchRule, location, method.Name, attributeUrl.Replace("\"", ""),
                inlineUrl.Replace("\"", ""));

            // we should raise an issue here
            codeBlockContext.ReportDiagnostic(diagnostic);
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
