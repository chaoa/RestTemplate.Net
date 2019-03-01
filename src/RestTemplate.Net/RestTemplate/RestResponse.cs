using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace RestTemplate
{
    public class RestResponse<T>
    {
        /// <summary>
        /// 响应状态码
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// 响应的报文头
        /// </summary>
        public HttpResponseHeaders Headers { get; set; }

        public T Body { get; set; }
    }
}
