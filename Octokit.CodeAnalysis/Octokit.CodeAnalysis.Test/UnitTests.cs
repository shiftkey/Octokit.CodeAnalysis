using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;

namespace Octokit.CodeAnalysis.Test
{
    [TestClass]
    public class UnitTest : DiagnosticVerifier
    {
        [TestMethod]
        public void NoAnalysisReturnsNoError()
        {
            var test = @"";

            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void HappyPathReturnsNoError()
        {
            var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace Octokit
    {
        public class IssueEventsClient
        {   
            [Endpoint(""repos/:owner/:repo/issues/events"")]
            public Task<IReadOnlyList<string>> GetAllForRepository(string owner, string name)
            {
                Ensure.ArgumentNotNullOrEmptyString(owner, ""owner"");
                Ensure.ArgumentNotNullOrEmptyString(name, ""name"");

                var uri = ""repos/{0}/{1}/issues/events"".FormatUri(owner, name);

                return ApiConnection.GetAll<string>(uri);
            }
        }
    }";

            VerifyCSharpDiagnostic(test);
        }

        [TestMethod]
        public void MarkerInterfaceCannotResolveToOldWay()
        {
            var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace Octokit
    {
        public class IssueEventsClient
        {   
            [Endpoint(""/repos/:owner/:repo/issues/events"")]
            public Task<IReadOnlyList<string>> GetAllForRepository(string owner, string name)
            {
                Ensure.ArgumentNotNullOrEmptyString(owner, ""owner"");
                Ensure.ArgumentNotNullOrEmptyString(name, ""name"");

                return ApiConnection.GetAll<string>(ApiUrls.IssuesEvents(owner, name));
            }
        }
    }";

            var expected = new DiagnosticResult
            {
                Id = "OctokitCodeAnalysis",
                Message = String.Format("Type name '{0}' contains lowercase letters", "TypeName"),
                Severity = DiagnosticSeverity.Warning,
                Locations =
                    new[] {
                            new DiagnosticResultLocation("Test0.cs", 11, 15)
                        }
            };

            VerifyCSharpDiagnostic(test, expected);
        }



        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new OctokitCodeAnalysisAnalyzer();
        }
    }
}