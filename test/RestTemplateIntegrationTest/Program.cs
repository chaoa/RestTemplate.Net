using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RestTemplateIntegrationTest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IWebHostBuilder webHostBuilder = WebHost.CreateDefaultBuilder(args);
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            configurationBuilder.AddJsonFile("appsettings.json", true, true);
            webHostBuilder.UseConfiguration(configurationBuilder.Build());
            webHostBuilder.UseStartup<Startup>();
            webHostBuilder.ConfigureLogging(logging=> { logging.AddConsole(); });
            webHostBuilder.Build().Run();
        }
    }
}
