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
				return OffServerLink.AccountId;
			}
			return string.Empty;
		}
	}

	public override string NPA
	{
		get
		{
			if (!string.IsNullOrEmpty(OffServerLink.DiscordName))
			{
				return "Discord: " + OffServerLink.DiscordName;
			}
			return "ย\u0e31งไม\u0e48ได\u0e49ผ\u0e39กรห\u0e31ส";
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

	public override void ShowAccountMenu()
	{
		UnityEngine.Debug.Log("[OffServer] ShowAccountMenu — เป\u0e34ดหน\u0e49าย\u0e49ายบ\u0e31ญช\u0e35 (ก\u0e38ญแจเคร\u0e37\u0e48องน\u0e35\u0e49=" + OffServerLink.AccountId + " · ผ\u0e39กด\u0e34ส=" + OffServerLink.DiscordName + ")");
		UIManager.Popup.Tooltip<TextInputPopup>().Show(delegate(string step1)
		{
			step1 = (step1 ?? string.Empty).Trim();
			if (step1.Length != 0)
			{
				UIManager.Popup.Tooltip<TextInputPopup>().Show(delegate(string text2)
				{
					string text = (text2 ?? string.Empty).Trim();
					if (text.Length != 0)
					{
						Connections.Frontend.Send(new AcceptTENCoupon
						{
							CouponNum = step1 + " " + text,
							ToyToken = OffServerLink.AccountId
						}).On<OK>(delegate
						{
							UIManager.Alarm.ShowNotify("สำเร\u0e47จ — กล\u0e31บหน\u0e49า Title แล\u0e49วเข\u0e49าใหม\u0e48เพ\u0e37\u0e48อโหลดต\u0e31วละครของบ\u0e31ญช\u0e35น\u0e35\u0e49", "icon_mainhud_shop", major: false);
						});
					}
				}, "รอบ 2/2 — ใส\u0e48รห\u0e31สผ\u0e48านบ\u0e31ญช\u0e35 (8 ต\u0e31วข\u0e36\u0e49นไป · ถ\u0e49าย\u0e31งไม\u0e48เคยต\u0e31\u0e49ง = ต\u0e31\u0e49งใหม\u0e48)");
			}
		}, "รอบ 1/2 — ใส\u0e48โค\u0e49ด 6 ต\u0e31วจากบอท หร\u0e37อ Account Number เต\u0e47มของบ\u0e31ญช\u0e35เด\u0e34ม");
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
