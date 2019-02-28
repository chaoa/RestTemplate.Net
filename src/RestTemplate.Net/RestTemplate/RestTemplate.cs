using Consul;
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
using Polly.Wrap;
using Polly.Timeout;

namespace RestTemplate
{
    public class RestTemplate : IHttpClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RestTemplate> _logger;
        private ConcurrentDictionary<string, AsyncPolicyWrap> _policyWraps;//polly容器
        private ConcurrentDictionary<string, List<string>> _hosts;//缓存服务注册表，定时刷新


        private readonly string _consulServerUrl;
        private PolicyConfiguration _policyConfiguration;

        public RestTemplate()
        {

        }


        public Task<RestTemplateResponse<T>> DeleteAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            throw new NotImplementedException();
        }

        public async Task<RestTemplateResponse<T>> GetAsync<T>(string url, HttpRequestHeaders requestHeaders = null)
        {
            Uri uri = new Uri(url);
            string serviceName = uri.Host;
 
            using (HttpRequestMessage httpRequestMessage = new HttpRequestMessage())
            {
                if (requestHeaders != null)
                {
                    foreach (var header in requestHeaders)
                    {
                        httpRequestMessage.Headers.Add(header.Key, header.Value);
                    }
                }
                httpRequestMessage.Method =HttpMethod.Get;

                //通过polly调用实际的请求
                RestTemplateResponse<T> restTemplateResponse = null;
                 await HttpInvoker(serviceName, async (pollyContext) =>
                 {
                     var failHosts = pollyContext["failHosts"] as List<string>;
                     string host =await GetHostByStragy(serviceName, failHosts);
                     pollyContext["currentHost"] = host;//保存当前host
                     string serviceUrl= uri.Scheme + "://" + host + uri.PathAndQuery;
                     httpRequestMessage.RequestUri = new Uri(serviceUrl);
                     restTemplateResponse = await SendAsync<T>(httpRequestMessage);
                 });
                return restTemplateResponse;
            }
        }

        public Task<RestTemplateResponse<T>> PostAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            throw new NotImplementedException();
        }

        public Task<RestTemplateResponse<T>> PutAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null)
        {
            throw new NotImplementedException();
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
            if (_hosts.TryGetValue(serviceName, out List<string> hosts)==false)
                hosts = await PullHostFromConsul(serviceName);
            return hosts;
        }


        private async Task<List<string>> PullHostFromConsul(string serviceName)
        {
            using (var consulClient = new ConsulClient(c => c.Address = new Uri(_consulServerUrl)))
            {
                var services = (await consulClient.Agent.Services()).Response.Values.Where(s => s.Service.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                if (!services.Any())
                {
                    throw new ArgumentException($"{serviceName} not found");
                }
                List<string> hosts = new List<string>();
                foreach (var service in services)
                {
                    hosts.Add($"{service.Address}:{service.Port}");
                }
                return hosts;
            }
        }


        private async Task<RestTemplateResponse<T>> SendAsync<T>(HttpRequestMessage httpRequestMessage)
        {
            var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage);
            RestTemplateResponse<T> restTemplateResponse = new RestTemplateResponse<T>();
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
                if (_policyConfiguration.MaxRetryCount > 0)
                {
                    var policyRetry = Policy.Handle<Exception>().WaitAndRetryAsync
                        (
                            _policyConfiguration.MaxRetryCount,
                            i => TimeSpan.FromMilliseconds(_policyConfiguration.RetryIntervalMilliseconds),
                            async (exception, timeSpan, pollyContext) =>
                            {
                                var failHosts = pollyContext["failHosts"] as List<string>;
                                string currentHost = pollyContext["currentHost"].ToString();
                                if (failHosts == null)
                                    failHosts = new List<string>() { currentHost };
                                else
                                    failHosts.Add(currentHost);
                                pollyContext["failHosts"] = failHosts;

                                _logger.LogTrace("polly开启了重试");
                            }
                        );
                    policyList.Add(policyRetry);
                }
                //熔断
                if (_policyConfiguration.EnableCircuitBreaker)
                {
                    var policyCircuit = Policy.Handle<Exception>().CircuitBreakerAsync
                        (
                            _policyConfiguration.AllowedCountBeforeBreaking,
                            TimeSpan.FromMilliseconds(_policyConfiguration.BreakingMilliseconds),
                            (ex,time) =>
                            {
                                _logger.LogTrace("polly打开了熔断器");
                            },() =>
                            {
                                _logger.LogTrace("polly重置了熔断器");
                            }
                        );
                    policyList.Add(policyCircuit);
                }
                //超时 优先级最高
                if (_policyConfiguration.TimeOutMilliseconds > 0)
                {
                    var policyTimeout = Policy.TimeoutAsync
                        (
                            TimeSpan.FromMilliseconds(_policyConfiguration.TimeOutMilliseconds),
                            TimeoutStrategy.Pessimistic,
                            async (a, b, c) =>
                            {
                                _logger.LogTrace("polly开启了超时");
                            }
                        );
                    policyList.Add(policyTimeout);
                }

                policyWrap = Policy.WrapAsync(policyList.ToArray());
                _policyWraps.TryAdd(serviceName, policyWrap);
            }

             await policyWrap.ExecuteAsync(func, new Context());
        }






    }
}
