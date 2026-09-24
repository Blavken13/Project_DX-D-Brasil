using System;
using Newtonsoft.Json.Linq;

namespace Durango.Utils.Extensions;

public static class JTokenExtensions
{
	public static T Get<T>(this JToken token, string key, T defaultVal = default(T))
	{
		if (token == null)
		{
			return defaultVal;
		}
		try
		{
			return token.Value<T>(key);
		}
		catch (Exception)
		{
			return defaultVal;
		}
	}

	public static T[] GetArray<T>(this JToken token, string key, T defaultVal = default(T))
	{
		if (token == null)
		{
			return null;
		}
		try
		{
			if (token[key] is JArray jArray)
			{
				return jArray.ToObject<T[]>();
			}
			return null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static string GetString(this JToken token, string defaultVal = null)
	{
		if (token == null)
		{
			return defaultVal;
		}
		try
		{
			return (string)token;
		}
		catch (Exception)
		{
			return defaultVal;
		}
	}
}
