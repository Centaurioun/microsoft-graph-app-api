﻿using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Web;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Graph;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Primitives;
using GraphExplorerAppModeService.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GraphWebApi.Controllers
{
    [ApiController]
    public class GraphExplorerAppModeController : ControllerBase
    {
        private readonly IGraphAppAuthProvider _graphClient;
        private readonly ITokenAcquisition _tokenAcquisition;

        private readonly IConfiguration _config;

        public GraphExplorerAppModeController(IConfiguration configuration, ITokenAcquisition tokenAcquisition, IGraphAppAuthProvider graphServiceClient)
        {
            this._graphClient = graphServiceClient;
            this._tokenAcquisition = tokenAcquisition;
            this._config = configuration;
        }
        [Route("api/[controller]/{*all}")]
        [Route("graphproxy/{*all}")]
        [HttpGet]
        [AuthorizeForScopes(Scopes = new[] { "https://graph.microsoft.com/.default" })]
        public async Task<IActionResult> GetAsync(string all)
        {
            return await this.ProcessRequestAsync("GET", all, null, _config.GetSection("AzureAd").GetSection("TenantId").Value).ConfigureAwait(false);
        }

        private async Task<string> GetTokenAsync(string tenantId)
        {
            // Acquire the access token.
            string scopes = "https://graph.microsoft.com/.default";

            return await _tokenAcquisition.GetAccessTokenForAppAsync(scopes, tenantId, null);
        }


        [Route("api/[controller]/{*all}")]
        [Route("graphproxy/{*all}")]
        [HttpPost]
        [AuthorizeForScopes(Scopes = new[] { "https://graph.microsoft.com/.default" })]
        public async Task<IActionResult> PostAsync(string all, [FromBody] object body)
        {
            return await ProcessRequestAsync("POST", all, body, _config.GetSection("AzureAd").GetSection("TenantId").Value).ConfigureAwait(false);
        }

        [Route("api/[controller]/{*all}")]
        [Route("graphproxy/{*all}")]
        [HttpDelete]
        public async Task<IActionResult> DeleteAsync(string all, [FromHeader] string Authorization)
        {
            return await ProcessRequestAsync("DELETE", all, null, _config.GetSection("AzureAd").GetSection("TenantId").Value).ConfigureAwait(false);
        }

        [Route("api/[controller]/{*all}")]
        [Route("graphproxy/{*all}")]
        [HttpPut]
        [AuthorizeForScopes(Scopes = new[] { "https://graph.microsoft.com/.default" })]
        public async Task<IActionResult> PutAsync(string all, [FromBody] object body)
        {
            return await ProcessRequestAsync("PUT", all, body, _config.GetSection("AzureAd").GetSection("TenantId").Value).ConfigureAwait(false);
        }

        private string GetBaseUrlWithoutVersion(GraphServiceClient graphClient)
        {
            var baseUrl = graphClient.BaseUrl;
            var index = baseUrl.LastIndexOf('/');
            return baseUrl.Substring(0, index);
        }

        public class HttpResponseMessageResult : IActionResult
        {
            private readonly HttpResponseMessage _responseMessage;

            public HttpResponseMessageResult(HttpResponseMessage responseMessage)
            {
                _responseMessage = responseMessage; // could add throw if null
            }

            public async Task ExecuteResultAsync(ActionContext context)
            {
                context.HttpContext.Response.StatusCode = (int)_responseMessage.StatusCode;

                foreach (var header in _responseMessage.Headers)
                {
                    context.HttpContext.Response.Headers.TryAdd(header.Key, new StringValues(header.Value.ToArray()));
                }

                context.HttpContext.Response.ContentType = _responseMessage.Content.Headers.ContentType.ToString();

                using (var stream = await _responseMessage.Content.ReadAsStreamAsync())
                {
                    await stream.CopyToAsync(context.HttpContext.Response.Body);
                    await context.HttpContext.Response.Body.FlushAsync();
                }
            }
        }

        [Route("api/[controller]/{*all}")]
        [Route("graphproxy/{*all}")]
        [HttpPatch]
        [AuthorizeForScopes(Scopes = new[] { "https://graph.microsoft.com/.default" })]
        public async Task<IActionResult> PatchAsync(string all, [FromBody] object body)
        {
            return await ProcessRequestAsync("PATCH", all, body, _config.GetSection("AzureAd").GetSection("TenantId").Value).ConfigureAwait(false);
        }

        private async Task<IActionResult> ProcessRequestAsync(string method, string all, object content, string tenantId)
        {
            GraphServiceClient _graphServiceClient = _graphClient.GetAuthenticatedGraphClient(GetTokenAsync(tenantId).Result.ToString());

            var qs = HttpContext.Request.QueryString;

            var url = $"{GetBaseUrlWithoutVersion(_graphServiceClient)}/{all}{qs.ToUriComponent()}";

            var request = new BaseRequest(url, _graphServiceClient, null)
            {
                Method = method,
                ContentType = HttpContext.Request.ContentType,
            };

            var neededHeaders = Request.Headers.Where(h => h.Key.ToLower() == "if-match" || h.Key.ToLower() == "consistencylevel").ToList();
            if (neededHeaders.Count() > 0)
            {
                foreach (var header in neededHeaders)
                {
                    request.Headers.Add(new HeaderOption(header.Key, string.Join(",", header.Value)));
                }
            }

            var contentType = "application/json";
            try
            {
                using (var response = await request.SendRequestAsync(content?.ToString(), CancellationToken.None, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false))
                {
                    response.Content.Headers.TryGetValues("content-type", out var contentTypes);

                    contentType = contentTypes?.FirstOrDefault() ?? contentType;

                    var byteArrayContent = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    Console.WriteLine(byteArrayContent);
                    return new HttpResponseMessageResult(ReturnHttpResponseMessage(HttpStatusCode.OK, contentType, new ByteArrayContent(byteArrayContent)));
                }
            }
            catch (ServiceException ex)
            {
                return new HttpResponseMessageResult(ReturnHttpResponseMessage(ex.StatusCode, contentType, new StringContent(ex.Error.ToString())));
            }
        }

        private static HttpResponseMessage ReturnHttpResponseMessage(HttpStatusCode httpStatusCode, string contentType, HttpContent httpContent)
        {
            var httpResponseMessage = new HttpResponseMessage(httpStatusCode)
            {
                Content = httpContent
            };

            try
            {
                httpResponseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }
            catch
            {
                httpResponseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return httpResponseMessage;
        }
    }
}
