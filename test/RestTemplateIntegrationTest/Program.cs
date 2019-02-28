using Polly;
using Polly.Timeout;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestTemplateIntegrationTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var policyTimeout = Policy.TimeoutAsync(
                TimeSpan.FromMilliseconds(500),
                TimeoutStrategy.Pessimistic,
                async (a, b, c) =>
                {
                    Console.WriteLine("进入超时");
                });

            var policyCircuit = Policy.Handle<Exception>().CircuitBreakerAsync(
                5,
                TimeSpan.FromMilliseconds(10000),
                (a, b) =>
                {
                    Console.WriteLine("进入熔断");
                }, () =>
                {
                    Console.WriteLine("重置熔断");
                }
                );

            var policyRetry = Policy.Handle<Exception>().WaitAndRetryAsync(
                10,
            i => TimeSpan.FromMilliseconds(500), (a, b) =>
            {
                Console.WriteLine("进入重试");
            }
            );

            var commonResilience = Policy.WrapAsync(policyRetry, policyCircuit, policyTimeout);

            var ctx = new Context();
            ctx["test"] = "aaaa";
            bool flag = false;
            commonResilience.ExecuteAsync(async () =>
            {
                Random random = new Random();
                //int i = random.Next(1, 3);
                //Console.WriteLine($"休眠{i}秒");
                //Thread.Sleep(i * 1000);
               
                if (flag == false)
                {
                    flag = !flag;
                    Console.WriteLine($"方法超时");
                    await Task.Delay(2000);
                }
                else
                {
                    Console.WriteLine($"方法异常");
                    throw new Exception("异常了aaaaaaaaaa");
                }
            });

            Console.ReadKey();
        }
    }
}
