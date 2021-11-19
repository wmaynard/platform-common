using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Rumble.Platform.Common.Utilities;
using Method = System.Net.Http.HttpMethod;

namespace Rumble.Platform.Common.Web
{
	[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
	public class PlatformRequest
	{
		private static readonly HttpClient CLIENT = new HttpClient(new HttpClientHandler()
		{
			AutomaticDecompression = DecompressionMethods.All
		});
		private static readonly Dictionary<string, string> STANDARD_HEADERS = new Dictionary<string, string>() {
			{"User-Agent", $"{Assembly.GetExecutingAssembly().GetName().Name}/{Assembly.GetExecutingAssembly().GetName().Version}"},
			{"Accept", "*/*"},
			{"Accept-Encoding", "gzip, deflate, br"}
		};
		private HttpRequestMessage Request { get; set; }
		private Uri Uri { get; set; }

		public Dictionary<string, string> Headers
		{
			get => Request.Headers.ToDictionary(keySelector: pair => pair.Key, elementSelector: pair => pair.Value.FirstOrDefault());
			set
			{
				Request.Headers.Clear();
				value ??= new Dictionary<string, string>();
				foreach (KeyValuePair<string, string> pair in STANDARD_HEADERS)
					value.TryAdd(pair.Key, pair.Value);
				value.TryAdd("Host", Uri.Host);
				foreach (KeyValuePair<string, string> pair in value)
					Request.Headers.Add(pair.Key, pair.Value);
			}
		}

		public string Payload
		{
			get
			{
				Task<string> task = Request.Content.ReadAsStringAsync();
				task.Wait();
				return task.Result;
			}
			set
			{
				Request.Content = new StringContent(value ?? "{}");
				Request.Content.Headers.Remove("Content-Type");
				Request.Content.Headers.Add("Content-Type", "application/json");
			}
		}
		
		private PlatformRequest(Method method, string url, Dictionary<string, string> headers = null, string payload = null)
		{
			Uri = new Uri(url);
			Request = new HttpRequestMessage(method: method, requestUri: Uri);
			Headers = headers;
			Payload = payload;
		}

		private void Reset()
		{
			Dictionary<string, string> headers = Headers;
			GenericData payload = Payload;
			Request = new HttpRequestMessage(Request.Method, Uri);
			Headers = headers;
			Payload = payload;
		}

		public GenericData Send(GenericData payload = null) => Send(payload, out HttpStatusCode unused);
		public GenericData Send(out HttpStatusCode code) => Send(null, out code);
		public GenericData Send(GenericData payload, out HttpStatusCode code)
		{
			code = HttpStatusCode.BadRequest;
			GenericData output = null;
			try
			{
				if (payload != null)
					Payload = payload;
				HttpResponseMessage response = CLIENT.Send(Request);
				code = response.StatusCode;
				HttpContent content = response.Content;

				Task<string> task = content.ReadAsStringAsync();
				task.Wait(); // TODO: Test timeout failure
				output = task.Result;
			}
			catch (HttpRequestException ex)
			{
				Log.Error(Owner.Default, "Unable to send web request.", exception: ex, data: new { Url = Uri.ToString(), Payload = payload });
			}
			catch (JsonException ex)
			{
				Log.Error(Owner.Default, "Unable to parse response.", exception: ex, data: new { Url = Uri.ToString(), Payload = payload });
			}

			Reset();
			return output;
		}
		public async Task<GenericData> SendAsync(GenericData payload = null) => Send(payload); // TODO: implement Async sending

		// These static wrappers for constructors are intended to improve readability and make it a little more intuitive to send requests out.
		// PlatformRequest request = PlatformRequest.Post(url, payload);
		// is nicer to read than
		// PlatformRequest request = new PlatformRequest(HttpMethod.Post, url, payload);
		public static PlatformRequest Delete(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Delete, url, headers, payload);
		public static PlatformRequest Get(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Get, url, headers);
		public static PlatformRequest Head(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Head, url, headers, payload);
		public static PlatformRequest Options(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Options, url, headers, payload);
		public static PlatformRequest Patch(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Patch, url, headers, payload);
		public static PlatformRequest Post(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Post, url, headers, payload);
		public static PlatformRequest Put(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Put, url, headers, payload);
		public static PlatformRequest Trace(string url, Dictionary<string, string> headers = null, GenericData payload = null) => new PlatformRequest(Method.Trace, url, headers, payload);
	}
}