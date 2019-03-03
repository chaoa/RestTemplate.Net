using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using Polly;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using DnsClient;
using Polly.Wrap;
using Polly.Timeout;

namespace RestTemplate
{
    public class RestHttpClient : IHttpClient
    {
        private readonly HttpClient _httpClient;
        private ConcurrentDictionary<string, AsyncPolicyWrap> _policyWraps;//polly容器
        private ConcurrentDictionary<string, List<string>> _hosts;//缓存服务注册表，定时刷新

        private readonly ILogger<RestHttpClient> _logger;
        private readonly PolicyOptions _policyOptions;
        private IDnsQuery _dnsQuery;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RestHttpClient(ILoggerFactory loggerFactory, PolicyOptions policyOptions, IDnsQuery dnsQuery, IHttpContextAccessor httpContextAccessor)
        {
            _httpClient=new HttpClient();
            _policyWraps = new ConcurrentDictionary<string, AsyncPolicyWrap>();
            _hosts=new ConcurrentDictionary<string, List<string>>();

            _logger = loggerFactory.CreateLogger<RestHttpClient>();
            _policyOptions = policyOptions;
            _dnsQuery = dnsQuery;
            _httpContextAccessor = httpContextAccessor;
        }


        public async Task<RestResponse<T>> DeleteAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            return await SendAsync<T>(url, HttpMethod.Delete, requestHeaders, body);
        }

        public async Task<RestResponse<T>> GetAsync<T>(string url, HttpRequestHeaders requestHeaders = null)
        {
            return await SendAsync<T>(url, HttpMethod.Get, requestHeaders, null);
        }

        public async Task<RestResponse<T>> PostAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            return await SendAsync<T>(url, HttpMethod.Post, requestHeaders, null);
        }

        public async Task<RestResponse<T>> PutAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            return await SendAsync<T>(url, HttpMethod.Put, requestHeaders, null);
        }

        public async Task<RestResponse<T>> SendAsync<T>(string url, HttpMethod method, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            Uri uri = new Uri(url);
            string serviceName = uri.Host;


            //通过polly调用实际的请求
            RestResponse<T> restTemplateResponse = null;
            await HttpInvoker(serviceName, async (pollyContext) =>
            {
                using (HttpRequestMessage httpRequestMessage = new HttpRequestMessage())
                {
                    SetAuthorizationHeader(httpRequestMessage);
                    if (requestHeaders != null)
                    {
                        foreach (var header in requestHeaders)
                        {
                            httpRequestMessage.Headers.Add(header.Key, header.Value);
                        }
                    }
                    httpRequestMessage.Method = method;
                    if (body != null)
                    {
                        httpRequestMessage.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    }
                    string host = await GetHostByStragy(serviceName, null);
                    string serviceUrl = uri.Scheme + "://" + host + uri.PathAndQuery;
                    httpRequestMessage.RequestUri = new Uri(serviceUrl);
                    restTemplateResponse = await SendAsync<T>(httpRequestMessage);
                }
            });
            return restTemplateResponse;
        }

        /// <summary>
        /// 失败自动切换服务器重试 待实现
        /// </summary>
        private async Task<string> GetHostByStragy(string serviceName,List<string> failHosts)
        {
            List<string> hosts =await PullHostFromCache(serviceName);
            //移除失败的服务器，如果移除完没可用服务器则随机取一台重试
            if (failHosts != null && failHosts.Count > 0)
            {
                var avaliableHosts = hosts.Except(failHosts).ToList();//取差集
                if (avaliableHosts.Count > 0)
                    hosts = avaliableHosts;
            }
            //根据当前时钟毫秒数对可用服务个数取模，取出一台机器使用
            var host = hosts.ElementAt(Environment.TickCount % hosts.Count());

            return host;
        }

        /// <summary>
        /// 第一次拉取后会缓存起来，通过定时器去定时拉取
        /// </summary>
        /// <param name="serviceName"></param>
        /// <returns></returns>
        private async Task<List<string>> PullHostFromCache(string serviceName)
        {
            if (_hosts.TryGetValue(serviceName, out List<string> hosts) == false)
            {
                hosts = await PullHostFromConsul(serviceName);
                //_hosts.TryAdd(serviceName, hosts);
            }
            return hosts;
        }


        private async Task<List<string>> PullHostFromConsul(string serviceName)
        {
            var hostList= await _dnsQuery.ResolveServiceAsync("service.consul", serviceName);
            List<string> hosts = new List<string>();
            foreach (var hostItem in hostList)
            {
                hosts.Add($"{hostItem.AddressList.First().ToString()}:{hostItem.Port}");
            }
            return hosts;
        }


        private async Task<RestResponse<T>> SendAsync<T>(HttpRequestMessage httpRequestMessage)
        {
            _logger.LogInformation("HttpClient开始发起请求-------------"+ httpRequestMessage.RequestUri);
            var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage);
            RestResponse<T> restTemplateResponse = new RestResponse<T>();
            restTemplateResponse.StatusCode = httpResponseMessage.StatusCode;
            restTemplateResponse.Headers = httpResponseMessage.Headers;
            string bodyStr = await httpResponseMessage.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(bodyStr))
                restTemplateResponse.Body = JsonConvert.DeserializeObject<T>(bodyStr);
            return restTemplateResponse;
        }



        private async Task HttpInvoker(string serviceName,Func<Context,Task> func)
        {
            if (!_policyWraps.TryGetValue(serviceName, out AsyncPolicyWrap policyWrap))
            {
                //弹性策略为：超时、熔断、重试
                //PolicyWrap commonResilience = Policy.Wrap(retry, breaker, timeout);
                List<IAsyncPolicy> policyList = new List<IAsyncPolicy>();

                //重试  失败自动切换服务器重试，当出现失败，重试其它服务器，已在获取服务器地址中实现
                if (_policyOptions.MaxRetryCount > 0)
                {
                    var policyRetry = Policy.Handle<Exception>().WaitAndRetryAsync
                        (
                        _policyOptions.MaxRetryCount,
                            i => TimeSpan.FromMilliseconds(_policyOptions.RetryIntervalMilliseconds),
                        async (exception, timeSpan,count, pollyContext) =>
                        {
                            var msg = $"第{count}次重试 of {pollyContext.PolicyKey} due to {exception}";
                            _logger.LogWarning(msg);
                        }
                    );
                    policyList.Add(policyRetry);
                }
                //熔断
                if (_policyOptions.EnableCircuitBreaker)
                {
                    var policyCircuit = Policy.Handle<Exception>().CircuitBreakerAsync
                        (
                        _policyOptions.AllowedCountBeforeBreaking,
                            TimeSpan.FromMilliseconds(_policyOptions.BreakingMilliseconds),
                            (ex,time) =>
                            {
                                _logger.LogWarning("polly打开了熔断器");
                            },() =>
                            {
                                _logger.LogWarning("polly重置了熔断器");
                            }
                        );
                    policyList.Add(policyCircuit);
                }
                //超时 优先级最高
                if (_policyOptions.TimeOutMilliseconds > 0)
                {
                    var policyTimeout = Policy.TimeoutAsync
                        (
                            TimeSpan.FromMilliseconds(_policyOptions.TimeOutMilliseconds),
                            TimeoutStrategy.Pessimistic,
                            async (a, b, c) =>
                            {
                                _logger.LogWarning("polly开启了超时");
                            }
                        );
                    policyList.Add(policyTimeout);
                }

                policyWrap = Policy.WrapAsync(policyList.ToArray());
                _policyWraps.TryAdd(serviceName, policyWrap);
            }

            var context = new Context();

             await policyWrap.ExecuteAsync(func, context);
        }


        private void SetAuthorizationHeader(HttpRequestMessage requestMessage)
        {
            var authorizationHeader = _httpContextAccessor.HttpContext.Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authorizationHeader))
            {
                requestMessage.Headers.Add("Authorization", new List<string>() { authorizationHeader });
            }
        }



    }
}
