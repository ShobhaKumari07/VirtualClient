// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VirtualClient.Actions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Moq;
    using NUnit.Framework;
    using VirtualClient.Common.Telemetry;
    using VirtualClient.Contracts;

    [TestFixture]
    [Category("Unit")]
    public class OpenRadiossExecutorTests
    {
        private MockFixture mockFixture;
        private DependencyPath mockOpenRadiossPackage;
        private DependencyPath mockVisualcppPackage;
        private string results;

        [SetUp]
        public void SetUpTests()
        {
            this.mockFixture = new MockFixture();
        }
        [Test]
        [TestCase(PlatformID.Win32NT, Architecture.X64)]
        public async Task ExecutorInitializesItsDependenciesAsExpected(PlatformID platform, Architecture architecture)
        {
            this.SetupDefaultMockBehavior(platform, architecture);
            using (TestOpenRadiossExecutor executor = new TestOpenRadiossExecutor(this.mockFixture))
            {
                this.mockFixture.ProcessManager.OnCreateProcess = (command, arguments, workingDirectory) =>
                {
                    return this.mockFixture.Process;
                };

                await executor.InitializeAsync(EventContext.None, CancellationToken.None)
                    .ConfigureAwait(false);

                string expectedScriptFilePath = this.mockFixture.PlatformSpecifics.Combine(
                    this.mockOpenRadiossPackage.Path,"win-x64","win_scripts_mk4", "openradioss_run_script_ps.bat");

                Assert.AreEqual(expectedScriptFilePath, executor.ExecutablePath);
            }
        }

        [Test]
        [TestCase(PlatformID.Win32NT, Architecture.X64)]
        public async Task OpenRadiossExecutorExecutesWorkloadAsExpected(PlatformID platform, Architecture architecture)
        {
            this.SetupDefaultMockBehavior(platform, architecture);

            using (TestOpenRadiossExecutor executor = new TestOpenRadiossExecutor(this.mockFixture))
            {
                int executed = 0;
                if (platform == PlatformID.Win32NT)
                {
                    string VisualcppPath = this.mockFixture.Combine(this.mockVisualcppPackage.Path, "VC_redist.x64.exe");
                    string expectedOpenRadiossExecutablePath = this.mockFixture.Combine(this.mockOpenRadiossPackage.Path, "win-x64","win_scripts_mk4", "openradioss_run_script_ps.bat");
                    string workingDir = this.mockFixture.Combine(this.mockOpenRadiossPackage.Path, "win-x64");

                    string expectedOpenRadiossArguments = this.mockFixture.Parameters["CommandLine1"].ToString();
                    string expectedVisualcppArguments = this.mockFixture.Parameters["CommandLine2"].ToString();
                    string expectedOpenRadiossCommandArguments = $"{expectedOpenRadiossExecutablePath} {expectedOpenRadiossArguments}";
                    string expectedVisualcppCommandArguments = $"{VisualcppPath} {expectedVisualcppArguments}";




                    this.mockFixture.ProcessManager.OnCreateProcess = (command, arguments, workingDirectory) =>
                    {
                         if ((command == VisualcppPath  || command == expectedOpenRadiossExecutablePath) && (arguments == expectedOpenRadiossArguments || arguments == expectedVisualcppArguments))
                        {
                            executed++;
                        }

                        return this.mockFixture.Process;
                    };

                    await executor.ExecuteAsync(EventContext.None, CancellationToken.None);
                }

                Assert.AreEqual(2, executed);
            }
        }

        [Test]
        public void OpenRadiossExecutorThrowsWhenTheResultsFileIsNotFoundAfterExecutingOpenRadioss()
        {
            this.SetupDefaultMockBehavior();

            using (TestOpenRadiossExecutor executor = new TestOpenRadiossExecutor(this.mockFixture))
            {
                this.mockFixture.ProcessManager.OnCreateProcess = (file, arguments, workingDirectory) =>
                {
                    this.mockFixture.FileSystem.Setup(fe => fe.File.Exists(executor.ResultsFilePath)).Returns(false);
                    this.mockFixture.Process.StandardError.Append("123");
                    return this.mockFixture.Process;
                };

                WorkloadResultsException exception = Assert.ThrowsAsync<WorkloadResultsException>(
                     () => executor.ExecuteAsync(EventContext.None, CancellationToken.None));
                int err = 0;
                if(exception.Reason == ErrorReason.DependencyInstallationFailed || ErrorReason.WorkloadResultsNotFound == exception.Reason)
                {
                    err++;

                }

                Assert.AreEqual(1, err);
            }
        }

        [Test]
        private void SetupDefaultMockBehavior(PlatformID platform = PlatformID.Win32NT, Architecture architecture = Architecture.X64)
        {
            this.mockFixture.Setup(platform, architecture);

            this.mockFixture.Parameters = new Dictionary<string, IConvertible>
            {
                { "Scenario", "Running_OpenRadioss"},
                { "MetricScenario","np_1_and_nt_2" },
                { "PackageName", "openradioss"},
                {"VisualcppPackageName", "visual_c++_red"},
                { "CommandLine1", "NEON1M11_0000.rad 2 1 no no no no no"},
                { "CommandLine2", "/install /passive /norestart"}
            };

            this.mockOpenRadiossPackage = new DependencyPath("openradioss", this.mockFixture.GetPackagePath("openradioss"));
            this.mockVisualcppPackage = new DependencyPath("visual_c++_red", this.mockFixture.GetPackagePath("visual_c++_red"));

            this.mockFixture.FileSystem.Setup(fe => fe.File.Exists(It.IsAny<string>())).Returns(true);
            this.mockFixture.FileSystem.Setup(fe => fe.File.Exists(null)).Returns(false);

            this.results = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Examples", "OpenRadioss", "OpenRadiossResultsExample.out"));

            this.mockFixture.FileSystem.Setup(rt => rt.File.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(this.results);

            this.mockFixture.PackageManager.OnGetPackage("openradioss").ReturnsAsync(this.mockOpenRadiossPackage);
            this.mockFixture.PackageManager.OnGetPackage("visual_c++_red").ReturnsAsync(this.mockVisualcppPackage);


            this.mockFixture.ProcessManager.OnCreateProcess = (command, arguments, directory) => this.mockFixture.Process;
        }

        private class TestOpenRadiossExecutor : OpenRadiossExecuter
        {
            public TestOpenRadiossExecutor(MockFixture fixture)
                : base(fixture.Dependencies, fixture.Parameters)
            {
            }

            public TestOpenRadiossExecutor(IServiceCollection dependencies, IDictionary<string, IConvertible> parameters)
                : base(dependencies, parameters)
            {
            }

            public new string ExecutablePath => base.ExecutablePath;

            public new string ResultsFilePath => base.ResultsFilePath;


            public new Task InitializeAsync(EventContext telemetryContext, CancellationToken cancellationToken)
            {
                return base.InitializeAsync(telemetryContext, cancellationToken);
            }

            public new Task ExecuteAsync(EventContext context, CancellationToken cancellationToken)
            {
                this.InitializeAsync(context, cancellationToken).GetAwaiter().GetResult();
                return base.ExecuteAsync(context, cancellationToken);
            }
        }
    }
}
