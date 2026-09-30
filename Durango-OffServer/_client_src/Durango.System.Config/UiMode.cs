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
		string text = (flag ? "PC" : "Celular");
		try
		{
			UIManager.MessageBox.Show(T._("Alterar modo da interface"), T._("Modo alterado para <em>{0}</em>.\nFeche e abra o jogo novamente para aplicar a mudança.", text), (Action)null, T._("Confirmar"));
		}
		catch (Exception)
		{
		}
	}
}
