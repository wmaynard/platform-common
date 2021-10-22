using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Rumble.Platform.Common.Utilities
{
	public class Crypto
	{
		// Might make more sense for this to be an env var or something not explicitly in the codebase, but fine for now
		private const string SALT = "YKdMrX2tohEEXn1oyx6RER2aWIKMA6NJ";

		public static string Encode(string sensitive)
		{
			using Aes aes = Aes.Create();
			ICryptoTransform transform = aes.CreateEncryptor(Encoding.UTF8.GetBytes(SALT), new byte[16]);

			using MemoryStream ms = new MemoryStream();
			using CryptoStream cs = new CryptoStream(ms, transform, CryptoStreamMode.Write);
			using (StreamWriter sw = new StreamWriter(cs))
				sw.Write(sensitive);

			return Convert.ToBase64String(ms.ToArray());
		}

		public static string Decode(string encrypted)
		{
			using Aes aes = Aes.Create();
			ICryptoTransform transform = aes.CreateDecryptor(Encoding.UTF8.GetBytes(SALT), new byte[16]);
			
			using MemoryStream ms = new MemoryStream(Convert.FromBase64String(encrypted));
			using CryptoStream cs = new CryptoStream(ms, transform, CryptoStreamMode.Read);
			using StreamReader rdr = new StreamReader(cs);
			
			return rdr.ReadToEnd();
		}
	}
}