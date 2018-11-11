﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Elastic.Agent.Core.DiagnosticSource;
using Elastic.Agent.Core.Model.Payload;

namespace Elastic.Agent.Core.DiagnosticListeners
{
    public class HttpDiagnosticListener : IDiagnosticListener
    {
        public string Name => "HttpHandlerDiagnosticListener";

        private Config _agentConfig;

        public HttpDiagnosticListener(Config config)
        {
            _agentConfig = config;
        }

        //TODO: find better way to keep track of respones
        private readonly ConcurrentDictionary<HttpRequestMessage, DateTime> _startedRequests = new ConcurrentDictionary<HttpRequestMessage, DateTime>();

        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        public void OnNext(KeyValuePair<string, object> kv)
        {
            var request = kv.Value.GetType().GetTypeInfo().GetDeclaredProperty("Request")?.GetValue(kv.Value) as HttpRequestMessage;

            if (IsRequestFiltered(request?.RequestUri))
            {
                return;
            }

            switch (kv.Key)
            {
                case "System.Net.Http.HttpRequestOut.Start": //TODO: look for consts
                    if (request != null)
                    {
                        var added = _startedRequests.TryAdd(request, DateTime.UtcNow);
                    }
                    break;

                case "System.Net.Http.HttpRequestOut.Stop":
                    var response = kv.Value.GetType().GetTypeInfo().GetDeclaredProperty("Response").GetValue(kv.Value) as HttpResponseMessage;
                    var requestTaskStatusObj = (TaskStatus)kv.Value.GetType().GetTypeInfo().GetDeclaredProperty("RequestTaskStatus").GetValue(kv.Value);
                    var requestTaskStatus = (TaskStatus)requestTaskStatusObj;

                    var transactionStartTime = TransactionContainer.Transactions.Value[0].TimestampInDateTime;
                    var utcNow = DateTime.UtcNow;

                    var http = new Http
                    {
                        Url = request.RequestUri.ToString(),
                        Method = request.Method.Method,
                    };

                    //TODO: response can be null if for example the request Task is Faulted. 
                    //E.g. writing this from an airplane without internet, and requestTaskStatus is "Faulted" and response is null
                    //How do we report this? There is no response code in that case.
                    if (response != null) 
                    {
                        http.Status_code = (int)response.StatusCode;
                    }

                    var span = new Span
                    {
                        Start = (decimal)(utcNow - transactionStartTime).TotalMilliseconds,
                        Name = $"{request.Method} {request.RequestUri.ToString()}",
                        Type = "Http",
                        Context = new Span.ContextC
                        {
                            Http = http
                        }
                    };

                    if (_startedRequests.TryRemove(request, out DateTime requestStart))
                    {
                        var requestDuration = DateTime.UtcNow - requestStart; //TODO: there are better ways
                        span.Duration = requestDuration.TotalMilliseconds;
                    }

                    TransactionContainer.Transactions.Value[0].Spans.Add(span);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Tells if the given request should be filtered from being captured. 
        /// </summary>
        /// <returns><c>true</c>, if request should not be captured, <c>false</c> otherwise.</returns>
        /// <param name="requestUri">Request URI. Can be null, which is not filtered</param>
        private bool IsRequestFiltered(Uri requestUri)
        {
            if (requestUri == null)
            {
                return false;
            }

            return _agentConfig.ServerUri.IsBaseOf(requestUri) ? true : false;
        }
    }
}
