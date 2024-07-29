// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VirtualClient.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.IO.Abstractions;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using VirtualClient.Common;
    using VirtualClient.Common.Extensions;
    using VirtualClient.Common.Platform;
    using VirtualClient.Common.Telemetry;
    using VirtualClient.Contracts;
    using VirtualClient.Contracts.Metadata;

    /// <summary>
    /// The Openradioss workload executor.
    /// </summary>
    [WindowsCompatible]
    public class OpenRadiossExecuter : VirtualClientComponent
    {
        private IFileSystem fileSystem;
        private ISystemManagement systemManagement;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenRadiossExecuter"/> class.
        /// </summary>
        /// <param name="dependencies">Provides required dependencies to the component.</param>
        /// <param name="parameters">Parameters defined in the profile or supplied on the command line.</param>
        public OpenRadiossExecuter(IServiceCollection dependencies, IDictionary<string, IConvertible> parameters)
             : base(dependencies, parameters)
        {
            this.systemManagement = this.Dependencies.GetService<ISystemManagement>();
            this.fileSystem = this.systemManagement.FileSystem;
        }

        /// <summary>
        /// Number of threads .
        /// </summary>
        public int? ThreadCount
        {
            get
            {
                this.Parameters.TryGetValue(nameof(OpenRadiossExecuter.ThreadCount), out IConvertible threadCount);
                return threadCount != null ? threadCount.ToInt32(CultureInfo.InvariantCulture) : null;
            }

            protected set
            {
                this.Parameters[nameof(this.ThreadCount)] = value;
            }
        }

        /// <summary>
        /// The command line argument defined in the profile to run the workload.
        /// </summary>
        public string CommandLine1
        {
            get
            {
                return this.Parameters.GetValue<string>(nameof(OpenRadiossExecuter.CommandLine1));
            }
        }

        /// <summary>
        /// The command line argument defined in the profile to run the workload.
        /// </summary>
        public string NumberofProcess
        {
            get
            {
                return this.Parameters.GetValue<string>(nameof(OpenRadiossExecuter.NumberofProcess));
            }
        }

        /// <summary>
        /// Parameter defines the name of the package that contains the PsExec executable/application.
        /// </summary>
        public string VisualcppPackageName
        {
            get
            {
                return this.Parameters.GetValue<string>(nameof(OpenRadiossExecuter.VisualcppPackageName)); 
            }
        }

        /// <summary>
        /// The package containing the openradioss toolsets.
        /// </summary>
        protected DependencyPath OpenRadiossPackage { get; private set; }

        /// <summary>
        /// The package containing the openradioss toolsets.
        /// </summary>
        protected DependencyPath VisualcppPackage { get; private set; }

        /// <summary>
        /// path to FurMark executable.
        /// </summary>
        protected string ExecutablePath { get; set; }

        /// <summary>
        /// path to scorefile.
        /// </summary>
        protected string ResultsFilePath { get; set; }

        /// <summary>
        /// path to scorefile.
        /// </summary>
        protected string VisualcppExecutablePath { get; set; }

        /// <summary>
        /// Executes cleanup operations. Because FurMark can run in a separate session (i.e. via PSExec), we need to 
        /// be explicit about ensuring the process is stopped before exiting.
        /// </summary>
        protected override async Task CleanupAsync(EventContext telemetryContext, CancellationToken cancellationToken)
        {
            await base.CleanupAsync(telemetryContext, cancellationToken);
            ProcessManager processManager = this.Dependencies.GetService<ProcessManager>();

            string processName = "OpenRadioss";
            IEnumerable<IProcessProxy> runningProcesses = processManager.GetProcesses(Path.GetFileNameWithoutExtension(processName));

            if (runningProcesses?.Any() == true)
            {
                foreach (IProcessProxy processProxy in runningProcesses)
                {
                    processProxy.SafeKill();
                }
            }
        }

        /// <summary>
        /// Initializes the environment for execution of the FurMark workload.
        /// </summary>
        protected override async Task InitializeAsync(EventContext telemetryContext, CancellationToken cancellationToken)
        {
            this.OpenRadiossPackage = await this.GetPlatformSpecificPackageAsync(this.PackageName, cancellationToken);
              
            switch (this.Platform)
            {
                case PlatformID.Win32NT:
                    this.VisualcppPackage = await this.GetPackageAsync(this.VisualcppPackageName, cancellationToken);
                    this.ExecutablePath = this.Combine(this.OpenRadiossPackage.Path, "win_scripts_mk4", "openradioss_run_script_ps.bat");
                    this.ResultsFilePath = this.Combine(this.OpenRadiossPackage.Path, "win_scripts_mk4", "NEON1M11_0001.out");
                    this.VisualcppExecutablePath = this.Combine(this.VisualcppPackage.Path, "VC_redist.x64.exe");
                    break;

                case PlatformID.Unix:
                    this.ExecutablePath = this.Combine(this.OpenRadiossPackage.Path);
                    this.ResultsFilePath = this.Combine(this.OpenRadiossPackage.Path, "NEON1M11_0001.out");
                    break;

                default:
                    throw new WorkloadException(
                        $"The Openradioss workload is not supported on the current platform/architecture " +
                        $"{PlatformSpecifics.GetPlatformArchitectureName(this.Platform, this.CpuArchitecture)}." +
                        ErrorReason.PlatformNotSupported);
            }
            
        }

        /// <summary>
        /// Returns true/false whether the component is supported on the current
        /// OS platform and CPU architecture.
        /// </summary>
        protected override bool IsSupported()
        {
            bool isSupported = base.IsSupported()
               &&
               ((this.Platform == PlatformID.Unix && this.CpuArchitecture == Architecture.X64)
               || (this.Platform == PlatformID.Win32NT && (this.CpuArchitecture == Architecture.X64 || this.CpuArchitecture == Architecture.Arm64)));

            if (!isSupported)
            {
                this.Logger.LogNotSupported("OpenRadioss", this.Platform, this.CpuArchitecture, EventContext.Persisted());
            }

            return isSupported;
        }

        /// <summary>
        /// Executes OpenRadiossExecuter workload.
        /// </summary>
        protected override async Task ExecuteAsync(EventContext telemetryContext, CancellationToken cancellationToken)
        {
            using (BackgroundOperations profiling = BackgroundOperations.BeginProfiling(this, cancellationToken))
            {
                if (this.fileSystem.File.Exists(this.ResultsFilePath))
                {
                    this.fileSystem.File.Delete(this.ResultsFilePath);
                }

                await this.ExecuteWorkloadAsync(telemetryContext, cancellationToken);
            }
        }

        private async Task ExecuteWorkloadAsync(EventContext telemetryContext, CancellationToken cancellationToken)
        {
            // Execute the first command to install Visual C++ redistributable
            switch (this.Platform)
            {
                case PlatformID.Win32NT:
                    {
                        string commandArguments_1 = $"{this.VisualcppExecutablePath}";
                        string buildArgument1 = $"/install /passive /norestart";

                        using (IProcessProxy process1 = await this.ExecuteCommandAsync(commandArguments_1, buildArgument1, this.VisualcppPackage.Path, telemetryContext, cancellationToken))
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                string[] openradiossresult1 = null;

                                try
                                {
                                    // Check for errors or specific conditions for process1 if needed
                                    if (process1.StandardError.Length > 0)
                                    {
                                        throw new WorkloadResultsException(
                                            $"Error occurred while installing Visual C++ redistributable: {process1.StandardError}",
                                            ErrorReason.DependencyInstallationFailed);
                                    }

                                    // Assuming the Visual C++ installation creates a necessary file or condition for the second command
                                    // For example, if a file is created or modified by the installation, you can check its existence or validity here

                                    // Proceed to execute the second command only if the first command succeeded
                                    string commandArguments_2 = $"{this.ExecutablePath}";
                                    string buildArgument2 = this.CommandLine1;

                                    using (IProcessProxy process2 = await this.ExecuteCommandAsync(commandArguments_2, buildArgument2, this.OpenRadiossPackage.Path, telemetryContext, cancellationToken))
                                    {
                                        if (!cancellationToken.IsCancellationRequested)
                                        {
                                            string[] openradiossresult2 = null;

                                            try
                                            {
                                                // Check for errors or specific conditions for process2 if needed
                                                if (process2.StandardError.Length > 0)
                                                {
                                                    throw new WorkloadResultsException(
                                                        $"Error occurred while running OpenRadioss: {process2.StandardError}",
                                                        ErrorReason.WorkloadFailed);
                                                }

                                                // Load results from the expected file path
                                                if (!this.fileSystem.File.Exists(this.ResultsFilePath))
                                                {
                                                    throw new WorkloadResultsException(
                                                        $"The expected openradioss results file was not found at path '{this.ResultsFilePath}'.",
                                                        ErrorReason.WorkloadResultsNotFound);
                                                }

                                                string results = await this.LoadResultsAsync(this.ResultsFilePath, cancellationToken);
                                                openradiossresult2 = new string[] { results };

                                                // Capture metrics if necessary
                                                this.CaptureMetrics(process2, results, telemetryContext);
                                            }
                                            finally
                                            {
                                                // Log process details for the second command
                                                await this.LogProcessDetailsAsync(process2, telemetryContext, "OpenRadioss", openradiossresult2);
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    // Log process details for the first command
                                    await this.LogProcessDetailsAsync(process1, telemetryContext, "VisualCppInstall", openradiossresult1);
                                }
                            }
                        }

                        break;

                    }

                case PlatformID.Unix:
                    {
                        // string commandArguments_2 = $"{this.ExecutablePath}";
                        string buildArgument2 = $"chmod 777 ";

                        using (IProcessProxy process = await this.ExecuteCommandAsync("sudo", buildArgument2 + this.ExecutablePath, this.ExecutablePath, telemetryContext, cancellationToken))
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                string[] openradiossresult2 = null;

                                try
                                {
                                    // Check for errors or specific conditions for process2 if needed
                                    if (process.StandardError.Length > 0)
                                    {
                                        throw new WorkloadResultsException(
                                            $"Error occurred while running OpenRadioss: {process.StandardError}",
                                            ErrorReason.WorkloadFailed);
                                    }
                                }
                                finally
                                {
                                    // Log process details for the second command
                                    await this.LogProcessDetailsAsync(process, telemetryContext, "OpenRadioss", openradiossresult2);
                                }
                            }
                        }

                        using (IProcessProxy process = await this.ExecuteCommandAsync("sudo", buildArgument2 + "runscript.sh", this.ExecutablePath, telemetryContext, cancellationToken))
                            {
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    string[] openradiossresult2 = null;

                                    try
                                    {
                                        // Check for errors or specific conditions for process2 if needed
                                        if (process.StandardError.Length > 0)
                                        {
                                            throw new WorkloadResultsException(
                                                $"Error occurred while running OpenRadioss: {process.StandardError}",
                                                ErrorReason.WorkloadFailed);
                                        }
                                    }
                                    finally
                                    {
                                        // Log process details for the second command
                                        await this.LogProcessDetailsAsync(process, telemetryContext, "OpenRadioss", openradiossresult2);
                                    }
                                }
                            }

                        string buildArgument1 = $" bash -c \"./runscript.sh NEON1M11_0000.rad {this.ThreadCount}\"";

                        using (IProcessProxy process2 = await this.ExecuteCommandAsync("sudo", buildArgument1, this.ExecutablePath, telemetryContext, cancellationToken))
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                string[] openradiossresult2 = null;

                                try
                                {
                                    // Check for errors or specific conditions for process2 if needed
                                    if (process2.StandardError.Length > 0)
                                    {
                                        throw new WorkloadResultsException(
                                            $"Error occurred while running OpenRadioss: {process2.StandardError}",
                                            ErrorReason.WorkloadFailed);
                                    }

                                    // Load results from the expected file path
                                    if (!this.fileSystem.File.Exists(this.ResultsFilePath))
                                    {
                                        throw new WorkloadResultsException(
                                            $"The expected openradioss results file was not found at path '{this.ResultsFilePath}'.",
                                            ErrorReason.WorkloadResultsNotFound);
                                    }

                                    string results = await this.LoadResultsAsync(this.ResultsFilePath, cancellationToken);
                                    openradiossresult2 = new string[] { results };

                                    // Capture metrics if necessary
                                    this.CaptureMetrics(process2, results, telemetryContext);
                                }
                                finally
                                {
                                    // Log process details for the second command
                                    await this.LogProcessDetailsAsync(process2, telemetryContext, "OpenRadioss", openradiossresult2);
                                }
                            }
                        }
                           
                        break;

                    }

                }

            }

        /// <summary>
        /// Logs the Openradioss workload metrics.
        /// </summary>
        private void CaptureMetrics(IProcessProxy process, string results, EventContext telemetryContext)
        {
            this.MetadataContract.AddForScenario(
                  "OpenRadioss",
                  process.FullCommand(),
                  toolVersion: this.OpenRadiossPackage.Version);

            this.MetadataContract.Apply(telemetryContext);

            OpenRadiossMetricsParser resultsParser = new OpenRadiossMetricsParser(results);
            IList<Metric> metrics1 = resultsParser.Parse();

            this.Logger.LogMetrics(
                "OpenRadioss",
                this.MetricScenario ?? this.Scenario,
                process.StartTime,
                process.ExitTime,
                metrics1,
                null,
                process.FullCommand(),
                this.Tags,
                telemetryContext);
        }
    }
}