using Durango.Logic.Clusters;
using Durango.Network;
using Durango.System.Config;
using Durango.UI;
using Durango.UI.Popup;
using Messages;
using UnityEngine;

namespace Durango.System;

public class Platform_PC : Platform
{
	public override string NPSN
	{
		get
		{
			if (OffServerLink.Active)
			{
				return OffServerLink.GetAuthenticatedAccountId(GameManager.GatewayUrl) ?? string.Empty;
			}
			return string.Empty;
		}
	}

	public override string Token
	{
		get
		{
			if (!OffServerLink.Active)
			{
				return string.Empty;
			}
			return OffServerLink.GetAuthToken(GameManager.GatewayUrl);
		}
	}

	public override string NPA
	{
		get
		{
			string username = OffServerLink.GetAuthenticatedUsername(GameManager.GatewayUrl);
			if (!string.IsNullOrEmpty(username))
			{
				return "Conta: " + username;
			}
			if (!string.IsNullOrEmpty(OffServerLink.DiscordName))
			{
				return "Discord: " + OffServerLink.DiscordName;
			}
			return "Conta Durango Brasil";
		}
	}

	public override bool IsLoginTypeGuest => true;

	public override bool IsPCStore => true;

	public override string AppBundleId => "com.nexon.durango.wildlands";

	public override bool IsConnectFacebook => false;

	public override bool IsConnectGooglePlus => false;

	public override bool IsAvailableOfferwall => false;

	public override bool UsePCUI => UiMode.UsePCUI;

	public override int DefaultUISize => 1280;

	public override bool UsePCRenderer => true;

	public override bool SupportPortrait => false;

	public override int DefaultRenderTargetSize => 1024;

	public override string PrologueMovieUrl => "https://d1skbslnewf3os.cloudfront.net/prologue_movie_pc.mp4";

	public override void Logout(global::System.Action<bool> onResult)
	{
		OffServerLink.ClearAuthentication(GameManager.GatewayUrl);
		GameManager.SessionToken = string.Empty;
		GameManager.PlayerId = string.Empty;
		UnityEngine.Debug.Log("[Auth] sessão local encerrada; uma nova conta poderá ser autenticada na tela inicial");
		onResult?.Invoke(true);
	}

	public override void ShowAccountMenu()
	{
		UIManager.Alarm.ShowNotify(
			"Para trocar de conta, volte à tela inicial e use a opção Sair. Depois entre com a outra conta.",
			"icon_mainhud_shop",
			major: false);
	}

	public override bool GetScreenResolution(bool isPortrait, out int width, out int height)
	{
		Point2 screenResolution = GetScreenResolution();
		width = screenResolution.x;
		height = screenResolution.y;
		return true;
	}

	public static Point2 GetScreenResolution()
	{
		Point2 result = new Point2
		{
			x = (int)((float)Screen.width * 96f / Screen.dpi),
			y = (int)((float)Screen.height * 96f / Screen.dpi)
		};
		int num = Mathf.Max((int)((float)(result.x * UIManager.UISize) / 1280f), 1600);
		int y = Mathf.RoundToInt((float)num * UIAnchorPolicy.DefaultAspectRatio);
		result.x = num;
		result.y = y;
		return result;
	}
}
