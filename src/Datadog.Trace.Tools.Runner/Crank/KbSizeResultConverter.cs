namespace Datadog.Trace.Tools.Runner.Crank
{
    internal readonly struct KbSizeResultConverter : IResultConverter
    {
        public readonly string Name;

        public KbSizeResultConverter(string name)
        {
            Name = name;
        }

        public bool CanConvert(string name)
            => Name == name;

        public void SetToSpan(Span span, string sanitizedName, object value)
        {
            if (double.TryParse(value.ToString(), out var doubleValue))
            {
                span.SetMetric(sanitizedName + "_bytes", doubleValue * 1024);
            }
            else
            {
                span.SetTag(sanitizedName, value.ToString());
            }
        }
    }
}
