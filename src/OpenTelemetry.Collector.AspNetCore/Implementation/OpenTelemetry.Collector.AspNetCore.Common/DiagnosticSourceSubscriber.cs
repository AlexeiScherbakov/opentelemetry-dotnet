﻿// <copyright file="DiagnosticSourceSubscriber.cs" company="OpenTelemetry Authors">
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

namespace OpenTelemetry.Collector.AspNetCore.Common
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.AspNetCore.Http;
    using OpenTelemetry.Trace;

    internal class DiagnosticSourceSubscriber : IDisposable, IObserver<DiagnosticListener>
    {
        private readonly Dictionary<string, Func<ITracer, Func<HttpRequest, ISampler>, ListenerHandler>> handlers;
        private readonly ITracer tracer;
        private readonly Func<HttpRequest, ISampler> sampler;
        private ConcurrentDictionary<string, DiagnosticSourceListener> subscriptions;
        private long disposed;
        private IDisposable subscription;

        public DiagnosticSourceSubscriber(Dictionary<string, Func<ITracer, Func<HttpRequest, ISampler>, ListenerHandler>> handlers, ITracer tracer, Func<HttpRequest, ISampler> sampler)
        {
            this.subscriptions = new ConcurrentDictionary<string, DiagnosticSourceListener>();
            this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            this.tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
            this.sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        }

        public void Subscribe()
        {
            if (this.subscription == null)
            {
                this.subscription = DiagnosticListener.AllListeners.Subscribe(this);
            }
        }

        public void OnNext(DiagnosticListener value)
        {
            if ((Interlocked.Read(ref this.disposed) == 0) && this.subscriptions != null)
            {
                if (this.handlers.ContainsKey(value.Name))
                {
                    this.subscriptions.GetOrAdd(value.Name, name =>
                    {
                        var dl = new DiagnosticSourceListener(value.Name, this.handlers[value.Name](this.tracer, this.sampler));
                        dl.Subscription = value.Subscribe(dl);
                        return dl;
                    });
                }
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref this.disposed, 1, 0) == 1)
            {
                return;
            }

            var subsCopy = this.subscriptions;
            this.subscriptions = null;

            var keys = subsCopy.Keys;
            foreach (var key in keys)
            {
                if (subsCopy.TryRemove(key, out var sub))
                {
                    sub?.Dispose();
                }
            }

            this.subscription?.Dispose();
            this.subscription = null;
        }
    }
}
