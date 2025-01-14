using System;
using System.Collections.Generic;
using System.Linq;
using Datadog.Trace.Ci;
using Datadog.Trace.Vendors.Newtonsoft.Json;

namespace FeatureTracking
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                CIAppFeatureTracking();
                return;
            }

            switch (args[0])
            {
                case "ciapp":
                    CIAppFeatureTracking();
                    break;
                default:
                    break;
            }
        }

        private static IEnumerable<string> GetFeatureTrackingValueFromType(params Type[] types)
        {
            return types
                .SelectMany(type => type.GetFields())
                .Where(f => f.GetCustomAttributes(typeof(FeatureTrackingAttribute), true).Length > 0)
                .Select(f => f.GetValue(null).ToString())
                .OrderBy(v => v);
        }

        private static void CIAppFeatureTracking()
        {
            var values = GetFeatureTrackingValueFromType(typeof(CommonTags), typeof(TestTags));
            var json = JsonConvert.SerializeObject(values);
            Console.WriteLine(json);
        }
    }
}
