using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace RestTemplate
{
    public interface IHttpClient
    {
        Task<RestTemplateResponse<T>> GetAsync<T>(string url, HttpRequestHeaders requestHeaders = null);

        Task<RestTemplateResponse<T>> PostAsync<T>(string url, HttpRequestHeaders requestHeaders = null,object body=null);

        Task<RestTemplateResponse<T>> DeleteAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body=null);

        Task<RestTemplateResponse<T>> PutAsync<T>(string url, HttpRequestHeaders requestHeaders = null, object body = null);
    }
}
