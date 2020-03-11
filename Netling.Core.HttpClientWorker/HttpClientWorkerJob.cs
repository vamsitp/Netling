using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Netling.Core.Extensions;
using Netling.Core.Models;

namespace Netling.Core.HttpClientWorker
{
    public class HttpClientWorkerJob : IWorkerJob
    {
        private readonly int _index;
        private readonly Uri _uri;
        private readonly Stopwatch _stopwatch;
        private readonly Stopwatch _localStopwatch;
        private readonly WorkerThreadResult _workerThreadResult;
        private readonly Dictionary<string, string> _headers;
        private readonly HttpClient _httpClient;

        // Used to approximately calculate bandwidth
        private static readonly int MissingHeaderLength = "HTTP/1.1 200 OK\r\nContent-Length: 123\r\nContent-Type: text/plain\r\n\r\n".Length;

        public HttpClientWorkerJob(Uri uri, Dictionary<string, string> headers)
        {
            _uri = uri;
            _headers = headers;
        }

        private HttpClientWorkerJob(int index, Uri uri, Dictionary<string, string> headers, WorkerThreadResult workerThreadResult)
        {
            _index = index;
            _uri = uri;
            _headers = headers;
            _stopwatch = Stopwatch.StartNew();
            _localStopwatch = new Stopwatch();
            _workerThreadResult = workerThreadResult;
            _httpClient = new HttpClient();
        }

        public async ValueTask DoWork()
        {
            _localStopwatch.Restart();
            SetHeaders();
            using (var response = await _httpClient.GetAsync(_uri))
            {
                var contentStream = await response.Content.ReadAsStreamAsync();
                var length = contentStream.Length + response.Headers.ToString().Length + MissingHeaderLength;
                var responseTime = (float)_localStopwatch.ElapsedTicks / Stopwatch.Frequency * 1000;
                var statusCode = (int)response.StatusCode;

                if (statusCode < 400)
                {
                    _workerThreadResult.Add((int)_stopwatch.ElapsedMilliseconds / 1000, length, responseTime, statusCode, _index < 10);
                }
                else
                {
                    _workerThreadResult.AddError((int)_stopwatch.ElapsedMilliseconds / 1000, responseTime, statusCode, _index < 10);
                }
            }
        }

        private void SetHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _headers.AddTraceId();
            foreach (var header in _headers)
            {
                _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }

        public WorkerThreadResult GetResults()
        {
            return _workerThreadResult;
        }

        public ValueTask<IWorkerJob> Init(int index, WorkerThreadResult workerThreadResult)
        {
            return new ValueTask<IWorkerJob>(new HttpClientWorkerJob(index, _uri, _headers, workerThreadResult));
        }
    }
}