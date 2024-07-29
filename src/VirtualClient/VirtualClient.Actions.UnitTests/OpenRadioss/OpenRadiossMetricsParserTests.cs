using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VirtualClient.Common.Contracts;
using NUnit.Framework;
using VirtualClient.Contracts;

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VirtualClient.Actions
{
    [TestFixture]
    [Category("Unit")]
    class OpenRadiossMetricsParserTests
    {
        [TestFixture]
        [Category("Unit")]
        public class OpenRadiossRuntimeParserTests
        {
            private string rawText;
            private OpenRadiossMetricsParser testParser;

            [SetUp]
            public void Setup()
            {
                string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string outputPath = Path.Combine(workingDirectory, "Examples", "OpenRadioss", "OpenRadiossResultsExample.out");
                this.rawText = File.ReadAllText(outputPath);
                this.testParser = new OpenRadiossMetricsParser(this.rawText);
            }

            [Test]
            public void OpenRadiossParserVerifyMetrics()
            {
                IList<Metric> metrics = this.testParser.Parse();

                Assert.AreEqual(3, metrics.Count);
                MetricAssert.Exists(metrics, "StarterEngineRuntime", 66370.16);
                MetricAssert.Exists(metrics, "TotalNumberOfCycles", 160039);
                MetricAssert.Exists(metrics, "NumberOfCyclesPerMinute", 144);
            }

            [Test]
            public void OpenRadiossParserThrowIfInvalidOutputFormat()
            {
                string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string incorrectOutputPath = Path.Combine(workingDirectory, "Examples", "OpenRadioss", "OpenRadiossResultsInvalidExample.out");
                this.rawText = File.ReadAllText(incorrectOutputPath);
                this.testParser = new OpenRadiossMetricsParser(this.rawText);

                SchemaException exception = Assert.Throws<SchemaException>(() => this.testParser.Parse());
                StringAssert.Contains("Input text is null or empty.", exception.Message);
            }
        }
    }
}
