using System;
using L10N;
using UnityEngine;

namespace Durango.System.Config;

public static class UiMode
{
	public const string PC = "PC";

	public const string Mobile = "Mobile";

	private const string PrefKey = "option:ui_mode";

	private static bool? _cachedUsePC;

	public static bool UsePCUI
	{
		get
		{
			if (GameManager.IsTitleScene || GameManager.IsPrologueMode)
			{
				return true;
			}
			if (!_cachedUsePC.HasValue)
			{
				string a;
				try
				{
					a = PlayerPrefs.GetString("option:ui_mode", "PC");
				}
				catch (Exception)
				{
					a = "PC";
				}
				_cachedUsePC = !string.Equals(a, "Mobile", StringComparison.OrdinalIgnoreCase);
			}
			return _cachedUsePC.Value;
		}
	}

	public static string Normalize(string value)
	{
		if (!string.Equals(value, "Mobile", StringComparison.OrdinalIgnoreCase))
		{
			return "PC";
		}
		return "Mobile";
	}

	public static void ApplyChanged(string value)
	{
		bool flag = !string.Equals(Normalize(value), "Mobile", StringComparison.OrdinalIgnoreCase);
		if (_cachedUsePC.HasValue && _cachedUsePC.Value == flag)
		{
			return;
		}
		try
		{
			PlayerPrefs.Save();
		}
		catch (Exception)
		{
		}
		string text = (flag ? "PC" : "Mobile");
		try
		{
			UIManager.MessageBox.Show(T._("เปล\u0e35\u0e48ยนโหมด UI"), T._("เปล\u0e35\u0e48ยนเป\u0e47นโหมด <em>{0}</em> แล\u0e49ว\nกร\u0e38ณาป\u0e34ดเกมแล\u0e49วเป\u0e34ดใหม\u0e48เพ\u0e37\u0e48อให\u0e49การเปล\u0e35\u0e48ยนแปลงม\u0e35ผล", text), (Action)null, T._("ตกลง"));
		}
		catch (Exception)
		{
		}
	}
}
