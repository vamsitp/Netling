﻿using System;
using Netling.Core.SocketWorker.Extensions;

namespace Netling.Core.SocketWorker.Performance
{
    public static class HttpHelperContentLength
    {
        public static int GetHeaderContentLength(ReadOnlySpan<byte> buffer)
        {
            HttpHelper.SeekHeader(buffer, HttpHeaders.ContentLength.Span, out var index, out var length);
            return ByteExtensions.ConvertToInt(buffer.Slice(index, length));
        }
        
        public static int GetResponseLength(ReadOnlySpan<byte> buffer)
        {
            var headerEnd = HttpHelper.SeekHeaderEnd(buffer);

            if (headerEnd < 0)
            {
                return -1;
            }

            var contentLength = GetHeaderContentLength(buffer);
            return headerEnd + 4 + contentLength;
        }
    }
}
