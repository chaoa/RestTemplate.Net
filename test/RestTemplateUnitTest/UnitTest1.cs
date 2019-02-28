using Polly;
using Polly.Timeout;
using Polly.Wrap;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RestTemplateUnitTest
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            var policyTimeout = Policy.TimeoutAsync(
                TimeSpan.FromMilliseconds(1000),
                TimeoutStrategy.Pessimistic,
                async (a, b, c) =>
                {
                    Console.WriteLine("��ʱ��");
                });

            var policyCircuit = Policy.Handle<Exception>().CircuitBreakerAsync(
                3,
                TimeSpan.FromMilliseconds(200),
                (a, b) =>
                {
                    Console.WriteLine("�۶���");
                }, () =>
                {
                    Console.WriteLine("���´�");
                }
                );

            var policyRetry = Policy.Handle<Exception>().WaitAndRetryAsync(
                3,
            i => TimeSpan.FromMilliseconds(3000), (a, b) =>
            {
                Console.WriteLine("�쳣��");
            }
            );

            var commonResilience = Policy.WrapAsync(policyRetry, policyCircuit, policyTimeout);

            var ctx = new Context();
            ctx["test"] = "aaaa";
            commonResilience.ExecuteAsync(async (context) =>
            {
                Thread.Sleep(2000);
            }, ctx);

            Thread.Sleep(100000);

        }
    }
}
