﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;

namespace OoBDev.Oobtainium
{
    public class CaptureProxy<I> : DispatchProxy
    {
        private ICallRecorder? _capture;
        private ICallHandler? _handler;
        private ILogger<I>? _logger;

        private readonly ConcurrentDictionary<string, object?> _backingStore = new ConcurrentDictionary<string, object?>();

        protected override object? Invoke(MethodInfo targetMethod, object[] args)
        {
            _logger?.LogInformation($"{targetMethod}");

            var response = _handler?.Invoke(targetMethod, args);
            var captured = response;

            if (response is Task)
            {
                _logger?.LogDebug($"{_handler} response is Task so await result");
                var awaited = (Task)response;
                awaited.GetAwaiter().GetResult();

                var awaitedType = response.GetType();
                if (awaitedType.IsGenericType)
                {
                    _logger?.LogDebug($"{_handler} is a Task<> so unwrap result");
                    captured = awaitedType.GetProperty("Result")?.GetValue(response, null);
                }
                else
                {
                    _logger?.LogDebug($"{_handler} is a Task<void> change capture to null");
                    captured = default;
                }
            }

            //if the method is a property act like you have a backing store
            if (targetMethod.IsSpecialName)
            {
                _logger?.LogDebug($"{targetMethod} is special");
                if (targetMethod.Name.StartsWith("set_"))
                {
                    var key = targetMethod.Name[4..] + (args.Length > 1 ? '[' + string.Join(';', args[..^1]) + ']' : "");
                    var value = args.Length == 0 ? args[0]: args[^1];

                    //TODO: look at an indexer
                    _logger?.LogDebug($"{key} is acting like a setter");
                    _backingStore.AddOrUpdate(key, value, (k, v) => value);
                }
                else if (targetMethod.Name.StartsWith("get_"))
                {
                    var key = targetMethod.Name[4..] + (args.Length > 0 ? '[' + string.Join(';', args) + ']' : "");
                    _logger?.LogDebug($"{key} is acting like a getter");
                    _backingStore.TryGetValue(key, out captured);
                }
                else
                {
                    _logger?.LogDebug($"{targetMethod} is not that special");
                }
            }


            //Capture response

            if (this == _capture)
            {
                _logger?.LogDebug($"Interception inception so bypass");
            }
            else
            {
                _capture?.Capture?.Invoke(this, typeof(I), targetMethod, args, captured);
            }

            //TODO: add type converter support

            if (targetMethod.ReturnType == null || targetMethod.ReturnType == typeof(void))
            {
                return null;
            }
            else if (targetMethod.ReturnType.IsInstanceOfType(captured))
            {
                return captured;
            }
            else if (captured is Delegate)
            {
                return captured;
            }
            else if (typeof(Task).IsAssignableFrom(targetMethod.ReturnType))
            {
                var taskType = targetMethod.ReturnType;
                if (taskType.IsGenericType)
                {
                    _logger?.LogDebug($"{targetMethod}.ReturnType is a Task<> so rebuild Task<>");

                    //rewrap captured value
                    var taskReturnType = taskType.GetGenericArguments()[0];
                    if (taskReturnType == typeof(string) && !(captured is string))
                    {
                        if (captured is byte[])
                        {
                            captured = Convert.ToBase64String((byte[])captured);
                        }
                        else
                        {
                            captured = captured?.ToString();
                        }
                    }
                    else if (!taskReturnType.IsInstanceOfType(captured))
                    {
                        _logger?.LogDebug($"{captured} not assignable so get default value instead");
                        captured = taskReturnType.GetDefaultValue();
                    }

                    var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult), BindingFlags.Static | BindingFlags.Public)?.MakeGenericMethod(taskReturnType)
                        ?? throw new NullReferenceException("Unable to resolve Task.FromResult<>");
                    var result = fromResult.Invoke(null, new[] { captured });
                    return result;
                }
                else
                {
                    _logger?.LogDebug($"{targetMethod}.ReturnType is a Task so return completed");
                    return Task.CompletedTask;
                }
            }
            // can is cast?
            // operator over load
            // convert?
            else
            {
                var @default = targetMethod.ReturnType.GetDefaultValue();
                return @default;
            }
        }


        internal static I Create(ICallHandler? intermediate = null, ICallRecorder? capture = null, ILogger<I>? logger = null)
        {
            object? proxy = Create<I, CaptureProxy<I>>();
            if (proxy != null)
            {
                var unwrapped = (CaptureProxy<I>)proxy;
                unwrapped._capture = capture;
                unwrapped._logger = logger;
                unwrapped._handler = intermediate;
            }
#pragma warning disable CS8603 // Possible null reference return.
            return (I)proxy;
#pragma warning restore CS8603 // Possible null reference return.
        }
    }

}