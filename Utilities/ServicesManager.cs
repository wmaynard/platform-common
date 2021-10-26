using System.Collections.Generic;
using System;
using System.Collections.Concurrent;

namespace Rumble.Platform.Common.Utilities
{
	public class ServicesManager
	{
		private static ConcurrentDictionary<System.Type, IService> _instances = new ConcurrentDictionary<System.Type, IService>();


		public static T Set<T>(T service) where T : IService
		{
			Type type = typeof(T);

			if (_instances.ContainsKey(type))
			{
				throw new Exception("You can not set a service twice! Destroy the first one if this was intentional");
			}

			_instances[type] = service;

			return service;
		}
		
		public static T Replace<T>(T service) where T : IService
		{
			Type type = typeof(T);

			if (_instances.ContainsKey(type))
			{
				_instances[type].OnDestroy();
			}

			_instances[type] = service;

			return service;
		}
		

		public static T Get<T>() where T : IService
		{
			Type type = typeof(T);

			if (_instances.ContainsKey(type))
			{
				return (T) _instances[type];
			}

			return default(T);
		}
		

		public static bool Has<T>() where T : IService
		{
			return _instances.ContainsKey(typeof(T));
		}
		
		
		public static void DeleteAll()
		{
			List<IService> services = new List<IService>();
			services.AddRange(_instances.Values);
			_instances.Clear();
			
			foreach (IService service in services)
			{
				string serviceType = "unknown";
				
				try
				{
					serviceType = service.GetType().ToString();
					service.OnDestroy();
				}
				catch (Exception e)
				{
					Log.Error(Owner.Sean, "Failed to shutdown service", exception: e, data: new {service =  serviceType});
				}
			}
		}
		

		public static void Delete<T>() where T : IService
		{
			Type type = typeof(T);

			T obj = Get<T>();

			if (obj != null)
			{
				obj.OnDestroy();
				_instances.TryRemove(new KeyValuePair<Type, IService>(type, obj));
			}
		}
	}
}
