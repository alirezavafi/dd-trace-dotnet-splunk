using System;
using System.Reflection;

namespace Datadog.Trace.ClrProfiler.DuckTyping
{
    /// <summary>
    /// Duck kind
    /// </summary>
    public enum DuckKind
    {
        /// <summary>
        /// Property
        /// </summary>
        Property,

        /// <summary>
        /// Field
        /// </summary>
        Field
    }

    /// <summary>
    /// Duck attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false)]
    public class DuckAttribute : Attribute
    {
        /// <summary>
        /// All flags for static, non static, public and non public members
        /// </summary>
        public const BindingFlags AllFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Gets or sets the underlying type member name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets binding flags
        /// </summary>
        public BindingFlags BindingFlags { get; set; } = AllFlags;

        /// <summary>
        /// Gets or sets duck kind
        /// </summary>
        public DuckKind Kind { get; set; } = DuckKind.Property;

        /// <summary>
        /// Gets or sets the generic parameter type names definition for a generic method call
        /// </summary>
        public string[] GenericParameterTypeNames { get; set; }
    }
}