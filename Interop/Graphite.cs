using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver.Core.Authentication;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class Graphite
	{
		public const string KEY_RESPONSE_TIME = "time";
		public const string KEY_REQUEST_COUNT = "requests";
		public const string KEY_EXCEPTION_COUNT = "exceptions";
		public const string KEY_SLACK_MESSAGE_COUNT = "slack-messages";
		public const string KEY_UNAUTHORIZED_ADMIN_COUNT = "unauthorized-admin-hits";
		public const string KEY_UNAUTHORIZED_COUNT = "unauthorized-hits";
		public const string KEY_AUTHORIZATION_COUNT = "authorized-hits";
		public const string KEY_LOGGLY_ENTRIES = "loggly-entries";


		private static readonly string Deployment = PlatformEnvironment.Variable("RUMBLE_DEPLOYMENT") ?? "unknown";
		private static Graphite Client { get; set; }
		
		private int Frequency { get; init; }
		private TcpClient TcpClient { get; init; }
		private string ParentService { get; init; }
		private string Server { get; init; }
		private int Port { get; init; }
		private ConcurrentDictionary<string, Metrics> TrackedMetrics { get; init; }
		private CancellationTokenSource CancelToken { get; set; }

		public static void Initialize(string service, int frequencyInMs = 60_000)
		{
			if (Client != null)
				Log.Error(Owner.Default, "Duplicate call to Graphite.Initialize will be ignored.");
			
			string value = PlatformEnvironment.Variable("GRAPHITE");
			string server = value[..value.IndexOf(':')];
			int port = int.Parse(value[(value.IndexOf(':') + 1)..]);
			
			Client ??= new Graphite(service, server, port, frequencyInMs);
		}
		private Graphite(string parentService, string server, int port, int frequency)
		{
			ParentService = parentService;
			Frequency = frequency;
			TcpClient = new TcpClient()
			{
				SendTimeout = 5_000,
				ReceiveTimeout = 5_000
			};
			TrackedMetrics = new ConcurrentDictionary<string, Metrics>();
			Server = server;
			Port = port;
			Start();
		}

		private static string Clean(string value)
		{
			while (value != null && value.StartsWith('/'))
				value = value[1..];
			return value?.Replace('/', '_').Replace('.', '-');
		}
		
		public async Task Add(string name, double value, string endpoint = null, Metrics.Type type = Metrics.Type.AVERAGE)
		{
			endpoint = Clean(endpoint) ?? "general";
			name = $"{endpoint}.{Clean(name)}";
			Metrics m;
			if (TrackedMetrics.TryGetValue(name, out m))
				await m.Track(value);
			else
			{
				TrackedMetrics[name] = new Metrics(name, true, type);
				if (TrackedMetrics.TryGetValue(name, out m))
					await m.Track(value);
			}
		}

		public void Start()
		{
			if (Port == -1 || string.IsNullOrEmpty(Server) || Frequency <= 0)
			{
				Log.Warn(Owner.Default, "Graphite stream could not start.", data: new { Client = this });
				return;
			}

			CancelToken = new CancellationTokenSource();
			Task.Run(Update);
			
			Log.Local(Owner.Default, "Graphite stream started.");
		}

		public void Stop()
		{
			CancelToken.Cancel();
			try
			{
				TcpClient.Close();
			}
			catch { }
		}

		private async Task Update()
		{
			while (!CancelToken.Token.IsCancellationRequested)
			{
				long start = DateTimeOffset.Now.ToUnixTimeMilliseconds();
				await Send(DateTimeOffset.Now.ToUnixTimeSeconds());

				long elapsed = DateTimeOffset.Now.ToUnixTimeMilliseconds() - start;
				if (elapsed > Frequency)
					await Task.Yield();
				else
					await Task.Delay((int) (Frequency - elapsed));
			}
		}

		private async Task Send(long ts)
		{
			Log.Verbose(Owner.Default, "Sending data to graphite.");
			try
			{
				List<Metrics> data = new List<Metrics>();
				foreach (KeyValuePair<string, Metrics> kvp in TrackedMetrics)
					data.Add(await kvp.Value.Flush());
				if (!data.Any())
					return;
				
				byte[][] messages = data
					.Where(metrics => metrics.CountSinceLastFlush > 0)
					.Select(metrics => $"rumble.platform-csharp.{ParentService}.{Deployment}.{metrics.Name} {metrics.Value} {ts}")
					.Select(message => Encoding.ASCII.GetBytes(message + '\n'))
					.ToArray();
				if (!messages.Any())
					return;
				
				foreach (byte[] bytes in messages)
				{
					if (!TcpClient.Connected) // Try to reconnect, one time only
						await TcpClient.ConnectAsync(Server, Port);
					if (!TcpClient.Connected)
						return;
				
					await TcpClient.GetStream().WriteAsync(bytes);
				}

				await TcpClient.GetStream().FlushAsync();
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "Graphite stream failed to send data.", exception: e);
			}
		}

		public static void Track(string name, double value, string endpoint = null, Metrics.Type type = Metrics.Type.FLAT)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				Log.Warn(Owner.Default, "Graphite tracking error: data point must have a name.  Data will not be tracked.", data: new
				{
					Name = name,
					Endpoint = endpoint,
					Value = value
				});
				return;
			}
			
			string prefix = type switch
			{
				Metrics.Type.AVERAGE => "avg",
				Metrics.Type.CUMULATIVE => "cum",
				Metrics.Type.FLAT => "flat",
				Metrics.Type.MAXIMUM => "max",
				Metrics.Type.MINIMUM => "min",
				_ => throw new NotImplementedException()
			};

			if (!name.StartsWith(prefix))
				name = $"{prefix}-{name}";
			
			if (Client == null)
				Log.Warn(Owner.Default, "Graphite has not yet been initialized. Data cannot yet be tracked.", data: new
				{
					Name = name,
					Endpoint = endpoint,
					Value = value
				});
			
			Client?.Add(name, value, endpoint, type);
		}
		
		public class Metrics
		{
			public enum Type { AVERAGE, CUMULATIVE, FLAT, MAXIMUM, MINIMUM  }
			public int CountSinceLastFlush { get; private set; }
			public double Data { get; private set; }
			private SemaphoreSlim Semaphore { get; init; }
			public string Name { get; init; }
			public Type DataType { get; init; }
		
			public long Value => DataType switch
			{
				Type.AVERAGE => Data > 0
					? (int) (Data / CountSinceLastFlush)
					: 0,
				_ => (long)Data
			};
		
			public Metrics(string name, bool async = true, Type type = Type.AVERAGE)
			{
				CountSinceLastFlush = 0;
				Data = type switch
				{
					Type.MINIMUM => double.MaxValue,
					_ => 0
				};
			
				Name = name;
				DataType = type;
				if (async)
					Semaphore = new SemaphoreSlim(1);
			}
		
			public async Task Track(double value)
			{
				await Semaphore.WaitAsync();
				Data = DataType switch
				{
					Type.MINIMUM => Math.Min(Data, value),
					Type.MAXIMUM => Math.Max(Data, value),
					_ => Data + value
				};
				CountSinceLastFlush++;
				Semaphore.Release();
			}
		
			public async Task<Metrics> Flush()
			{
				Metrics output = new Metrics(Name, false, DataType);
			
				await Semaphore.WaitAsync();
				output.CountSinceLastFlush = CountSinceLastFlush;
				output.Data = Data;
				
				switch (DataType)
				{
					case Type.AVERAGE:
					case Type.FLAT:
						Data = 0;
						break;
				}
				CountSinceLastFlush = 0;
				
				Semaphore.Release();
				return output;
			}
		}
	}
}