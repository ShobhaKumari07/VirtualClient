// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VirtualClient.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text.RegularExpressions;
    using VirtualClient.Common.Contracts;
    using VirtualClient.Contracts;
    using DataTableExtensions = VirtualClient.Contracts.DataTableExtensions;

    /// <summary>
    /// Parser for OpenRadioss output document.
    /// </summary>
    public class OpenRadiossMetricsParser : MetricsParser
    {
        /// <summary>
        /// Separate the column values by 2 or more spaces.
        /// </summary> 
        private static readonly string StarterEngineRuntimePattern = @"STARTER\+ENGINE RUNTIME =\s+([\d\.]+)s \(([\d:]+)\)";

        /// <summary>
        /// Separate the column values by 2 or more spaces.
        /// </summary> 
        private static readonly string TotalNumberOfCyclesPattern = @"TOTAL NUMBER OF CYCLES\s+:\s+(\d+)";

        // private static readonly Regex OpenRadiossSectionDelimiter = new Regex(@"(\n)(\s)*(\n)", RegexOptions.ExplicitCapture);

        /// <summary>
        /// constructor for <see cref="OpenRadiossMetricsParser"/>.
        /// </summary>
        /// <param name="rawText">Raw text to parse.</param>
        public OpenRadiossMetricsParser(string rawText)
            : base(rawText)
        {
        }

        /// <inheritdoc/>
        public override IList<Metric> Parse()
        {
            this.ThrowIfInvalidOutputFormat();
            List<Metric> metrics = new List<Metric>();

            string input = this.RawText;

            // Parse STARTER+ENGINE RUNTIME
            var starterEngineMatch = Regex.Match(input, StarterEngineRuntimePattern);
            if (starterEngineMatch.Success)
            {
                string runtimeSecondsStr = starterEngineMatch.Groups[1].Value;
                double runtimeSeconds = double.Parse(runtimeSecondsStr);
                metrics.Add(new Metric("StarterEngineRuntime", runtimeSeconds, "seconds"));
            }
            else
            {
                throw new SchemaException("Unable to parse STARTER+ENGINE RUNTIME.");
            }

            // Parse TOTAL NUMBER OF CYCLES
            var totalCyclesMatch = Regex.Match(input, TotalNumberOfCyclesPattern);
            if (totalCyclesMatch.Success)
            {
                int totalCycles = int.Parse(totalCyclesMatch.Groups[1].Value);
                metrics.Add(new Metric("TotalNumberOfCycles", totalCycles, "Cycles"));
            }
            else
            {
                throw new SchemaException("Unable to parse TOTAL NUMBER OF CYCLES.");
            }

            // Parse TOTAL NUMBER OF CYCLES
            if (starterEngineMatch.Success && totalCyclesMatch.Success)
            {
                string runtimeSecondsStr = starterEngineMatch.Groups[1].Value;
                double runtimeSeconds = double.Parse(runtimeSecondsStr);
                int totalCycles = int.Parse(totalCyclesMatch.Groups[1].Value);
                double t = (totalCycles * 60) / (runtimeSeconds);
                int x = (int)t;
                metrics.Add(new Metric("NumberOfCyclesPerMinute", x, "Cycles/min"));

            }

            return metrics;
        }

        /// <inheritdoc/>
        private void ThrowIfInvalidOutputFormat()
        {
            if (string.IsNullOrEmpty(this.RawText))
            {
                throw new SchemaException("Input text is null or empty.");
            }
        }
    }

}