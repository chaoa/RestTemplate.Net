using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RestTemplate;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace RestTemplateIntegrationTest
{
    /// <summary>
    /// 用户接口
    /// </summary>
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class HomeController : Controller
    {
        //
        private readonly IHttpClient _httpClient;

        //
        public HomeController(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        [HttpGet]
        public async Task<ActionResult> Get()
        {
            try
            {
                var res1 = await _httpClient.GetAsync<Person>("http://UnitTestService/test");
                return Ok(res1);
            }
            catch (Exception e)
            {
                return Ok(e.Message);
            }
        }

    }

    public class Person
    {
        public string Name { get; set; }

        public  int Age { get; set; }
    }
}
