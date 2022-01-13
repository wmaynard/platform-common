using System;
using System.Collections.Generic;
using System.Net;
using MaxMind.Db;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Models;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public static class MaxMind
	{
		public const string FILENAME = "GeoIP2-Country.mmdb";
		public static GeoIPData Lookup(string ipAddress)
		{
			try
			{
				IPAddress ip = IPAddress.Parse(ipAddress); 
			
				using Reader reader = new Reader(FILENAME);
				GenericData data = GenericData.FromDictionary(reader.Find<Dictionary<string, object>>(ip));
				return new GeoIPData(ipAddress, data);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "Unable to retrieve GeoIP data.", data: new
				{
					IpAddress = ipAddress
				});
				return new GeoIPData(ipAddress);
			}
			
		}

		public static void Download()
		{
			// TODO: Download updated .mmdb from S3
		}
	}
}