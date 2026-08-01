using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace Diagnostic.Service.Hubs;

public static class WebApiUtil
{
    private static readonly HttpClient Client = new(new HttpClientHandler { UseDefaultCredentials = true });

    private static async Task<HttpResponseMessage> SendRequest(string uri, HttpMethod method, object? arg = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (arg != null)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(arg)));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return await Client.SendAsync(request);
    }

    public static async Task<string> Get(string url)
    {
        using var response = await SendRequest(url, HttpMethod.Get);
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new ServiceException(response.StatusCode, GetErrorMessage(content));
        }

        return content;
    }

    public static async Task<T> Get<T>(string url)
    {
        using var response = await SendRequest(url, HttpMethod.Get);

        var content = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ServiceException(response.StatusCode, GetErrorMessage(content));
        }

        var result = JsonConvert.DeserializeObject<T>(content)!;
        return result;
    }

    public static async Task<string> Post(string url, object param)
    {
        using var response = await SendRequest(url, HttpMethod.Post, param);
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new ServiceException(response.StatusCode, GetErrorMessage(content));
        }

        return content;
    }

    public static async Task<T> Post<T>(string url, object param)
    {
        using var response = await SendRequest(url, HttpMethod.Post, param);

        var content = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ServiceException(response.StatusCode, GetErrorMessage(content));
        }

        var result = JsonConvert.DeserializeObject<T>(content)!;
        return result;
    }

    private static string GetErrorMessage(string content)
    {
        try
        {
            return JsonConvert.DeserializeObject<string>(content) ?? "Unknown Error";
        }
        catch
        {
            return content;
        }
    }
}

public class ServiceException : Exception
{
    public ServiceException(HttpStatusCode httpStatusCode, string message)
        : base(message)
    {
        StatusCode = httpStatusCode;
    }

    public HttpStatusCode StatusCode { get; set; }
}
