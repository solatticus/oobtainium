﻿using Microsoft.Extensions.Logging;
using System;

namespace OoBDev.Oobtainium
{
    public class CaptureProxyFactory : ICaptureProxyFactory
    {
        private readonly IServiceProvider? _serviceProvider;
        public CaptureProxyFactory(IServiceProvider? serviceProvider = null) => _serviceProvider = serviceProvider;

        public T Create<T>(ICallRecorder? recorder = null, ICallHandler? handler = null, ILogger<T>? logger = null) =>
            CaptureProxy<T>.Create(
                handler ?? _serviceProvider?.GetService(typeof(ICallHandler)) as ICallHandler
,
                recorder ?? _serviceProvider?.GetService(typeof(ICallRecorder)) as ICallRecorder,
                logger ?? _serviceProvider?.GetService(typeof(ILogger<T>)) as ILogger<T>);
    }
}