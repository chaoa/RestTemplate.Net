using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace RestTemplate
{
    public interface IHttpClient
    {
        Task<RestResponse<T>> GetAsync<T>(string url, HttpRequestHeaders requestHeaders = null);

        Task<RestResponse<T>> PostAsync<T>(string url, HttpRequestHeaders requestHeaders = null,object body=null);

        Task<RestResponse<T>> DeleteAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body=null);

        Task<RestResponse<T>> PutAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null);
    }
}
