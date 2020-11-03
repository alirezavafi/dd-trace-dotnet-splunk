using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Datadog.Trace.DuckTyping;
using Datadog.Trace.ExtensionMethods;
using Datadog.Trace.Logging;
using Datadog.Trace.Vendors.Newtonsoft.Json.Utilities;

namespace Datadog.Trace.ClrProfiler.CallTarget
{
    internal class CallTargetInvokerHandler
    {
        private const string BeginMethodName = "OnMethodBegin";
        private const string EndMethodName = "OnMethodEnd";
        private const string EndAsyncMethodName = "OnAsyncMethodEnd";

        private static readonly Vendors.Serilog.ILogger Log = DatadogLogging.GetLogger(typeof(CallTargetInvokerHandler));

        internal static DynamicMethod CreateBeginMethodDelegate(Type integrationType, Type targetType, Type[] argumentsTypes)
        {
            Log.Information($"Creating BeginMethod Dynamic Method for '{integrationType.FullName}' integration. [Target={targetType.FullName}]");
            MethodInfo onMethodBeginMethodInfo = integrationType.GetMethod(BeginMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (onMethodBeginMethodInfo is null)
            {
                throw new NullReferenceException($"Couldn't find the method: {BeginMethodName} in type: {integrationType.FullName}");
            }

            if (onMethodBeginMethodInfo.ReturnType != typeof(CallTargetState))
            {
                throw new ArgumentException($"The return type of the method: {BeginMethodName} in type: {integrationType.FullName} is not {nameof(CallTargetState)}");
            }

            Type[] genericArgumentsTypes = onMethodBeginMethodInfo.GetGenericArguments();
            if (genericArgumentsTypes.Length < 1)
            {
                throw new ArgumentException($"The method: {BeginMethodName} in type: {integrationType.FullName} doesn't have the generic type for the instance type.");
            }

            ParameterInfo[] onMethodBeginParameters = onMethodBeginMethodInfo.GetParameters();
            if (onMethodBeginParameters.Length < argumentsTypes.Length)
            {
                throw new ArgumentException($"The method: {BeginMethodName} with {onMethodBeginParameters.Length} paremeters in type: {integrationType.FullName} has less parameters than required.");
            }
            else if (onMethodBeginParameters.Length > argumentsTypes.Length + 1)
            {
                throw new ArgumentException($"The method: {BeginMethodName} with {onMethodBeginParameters.Length} paremeters in type: {integrationType.FullName} has more parameters than required.");
            }
            else if (onMethodBeginParameters.Length != argumentsTypes.Length && onMethodBeginParameters[0].ParameterType != genericArgumentsTypes[0])
            {
                throw new ArgumentException($"The first generic argument for method: {BeginMethodName} in type: {integrationType.FullName} must be the same as the first parameter for the instance value.");
            }

            List<Type> callGenericTypes = new List<Type>();

            bool mustLoadInstance = onMethodBeginParameters.Length != argumentsTypes.Length;
            Type instanceGenericType = genericArgumentsTypes[0];
            Type instanceGenericConstraint = instanceGenericType.GetGenericParameterConstraints().FirstOrDefault();
            Type instanceProxyType = null;
            if (instanceGenericConstraint != null)
            {
                var result = DuckType.GetOrCreateProxyType(instanceGenericConstraint, targetType);
                instanceProxyType = result.ProxyType;
                callGenericTypes.Add(instanceProxyType);
            }
            else
            {
                callGenericTypes.Add(targetType);
            }

            DynamicMethod callMethod = new DynamicMethod(
                     $"{onMethodBeginMethodInfo.DeclaringType.Name}.{onMethodBeginMethodInfo.Name}",
                     typeof(CallTargetState),
                     new Type[] { targetType }.Concat(argumentsTypes),
                     onMethodBeginMethodInfo.Module,
                     true);

            ILGenerator ilWriter = callMethod.GetILGenerator();

            // Load the instance if is needed
            if (mustLoadInstance)
            {
                ilWriter.Emit(OpCodes.Ldarg_0);
                if (instanceGenericConstraint != null)
                {
                    ilWriter.Emit(OpCodes.Newobj, instanceProxyType.GetConstructor(new Type[] { targetType }));
                }
            }

            // Load arguments
            for (var i = mustLoadInstance ? 1 : 0; i < onMethodBeginParameters.Length; i++)
            {
                Type sourceParameterType = argumentsTypes[mustLoadInstance ? i - 1 : i];
                Type targetParameterType = onMethodBeginParameters[i].ParameterType;
                Type parameterProxyType = null;

                if (targetParameterType.IsGenericParameter)
                {
                    targetParameterType = genericArgumentsTypes[targetParameterType.GenericParameterPosition];
                    Type targetParameterTypeConstraint = targetParameterType.GetGenericParameterConstraints().FirstOrDefault(pType => pType != typeof(IDuckType));
                    if (targetParameterTypeConstraint is null)
                    {
                        callGenericTypes.Add(sourceParameterType);
                    }
                    else
                    {
                        var result = DuckType.GetOrCreateProxyType(targetParameterTypeConstraint, sourceParameterType);
                        parameterProxyType = result.ProxyType;
                        callGenericTypes.Add(parameterProxyType);
                    }
                }
                else if (!targetParameterType.IsAssignableFrom(sourceParameterType))
                {
                    throw new InvalidCastException($"The target parameter {targetParameterType} can't be assigned from {sourceParameterType}");
                }

                ILHelpersExtensions.WriteLoadArgument(ilWriter, i, mustLoadInstance);
                if (parameterProxyType != null)
                {
                    ilWriter.Emit(OpCodes.Newobj, parameterProxyType.GetConstructor(new Type[] { sourceParameterType }));
                }
            }

            // Call method
            Log.Information("Generic Types: " + string.Join(", ", callGenericTypes.Select(t => t.FullName)));
            Log.Information("Method: " + onMethodBeginMethodInfo);
            onMethodBeginMethodInfo = onMethodBeginMethodInfo.MakeGenericMethod(callGenericTypes.ToArray());
            Log.Information("Method: " + onMethodBeginMethodInfo);
            ilWriter.EmitCall(OpCodes.Call, onMethodBeginMethodInfo, null);
            ilWriter.Emit(OpCodes.Ret);

            Log.Information($"Created BeginMethod Dynamic Method for '{integrationType.FullName}' integration. [Target={targetType.FullName}]");
            return callMethod;
        }

        private static TTo ConvertType<TFrom, TTo>(TFrom value)
        {
            return default;
            /*if (value is null || typeof(TTo) == typeof(object))
            {
                return (TTo)(object)value;
            }

            Type valueType = value.GetType();
            if (valueType == conversionType || conversionType.IsAssignableFrom(valueType))
            {
                return value;
            }

            // Finally we try to duck type
            return DuckType.Create<TTo>((object)value);*/
        }

        internal static class IntegrationOptions<TIntegration, TTarget>
        {
            private static readonly Vendors.Serilog.ILogger Log = DatadogLogging.GetLogger(typeof(IntegrationOptions<TIntegration, TTarget>));

            private static volatile bool _disableIntegration = false;

            internal static bool IsIntegrationEnabled => !_disableIntegration;

            internal static void DisableIntegration() => _disableIntegration = true;

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static void LogException(Exception exception)
            {
                Log.SafeLogError(exception, exception?.Message, null);
                if (exception is DuckTypeException)
                {
                    Log.Warning($"DuckTypeException has been detected, the integration <{typeof(TIntegration)}, {typeof(TTarget)}> will be disabled.");
                    _disableIntegration = true;
                }
                else if (exception is CallTargetInvokerException)
                {
                    Log.Warning($"CallTargetInvokerException has been detected, the integration <{typeof(TIntegration)}, {typeof(TTarget)}> will be disabled.");
                    _disableIntegration = true;
                }
            }
        }

        internal static class BeginMethodHandler<TIntegration, TTarget>
        {
            private static InvokeDelegate invokeDelegate = instance => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), Util.ArrayHelper.Empty<Type>());
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance)
                => invokeDelegate(instance);
        }

        internal static class BeginMethodHandler<TIntegration, TTarget, TArg1>
        {
            private static InvokeDelegate invokeDelegate = (instance, arg1) => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), new[] { typeof(TArg1) });
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance, TArg1 arg1);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, TArg1 arg1)
                => invokeDelegate(instance, arg1);
        }

        internal static class BeginMethodHandler<TIntegration, TTarget, TArg1, TArg2>
        {
            private static InvokeDelegate invokeDelegate = (instance, arg1, arg2) => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), new[] { typeof(TArg1), typeof(TArg2) });
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance, TArg1 arg1, TArg2 arg2);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, TArg1 arg1, TArg2 arg2)
                => invokeDelegate(instance, arg1, arg2);
        }

        internal static class BeginMethodHandler<TIntegration, TTarget, TArg1, TArg2, TArg3>
        {
            private static InvokeDelegate invokeDelegate = (instance, arg1, arg2, arg3) => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3) });
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3)
                => invokeDelegate(instance, arg1, arg2, arg3);
        }

        internal static class BeginMethodHandler<TIntegration, TTarget, TArg1, TArg2, TArg3, TArg4>
        {
            private static InvokeDelegate invokeDelegate = (instance, arg1, arg2, arg3, arg4) => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4) });
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
                => invokeDelegate(instance, arg1, arg2, arg3, arg4);
        }

        internal static class BeginMethodHandler<TIntegration, TTarget, TArg1, TArg2, TArg3, TArg4, TArg5>
        {
            private static InvokeDelegate invokeDelegate = (instance, arg1, arg2, arg3, arg4, arg5) => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5) });
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5)
                => invokeDelegate(instance, arg1, arg2, arg3, arg4, arg5);
        }

        internal static class BeginMethodHandler<TIntegration, TTarget, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>
        {
            private static InvokeDelegate invokeDelegate = (instance, arg1, arg2, arg3, arg4, arg5, arg6) => CallTargetState.GetDefault();

            static BeginMethodHandler()
            {
                try
                {
                    DynamicMethod dynMethod = CreateBeginMethodDelegate(typeof(TIntegration), typeof(TTarget), new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6) });
                    if (dynMethod != null)
                    {
                        invokeDelegate = (InvokeDelegate)dynMethod.CreateDelegate(typeof(InvokeDelegate));
                    }
                }
                catch (Exception ex)
                {
                    throw new CallTargetInvokerException(ex);
                }
            }

            internal delegate CallTargetState InvokeDelegate(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
                => invokeDelegate(instance, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        internal static class BeginMethodSlowHandler<TIntegration, TTarget>
        {
            private static InvokeDelegate invokeDelegate = (instance, arguments) => CallTargetState.GetDefault();

            internal delegate CallTargetState InvokeDelegate(TTarget instance, object[] arguments);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetState Invoke(TTarget instance, object[] arguments)
                => invokeDelegate(instance, arguments);
        }

        internal static class EndMethodHandler<TIntegration, TTarget>
        {
            private static InvokeDelegate invokeDelegate = (instance, exception, state) => CallTargetReturn.GetDefault();

            internal delegate CallTargetReturn InvokeDelegate(TTarget instance, Exception exception, CallTargetState state);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetReturn Invoke(TTarget instance, Exception exception, CallTargetState state)
                => invokeDelegate(instance, exception, state);
        }

        internal static class EndMethodHandler<TIntegration, TTarget, TReturn>
        {
            private static InvokeDelegate invokeDelegate = (instance, returnValue, exception, state) => new CallTargetReturn<TReturn>(returnValue);

            internal delegate CallTargetReturn<TReturn> InvokeDelegate(TTarget instance, TReturn returnValue, Exception exception, CallTargetState state);

#if NETCOREAPP3_1 || NET5_0
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
            internal static CallTargetReturn<TReturn> Invoke(TTarget instance, TReturn returnValue, Exception exception, CallTargetState state)
                => invokeDelegate(instance, returnValue, exception, state);
        }
    }
}