﻿// <copyright file="StackdriverExporter.cs" company="OpenTelemetry Authors">
// Copyright 2018, OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

namespace OpenTelemetry.Exporter.Stackdriver
{
    using Google.Api.Gax;
    using Google.Apis.Auth.OAuth2;
    using Google.Cloud.Monitoring.V3;
    using OpenTelemetry.Exporter.Stackdriver.Implementation;
    using OpenTelemetry.Stats;
    using OpenTelemetry.Trace.Export;

    /// <summary>
    /// Implementation of the exporter to Stackdriver.
    /// </summary>
    public class StackdriverExporter
    {
        private const string ExporterName = "StackdriverTraceExporter";

        private readonly ISpanExporter exporter;
        private readonly IViewManager viewManager;
        private readonly string projectId;
        private readonly string jsonPath;
        private readonly object locker = new object();
        private StackdriverStatsExporter statsExporter;
        private bool isInitialized = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="StackdriverExporter"/> class.
        /// </summary>
        /// <param name="projectId">Google Cloud ProjectId that is used to send data to Stackdriver.</param>
        /// <param name="exporter">Exporter to get traces from.</param>
        /// <param name="viewManager">View manager to get the stats from.</param>
        public StackdriverExporter(
            string projectId,
            ISpanExporter exporter,
            IViewManager viewManager) : this(projectId, null, exporter, viewManager)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StackdriverExporter"/> class.
        /// </summary>
        /// <param name="projectId">Google Cloud ProjectId that is used to send data to Stackdriver.</param>
        /// <param name="jsonPath">File path to the json file containing the service credential used to authenticate against Stackdriver APIs.</param>
        /// <param name="exporter">Exporter to get traces from.</param>
        /// <param name="viewManager">View manager to get the stats from.</param>
        public StackdriverExporter(
            string projectId,
            string jsonPath,
            ISpanExporter exporter,
            IViewManager viewManager)
        {
            GaxPreconditions.CheckNotNullOrEmpty(projectId, "projectId");

            this.projectId = projectId;
            this.jsonPath = jsonPath;
            this.exporter = exporter;
            this.viewManager = viewManager;
        }

        /// <summary>
        /// Starts the exporter.
        /// </summary>
        public void Start()
        {
            lock (this.locker)
            {
                if (this.isInitialized)
                {
                    return;
                }

                // Register trace exporter
                if (this.exporter != null)
                {
                    var traceExporter = new StackdriverTraceExporter(this.projectId);
                    this.exporter.RegisterHandler(ExporterName, traceExporter);
                }

                // Register stats(metrics) exporter
                if (this.viewManager != null)
                {
                    var credential = this.GetGoogleCredential();

                    var statsConfig = StackdriverStatsConfiguration.Default;
                    statsConfig.GoogleCredential = credential;
                    if (statsConfig.ProjectId != this.projectId)
                    {
                        statsConfig.ProjectId = this.projectId;
                        statsConfig.MonitoredResource = GoogleCloudResourceUtils.GetDefaultResource(this.projectId);
                    }

                    this.statsExporter = new StackdriverStatsExporter(this.viewManager, statsConfig);
                    this.statsExporter.Start();
                }

                this.isInitialized = true;
            }
        }

        /// <summary>
        /// Stops the exporter.
        /// </summary>
        public void Stop()
        {
            lock (this.locker)
            {
                if (!this.isInitialized)
                {
                    return;
                }

                // Stop tracing exporter
                if (this.exporter != null)
                {
                    this.exporter.UnregisterHandler(ExporterName);
                }

                // Stop metrics exporter
                if (this.statsExporter != null)
                {
                    this.statsExporter.Stop();
                }

                this.isInitialized = false;
            }
        }

        private GoogleCredential GetGoogleCredential()
        {
            GoogleCredential credential;
            if (string.IsNullOrEmpty(this.jsonPath))
            {
                credential = GoogleCredential.GetApplicationDefault();
            }
            else
            {
                credential = GoogleCredential.FromFile(this.jsonPath)
                 .CreateScoped(MetricServiceClient.DefaultScopes);
            }

            return credential;
        }
    }
}
