using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Builders;
using NUnit.Framework.Internal.Filters;

namespace MyAiWorld.CodeTests
{
    internal sealed class CodeTestRunner : ITestListener
    {
        // NUnit temporarily redirects Console; writing callbacks into that redirect recurses.
        // Instance construction precedes runner.Run and therefore NUnit's redirection.
        private readonly TextWriter _standardOutput = Console.Out;
        private readonly TextWriter _standardError = Console.Error;
        private static int Main(string[] args)
        {
            try
            {
                string suite = args[0];
                string reportPath = args[1];
                bool listOnly = args[2] == "list";
                string[] names = args.Skip(3).ToArray();
                TestFilter filter = TestFilter.Empty;
                if (suite == "fast") { filter = new NotFilter(new CategoryFilter("LongRunning")); }
                if (suite == "slow") { filter = new CategoryFilter("LongRunning"); }
                if (names.Length > 0)
                {
                    var nameFilter = new FullNameFilter(string.Join("|", names.Select(Regex.Escape))) { IsRegex = true };
                    filter = new AndFilter(filter, nameFilter);
                }

                var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
                runner.Load(Assembly.GetExecutingAssembly(), new Dictionary<string, object>
                {
                    { "NumberOfTestWorkers", 0 },
                    { "WorkDirectory", Path.GetDirectoryName(reportPath) }
                });
                int discovered = runner.LoadedTest.TestCaseCount;
                int selected = runner.CountTestCases(filter);
                Console.WriteLine("Code tests ({0}): {1} selected, {2} discovered, {3} outside this suite.",
                    suite, selected, discovered, discovered - selected);
                if (selected == 0)
                {
                    Console.Error.WriteLine("No tests matched. Check the suite and --filter.");
                    return 2;
                }
                if (listOnly)
                {
                    foreach (ITest test in Leaves(runner.LoadedTest).Where(filter.Pass)) { Console.WriteLine(test.FullName); }
                    return 0;
                }

                ITestResult result = runner.Run(new CodeTestRunner(), filter);
                File.WriteAllText(Path.ChangeExtension(reportPath, ".xml"), result.ToXml(true).OuterXml);
                var cases = Results(result).Select(item => new
                {
                    name = item.FullName,
                    result = item.ResultState.ToString(),
                    durationSeconds = item.Duration,
                    message = item.Message,
                    stackTrace = item.StackTrace
                }).ToArray();
                File.WriteAllText(reportPath, JsonConvert.SerializeObject(new
                {
                    mode = "Code",
                    suite,
                    filters = names,
                    discovered,
                    selected,
                    excluded = discovered - selected,
                    result = result.ResultState.ToString(),
                    completedUtc = DateTime.UtcNow.ToString("O"),
                    passed = result.PassCount,
                    failed = result.FailCount,
                    skipped = result.SkipCount,
                    inconclusive = result.InconclusiveCount,
                    durationSeconds = result.Duration,
                    cases
                }, Formatting.Indented));
                Console.WriteLine("Passed: {0}, failed: {1}, skipped: {2}, inconclusive: {3}. Test time: {4:F3}s.",
                    result.PassCount, result.FailCount, result.SkipCount, result.InconclusiveCount, result.Duration);
                foreach (var item in cases.OrderByDescending(item => item.durationSeconds).Take(5))
                {
                    Console.WriteLine("  {0:F3}s  {1}", item.durationSeconds, item.name);
                }
                return result.FailCount == 0 && result.SkipCount == 0 && result.InconclusiveCount == 0 &&
                    result.PassCount == selected ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 2;
            }
        }

        private static IEnumerable<ITest> Leaves(ITest test)
        {
            if (!test.IsSuite) { yield return test; }
            else
            {
                foreach (ITest child in test.Tests)
                foreach (ITest leaf in Leaves(child)) { yield return leaf; }
            }
        }

        private static IEnumerable<ITestResult> Results(ITestResult result)
        {
            if (!result.HasChildren) { yield return result; }
            else
            {
                foreach (ITestResult child in result.Children)
                foreach (ITestResult leaf in Results(child)) { yield return leaf; }
            }
        }

        public void TestStarted(ITest test) { }
        public void TestOutput(TestOutput output) { _standardOutput.Write(output.Text); }
        public void TestFinished(ITestResult result)
        {
            if (!result.Test.IsSuite && result.ResultState.Status != TestStatus.Passed)
            {
                _standardError.WriteLine("{0}: {1}\n{2}\n{3}", result.ResultState, result.FullName, result.Message, result.StackTrace);
            }
        }
    }
}
