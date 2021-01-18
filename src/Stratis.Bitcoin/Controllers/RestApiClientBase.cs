﻿using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using Polly;
using Polly.Retry;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace Stratis.Bitcoin.Controllers
{
    public interface IRestApiClientBase
    {
        /// <summary>Api endpoint URL that client uses to make calls.</summary>
        string EndpointUrl { get; }
    }

    /// <summary>Client for making API calls for methods provided by controllers.</summary>
    public abstract class RestApiClientBase : IRestApiClientBase
    {
        private readonly IHttpClientFactory httpClientFactory;

        private readonly ILogger logger;

        /// <summary>URL of API endpoint.</summary>
        private readonly string endpointUrl;

        public const int RetryCount = 3;

        /// <summary>Delay between retries.</summary>
        private const int AttemptDelayMs = 1000;

        public const int TimeoutSeconds = 60;

        private readonly RetryPolicy policy;

        /// <inheritdoc />
        public string EndpointUrl => this.endpointUrl;

        public RestApiClientBase(IHttpClientFactory httpClientFactory, int port, string controllerName, string url)
        {
            this.httpClientFactory = httpClientFactory;
            this.logger = LogManager.GetCurrentClassLogger();

            this.endpointUrl = $"{url}:{port}/api/{controllerName}";

            this.policy = Policy.Handle<HttpRequestException>().WaitAndRetryAsync(retryCount: RetryCount, sleepDurationProvider:
                attemptNumber =>
                {
                    // Intervals between new attempts are growing.
                    int delayMs = AttemptDelayMs;

                    if (attemptNumber > 1)
                        delayMs *= attemptNumber;

                    return TimeSpan.FromMilliseconds(delayMs);
                }, onRetry: this.OnRetry);
        }

        protected async Task<HttpResponseMessage> SendPostRequestAsync<Model>(Model requestModel, string apiMethodName, CancellationToken cancellation) where Model : class
        {
            Guard.NotNull(requestModel, nameof(requestModel));

            var publicationUri = new Uri($"{this.endpointUrl}/{apiMethodName}");

            HttpResponseMessage response = null;

            using (HttpClient client = this.httpClientFactory.CreateClient())
            {
                client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

                var request = new JsonContent(requestModel);

                try
                {
                    // Retry the following call according to the policy.
                    await this.policy.ExecuteAsync(async token =>
                    {
                        this.logger.Debug("Sending request of type '{0}' to Uri '{1}'.",
                            requestModel.GetType().FullName, publicationUri);

                        response = await client.PostAsync(publicationUri, request, cancellation).ConfigureAwait(false);
                        this.logger.Debug("Response received: {0}", response);
                    }, cancellation);
                }
                catch (OperationCanceledException)
                {
                    this.logger.Debug("Operation canceled.");
                    this.logger.Trace("(-)[CANCELLED]:null");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    this.logger.Error("Target node is not ready to receive API calls at this time on {0}. Reason: {1}.", this.EndpointUrl, ex.Message);
                    this.logger.Debug("Failed to send a message. Exception: '{0}'.", ex);
                    return new HttpResponseMessage() { ReasonPhrase = ex.Message, StatusCode = HttpStatusCode.InternalServerError };
                }
            }

            this.logger.Trace("(-)[SUCCESS]");
            return response;
        }

        protected async Task<Response> SendPostRequestAsync<Model, Response>(Model requestModel, string apiMethodName, CancellationToken cancellation) where Response : class where Model : class
        {
            HttpResponseMessage response = await this.SendPostRequestAsync(requestModel, apiMethodName, cancellation).ConfigureAwait(false);

            if (response != null && !response.IsSuccessStatusCode && response.Content != null)
            {
                string errorJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(errorJson);
                throw new Exception(errorResponse.Errors[0].Message);
            }

            return await this.ParseHttpResponseMessageAsync<Response>(response).ConfigureAwait(false);
        }

        public async Task<Response> SendGetRequestAsync<Response>(string apiMethodName, string arguments = null, CancellationToken cancellation = default) where Response : class
        {
            HttpResponseMessage response = await this.SendGetRequestAsync(apiMethodName, arguments, cancellation).ConfigureAwait(false);

            return await this.ParseHttpResponseMessageAsync<Response>(response).ConfigureAwait(false);
        }

        private async Task<Response> ParseHttpResponseMessageAsync<Response>(HttpResponseMessage httpResponse) where Response : class
        {
            if (httpResponse == null)
            {
                this.logger.Trace("(-)[NO_RESPONSE]:null");
                return null;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                this.logger.Trace("(-)[NOT_SUCCESS_CODE]:null");
                return null;
            }

            if (httpResponse.Content == null)
            {
                this.logger.Trace("(-)[NO_CONTENT]:null");
                return null;
            }

            // Parse response.
            string successJson = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (successJson == null)
            {
                this.logger.Trace("(-)[JSON_PARSING_FAILURE]:null");
                return null;
            }

            Response responseModel = JsonConvert.DeserializeObject<Response>(successJson);

            this.logger.Trace("(-)[SUCCESS]");
            return responseModel;
        }

        protected async Task<HttpResponseMessage> SendGetRequestAsync(string apiMethodName, string arguments = null, CancellationToken cancellation = default)
        {
            string url = $"{this.endpointUrl}/{apiMethodName}";

            if (!string.IsNullOrEmpty(arguments))
            {
                url += "/?" + arguments;
            }

            HttpResponseMessage response = null;

            using (HttpClient client = this.httpClientFactory.CreateClient())
            {
                client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

                try
                {
                    // Retry the following call according to the policy.
                    await this.policy.ExecuteAsync(async token =>
                    {
                        this.logger.Debug("Sending request to Url '{1}'.", url);

                        response = await client.GetAsync(url, cancellation).ConfigureAwait(false);

                        if (response != null)
                            this.logger.Debug("Response received: {0}", response);
                    }, cancellation);
                }
                catch (OperationCanceledException)
                {
                    this.logger.Debug("Operation canceled.");
                    this.logger.Trace("(-)[CANCELLED]:null");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    this.logger.Error("Target node is not ready to receive API calls at this time ({0})", this.EndpointUrl);
                    this.logger.Debug("Failed to send a message to '{0}'. Exception: '{1}'.", url, ex);
                    return new HttpResponseMessage() { ReasonPhrase = ex.Message, StatusCode = HttpStatusCode.InternalServerError };
                }
            }

            this.logger.Trace("(-)[SUCCESS]");
            return response;
        }

        protected virtual void OnRetry(Exception exception, TimeSpan delay)
        {
            this.logger.Debug("Exception while calling API method: {0}. Retrying...", exception.ToString());
        }
    }

    /// <summary>
    /// Helper class to interpret a string as json.
    /// </summary>
    public class JsonContent : StringContent
    {
        public JsonContent(object obj) :
            base(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json")
        {
        }
    }

    /// <summary>
    /// TODO: this should be removed when compatible with full node API, instead, we should use
    /// services.AddHttpClient from Microsoft.Extensions.Http
    /// </summary>
    public class HttpClientFactory : IHttpClientFactory
    {
        /// <inheritdoc />
        public HttpClient CreateClient(string name)
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return httpClient;
        }
    }
}
