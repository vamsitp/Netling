using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Netling.Core.Extensions
{
    public static class GenericExtensions
    {
        private const string RequestId = "Request-Id";
        private const string OperationId = "operation_Id";

        public static void AddTraceId(this Dictionary<string, string> headers, string traceId = null)
        {
            if (headers == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(traceId))
            {
                traceId = Guid.NewGuid().ToString();
            }

            // headers.Add(OperationId, traceId); // Use W3C format instead of GUID
            if (headers.ContainsKey(RequestId) == true)
            {
                headers[RequestId] = traceId;
            }
            else
            {
                headers.Add(RequestId, traceId);
            }

            Console.WriteLine($"{nameof(traceId)}: {traceId}");
            Debug.WriteLine($"{nameof(traceId)}: {traceId}");
        }
    }
}
