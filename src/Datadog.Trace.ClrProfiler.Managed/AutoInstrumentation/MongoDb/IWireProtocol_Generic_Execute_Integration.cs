using System;
using System.Threading;
using Datadog.Trace.ClrProfiler.CallTarget;
using Datadog.Trace.ClrProfiler.Integrations;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.MongoDb
{
    /// <summary>
    /// MongoDB.Driver.Core.WireProtocol.IWireProtocol&lt;TResult&gt; instrumentation
    /// </summary>
    [MongoDbExecute(
        typeName: "MongoDB.Driver.Core.WireProtocol.CommandUsingQueryMessageWireProtocol`1",
        isGeneric: true)]
    [MongoDbExecute(
        typeName: "MongoDB.Driver.Core.WireProtocol.CommandUsingCommandMessageWireProtocol`1",
        isGeneric: true)]
    [MongoDbExecute(
        typeName: "MongoDB.Driver.Core.WireProtocol.CommandWireProtocol`1",
        isGeneric: true)]
    [MongoDbExecute(
        typeName: "MongoDB.Driver.Core.WireProtocol.GetMoreWireProtocol`1",
        isGeneric: true)]
    [MongoDbExecute(
        typeName: "MongoDB.Driver.Core.WireProtocol.QueryWireProtocol`1",
        isGeneric: true)]
    [MongoDbExecute(
        typeName: "MongoDB.Driver.Core.WireProtocol.WriteWireProtocolBase`1",
        isGeneric: true)]
    // ReSharper disable once InconsistentNaming
    public class IWireProtocol_Generic_Execute_Integration
    {
        /// <summary>
        /// OnMethodBegin callback
        /// </summary>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="connection">The MongoDB connection</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <returns>Calltarget state value</returns>
        public static CallTargetState OnMethodBegin<TTarget>(TTarget instance, object connection, CancellationToken cancellationToken)
        {
            var scope = MongoDbIntegration.CreateScope(instance, connection);

            return new CallTargetState(scope);
        }

        /// <summary>
        /// OnAsyncMethodEnd callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <typeparam name="TReturn">Type of the return value</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="returnValue">Return value</param>
        /// <param name="exception">Exception instance in case the original code threw an exception.</param>
        /// <param name="state">Calltarget state value</param>
        /// <returns>A response value, in an async scenario will be T of Task of T</returns>
        public static CallTargetReturn<TReturn> OnMethodEnd<TTarget, TReturn>(TTarget instance, TReturn returnValue, Exception exception, CallTargetState state)
        {
            var scope = state.Scope;

            scope.DisposeWithException(exception);

            return new CallTargetReturn<TReturn>(returnValue);
        }
    }
}
