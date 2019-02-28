using Polly;
using Polly.Timeout;
using Polly.Wrap;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using RestTemplate;
using Microsoft.Extensions.Logging;

namespace RestTemplateUnitTest
{
    public class RestTemplateTest
    {
        [Fact]
        public void Test_HttpInvoker()
        {
            // PolicyConfiguration policyConfiguration = new PolicyConfiguration()
            //{
            //    AllowedCountBeforeBreaking = 3,
            //    BreakingMilliseconds = 3000,
            //    EnableCircuitBreaker = true,
            //    MaxRetryCount = 3,
            //    RetryIntervalMilliseconds = 200,
            //    TimeOutMilliseconds = 1000
            //};
            //RestHttpClient restHttpClient = new RestHttpClient(policyConfiguration);

            //var polly = restHttpClient.CreatePolicyWrap("test");

            //polly.ExecuteAsync(async () =>
            //{


            //})
        }
    }
}
