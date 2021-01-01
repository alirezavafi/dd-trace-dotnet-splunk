using System.Collections.Generic;
using System.Text;
using System.Threading;
using Datadog.Trace.Util;
using Datadog.Trace.Vendors.MessagePack;

namespace Datadog.Trace.Tagging
{
    /// <summary>
    /// Tags List
    /// </summary>
    public abstract class TagsList : ITags
    {
        private List<KeyValuePair<string, double>> _metrics;
        private List<KeyValuePair<string, string>> _tags;

        /// <summary>
        /// Initializes a new instance of the <see cref="TagsList"/> class.
        /// </summary>
        protected TagsList()
        {
            this.Metrics = Volatile.Read(ref _metrics);
            this.Tags = Volatile.Read(ref _tags);
        }

        /// <summary>
        /// Gets Metrics
        /// </summary>
        public List<KeyValuePair<string, double>> Metrics { get; }

        /// <summary>
        /// Gets Tags
        /// </summary>
        public List<KeyValuePair<string, string>> Tags { get; }

        /// <summary>
        /// Get tag value by key
        /// </summary>
        /// <param name="key">tag</param>
        /// <returns>value</returns>
        public string GetTag(string key)
        {
            foreach (var property in GetAdditionalTags())
            {
                if (property.Key == key)
                {
                    return property.Getter(this);
                }
            }

            var tags = Tags;

            if (tags == null)
            {
                return null;
            }

            lock (tags)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    if (tags[i].Key == key)
                    {
                        return tags[i].Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get metric by key
        /// </summary>
        /// <param name="key">metric</param>
        /// <returns>value</returns>
        public double? GetMetric(string key)
        {
            foreach (var property in GetAdditionalMetrics())
            {
                if (property.Key == key)
                {
                    return property.Getter(this);
                }
            }

            var metrics = Metrics;

            if (metrics == null)
            {
                return null;
            }

            lock (metrics)
            {
                for (int i = 0; i < metrics.Count; i++)
                {
                    if (metrics[i].Key == key)
                    {
                        return metrics[i].Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sets tag value
        /// </summary>
        /// <param name="key">tag</param>
        /// <param name="value">value</param>
        public void SetTag(string key, string value)
        {
            foreach (var property in GetAdditionalTags())
            {
                if (property.Key == key)
                {
                    property.Setter(this, value);
                    return;
                }
            }

            var tags = Tags;

            if (tags == null)
            {
                var newTags = new List<KeyValuePair<string, string>>();
                tags = Interlocked.CompareExchange(ref _tags, newTags, null) ?? newTags;
            }

            lock (tags)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    if (tags[i].Key == key)
                    {
                        if (value == null)
                        {
                            tags.RemoveAt(i);
                        }
                        else
                        {
                            tags[i] = new KeyValuePair<string, string>(key, value);
                        }

                        return;
                    }
                }

                // If we get there, the tag wasn't in the collection
                if (value != null)
                {
                    tags.Add(new KeyValuePair<string, string>(key, value));
                }
            }
        }

        /// <summary>
        /// Set metric
        /// </summary>
        /// <param name="key">metric</param>
        /// <param name="value">value</param>
        public void SetMetric(string key, double? value)
        {
            foreach (var property in GetAdditionalMetrics())
            {
                if (property.Key == key)
                {
                    property.Setter(this, value);
                    return;
                }
            }

            var metrics = Metrics;

            if (metrics == null)
            {
                var newMetrics = new List<KeyValuePair<string, double>>();
                metrics = Interlocked.CompareExchange(ref _metrics, newMetrics, null) ?? newMetrics;
            }

            lock (metrics)
            {
                for (int i = 0; i < metrics.Count; i++)
                {
                    if (metrics[i].Key == key)
                    {
                        if (value == null)
                        {
                            metrics.RemoveAt(i);
                        }
                        else
                        {
                            metrics[i] = new KeyValuePair<string, double>(key, value.Value);
                        }

                        return;
                    }
                }

                // If we get there, the tag wasn't in the collection
                if (value != null)
                {
                    metrics.Add(new KeyValuePair<string, double>(key, value.Value));
                }
            }
        }

        /// <summary>
        /// Serialize to byte array
        /// </summary>
        /// <param name="bytes">array ref</param>
        /// <param name="offset">start offset</param>
        /// <returns>offset</returns>
        public int SerializeTo(ref byte[] bytes, int offset)
        {
            int originalOffset = offset;

            offset += WriteTags(ref bytes, offset);
            offset += WriteMetrics(ref bytes, offset);

            return offset - originalOffset;
        }

        /// <summary>
        /// Represent this object as string
        /// </summary>
        /// <returns>string representation</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();

            var tags = Tags;

            if (tags != null)
            {
                lock (tags)
                {
                    foreach (var pair in tags)
                    {
                        sb.Append($"{pair.Key} (tag):{pair.Value},");
                    }
                }
            }

            var metrics = Metrics;

            if (metrics != null)
            {
                lock (metrics)
                {
                    foreach (var pair in metrics)
                    {
                        sb.Append($"{pair.Key} (metric):{pair.Value}");
                    }
                }
            }

            foreach (var property in GetAdditionalTags())
            {
                var value = property.Getter(this);

                if (value != null)
                {
                    sb.Append($"{property.Key} (tag):{value},");
                }
            }

            foreach (var property in GetAdditionalMetrics())
            {
                var value = property.Getter(this);

                if (value != null)
                {
                    sb.Append($"{property.Key} (metric):{value.Value},");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get additional tags
        /// </summary>
        /// <returns>tags</returns>
        protected virtual IProperty<string>[] GetAdditionalTags() => ArrayHelper.Empty<IProperty<string>>();

        /// <summary>
        /// Get additional metrics
        /// </summary>
        /// <returns>metrics</returns>
        protected virtual IProperty<double?>[] GetAdditionalMetrics() => ArrayHelper.Empty<IProperty<double?>>();

        private int WriteTags(ref byte[] bytes, int offset)
        {
            int originalOffset = offset;

            offset += MessagePackBinary.WriteString(ref bytes, offset, "meta");

            int count = 0;

            var tags = Tags;
            var additionalTags = GetAdditionalTags();

            foreach (var property in additionalTags)
            {
                if (property.Getter(this) != null)
                {
                    count++;
                }
            }

            if (tags != null)
            {
                lock (tags)
                {
                    count += tags.Count;

                    offset += MessagePackBinary.WriteMapHeader(ref bytes, offset, count);

                    foreach (var pair in tags)
                    {
                        offset += MessagePackBinary.WriteString(ref bytes, offset, pair.Key);
                        offset += MessagePackBinary.WriteString(ref bytes, offset, pair.Value);
                    }
                }
            }
            else
            {
                offset += MessagePackBinary.WriteMapHeader(ref bytes, offset, count);
            }

            foreach (var property in additionalTags)
            {
                var value = property.Getter(this);

                if (value != null)
                {
                    offset += MessagePackBinary.WriteString(ref bytes, offset, property.Key);
                    offset += MessagePackBinary.WriteString(ref bytes, offset, value);
                }
            }

            return offset - originalOffset;
        }

        private int WriteMetrics(ref byte[] bytes, int offset)
        {
            int originalOffset = offset;

            offset += MessagePackBinary.WriteString(ref bytes, offset, "metrics");

            int count = 0;

            var metrics = Metrics;
            var additionalMetrics = GetAdditionalMetrics();

            foreach (var property in additionalMetrics)
            {
                if (property.Getter(this) != null)
                {
                    count++;
                }
            }

            if (metrics != null)
            {
                lock (metrics)
                {
                    count += metrics.Count;

                    offset += MessagePackBinary.WriteMapHeader(ref bytes, offset, count);

                    foreach (var pair in metrics)
                    {
                        offset += MessagePackBinary.WriteString(ref bytes, offset, pair.Key);
                        offset += MessagePackBinary.WriteDouble(ref bytes, offset, pair.Value);
                    }
                }
            }
            else
            {
                offset += MessagePackBinary.WriteMapHeader(ref bytes, offset, count);
            }

            foreach (var property in GetAdditionalMetrics())
            {
                var value = property.Getter(this);

                if (value != null)
                {
                    offset += MessagePackBinary.WriteString(ref bytes, offset, property.Key);
                    offset += MessagePackBinary.WriteDouble(ref bytes, offset, value.Value);
                }
            }

            return offset - originalOffset;
        }
    }
}
