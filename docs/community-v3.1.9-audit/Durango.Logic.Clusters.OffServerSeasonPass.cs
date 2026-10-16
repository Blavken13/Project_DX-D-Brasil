using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BestHTTP;
using BestHTTP.Forms;
using Durango.Network;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Yaml;

namespace Durango.Logic.Clusters;

public static class OffServerSeasonPass
{
	public sealed class Reward
	{
		public readonly List<WarpRushReward> Items = new List<WarpRushReward>();

		public long TStone;

		public bool IsEmpty
		{
			get
			{
				if (Items.Count == 0)
				{
					return TStone <= 0;
				}
				return false;
			}
		}

		public string Describe()
		{
			List<string> list = new List<string>();
			foreach (WarpRushReward item in Items)
			{
				if (item.Item != null)
				{
					Prototype itemPrototype = PrototypeYaml.GetItemPrototype(item.Item.PrototypeId, item.Item.Level);
					list.Add($"{((itemPrototype != null) ? ((object)itemPrototype.Name) : item.Item.PrototypeId)} x{item.Item.Count}");
				}
			}
			if (TStone > 0)
			{
				list.Add(Text("tstone", TStone.ToString("N0")));
			}
			return string.Join("\n", list.ToArray());
		}
	}

	public sealed class Tier
	{
		public int Number;

		public int State;

		public readonly Reward Box = new Reward();

		public readonly Reward Special = new Reward();
	}

	public sealed class Mission
	{
		public string Id = string.Empty;

		public string Name = string.Empty;

		public string Event = string.Empty;

		public int Count;

		public int Progress;

		public int Points;

		public bool Done;
	}

	public sealed class MissionList
	{
		public double ResetAt;

		public int MaxPoints;

		public readonly List<Mission> Missions = new List<Mission>();

		public int EarnedPoints
		{
			get
			{
				int num = 0;
				foreach (Mission mission in Missions)
				{
					if (mission.Done)
					{
						num += mission.Points;
					}
				}
				return num;
			}
		}
	}

	public sealed class Status
	{
		public bool Active;

		public string SeasonId = string.Empty;

		public string SeasonName = string.Empty;

		public double Since;

		public double Until;

		public double Now;

		public long Points;

		public int Tier;

		public int MaxTier;

		public int PointsPerTier = 1;

		public long TierPoints;

		public int Claimable;

		public readonly List<Tier> Tiers = new List<Tier>();

		public readonly MissionList Daily = new MissionList();

		public readonly MissionList Weekly = new MissionList();

		public float ReceivedAt;

		public int FirstClaimable
		{
			get
			{
				foreach (Tier tier in Tiers)
				{
					if (tier.State == 1)
					{
						return tier.Number;
					}
				}
				return 0;
			}
		}

		public Tier GetTier(int number)
		{
			foreach (Tier tier in Tiers)
			{
				if (tier.Number == number)
				{
					return tier;
				}
			}
			return null;
		}
	}

	public const int StateLocked = 0;

	public const int StateClaimable = 1;

	public const int StateClaimed = 2;

	private const int HttpTimeoutSec = 15;

	public static Status Current;

	public static bool Busy;

	public static string LastEvent = string.Empty;

	public static string LastClaimed = string.Empty;

	private static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
	{
		{
			"menu",
			new string[4] { "Season Pass", "Season Pass", "Season Pass", "시즌 패스" }
		},
		{
			"title",
			new string[4] { "Season Pass", "Season Pass", "Season Pass", "시즌 패스" }
		},
		{
			"tab_rewards",
			new string[4] { "รางว\u0e31ล", "Rewards", "Hadiah", "보상" }
		},
		{
			"tier_n",
			new string[4] { "ข\u0e31\u0e49น {0}", "Tier {0}", "Tingkat {0}", "{0}단계" }
		},
		{
			"row_box",
			new string[4] { "กล\u0e48องเสบ\u0e35ยง", "Supply box", "Kotak perbekalan", "보급 상자" }
		},
		{
			"row_special",
			new string[4] { "ของพ\u0e34เศษ", "Special reward", "Hadiah spesial", "특별 보상" }
		},
		{
			"box_tooltip",
			new string[4] { "กล\u0e48องเสบ\u0e35ยง ข\u0e31\u0e49น {0}", "Supply box, tier {0}", "Kotak perbekalan tingkat {0}", "보급 상자 {0}단계" }
		},
		{
			"tstone",
			new string[4] { "T Stone {0}", "{0} T Stone", "{0} T Stone", "T스톤 {0}" }
		},
		{
			"progress",
			new string[4] { "<em>ข\u0e31\u0e49น {0}</em> / {1}   {2} / {3} แต\u0e49ม", "<em>Tier {0}</em> / {1}   {2} / {3} pts", "<em>Tingkat {0}</em> / {1}   {2} / {3} poin", "<em>{0}단계</em> / {1}   {2} / {3} 포인트" }
		},
		{
			"progress_max",
			new string[4] { "<em>ข\u0e31\u0e49นส\u0e39งส\u0e38ด {0}</em>   {1} แต\u0e49ม", "<em>Max tier {0}</em>   {1} pts", "<em>Tingkat maks {0}</em>   {1} poin", "<em>최고 {0}단계</em>   {1} 포인트" }
		},
		{
			"claim_all",
			new string[4] { "ร\u0e31บท\u0e31\u0e49งหมด ({0})", "Claim all ({0})", "Ambil semua ({0})", "모두 받기 ({0})" }
		},
		{
			"claim_none",
			new string[4] { "ไม\u0e48ม\u0e35ของให\u0e49ร\u0e31บ", "Nothing to claim", "Tidak ada hadiah", "받을 보상 없음" }
		},
		{
			"missions",
			new string[4] { "ภารก\u0e34จ", "Missions", "Misi", "임무" }
		},
		{
			"missions_title",
			new string[4] { "ภารก\u0e34จ Season Pass", "Season Pass missions", "Misi Season Pass", "시즌 패스 임무" }
		},
		{
			"daily",
			new string[4] { "ประจำว\u0e31น", "Daily", "Harian", "일일" }
		},
		{
			"weekly",
			new string[4] { "ประจำส\u0e31ปดาห\u0e4c", "Weekly", "Mingguan", "주간" }
		},
		{
			"earned",
			new string[4] { "ได\u0e49แล\u0e49ว {0} / {1} แต\u0e49ม", "earned {0} / {1} pts", "dapat {0} / {1} poin", "{0} / {1} 포인트 획득" }
		},
		{
			"reset_in",
			new string[4] { "(ร\u0e35เซ\u0e47ตใน {0})", "(resets in {0})", "(reset dalam {0})", "({0} 후 초기화)" }
		},
		{
			"done",
			new string[4] { "สำเร\u0e47จ", "done", "selesai", "완료" }
		},
		{
			"no_season",
			new string[4] { "ตอนน\u0e35\u0e49ไม\u0e48ม\u0e35ซ\u0e35ซ\u0e31\u0e48นท\u0e35\u0e48เป\u0e34ดอย\u0e39\u0e48 — รอซ\u0e35ซ\u0e31\u0e48นถ\u0e31ดไป", "No season is running right now. Wait for the next one.", "Belum ada musim yang berjalan. Tunggu musim berikutnya.", "현재 진행 중인 시즌이 없습니다. 다음 시즌을 기다려 주세요." }
		},
		{
			"ends_in",
			new string[4] { "เหล\u0e37อ {0}", "{0} left", "{0} lagi", "{0} 남음" }
		},
		{
			"unclaimed_hint",
			new string[4] { "ของท\u0e35\u0e48ถ\u0e36งข\u0e31\u0e49นแล\u0e49วแต\u0e48ย\u0e31งไม\u0e48กดร\u0e31บ จะส\u0e48งเข\u0e49าจดหมายตอนหมดซ\u0e35ซ\u0e31\u0e48น", "Reached but unclaimed tiers are mailed to you when the season ends.", "Tingkat yang tercapai tapi belum diambil akan dikirim lewat surat saat musim berakhir.", "도달했지만 받지 않은 보상은 시즌 종료 시 우편으로 보내드립니다." }
		},
		{
			"claim_failed",
			new string[4] { "ร\u0e31บของไม\u0e48สำเร\u0e47จ", "Couldn't claim the reward", "Gagal mengambil hadiah", "보상을 받지 못했습니다" }
		},
		{
			"fail_notreached",
			new string[4] { "ย\u0e31งไม\u0e48ถ\u0e36งข\u0e31\u0e49นน\u0e35\u0e49", "You haven't reached this tier yet.", "Tingkat ini belum tercapai.", "아직 이 단계에 도달하지 않았습니다." }
		},
		{
			"fail_taken",
			new string[4] { "ร\u0e31บข\u0e31\u0e49นน\u0e35\u0e49ไปแล\u0e49ว", "Already claimed.", "Sudah diambil.", "이미 받았습니다." }
		},
		{
			"fail_none",
			new string[4] { "ไม\u0e48ม\u0e35ของให\u0e49ร\u0e31บ", "Nothing to claim.", "Tidak ada hadiah.", "받을 보상이 없습니다." }
		},
		{
			"fail_noseason",
			new string[4] { "ซ\u0e35ซ\u0e31\u0e48นน\u0e35\u0e49จบแล\u0e49ว", "This season has ended.", "Musim ini sudah berakhir.", "이 시즌은 종료되었습니다." }
		},
		{
			"fail_rate_limited",
			new string[4] { "กดถ\u0e35\u0e48เก\u0e34นไป — รอส\u0e31กคร\u0e39\u0e48", "Too many requests. Wait a moment.", "Terlalu sering. Tunggu sebentar.", "요청이 너무 많습니다. 잠시 기다려 주세요." }
		},
		{
			"fail_unauthorized",
			new string[4] { "ย\u0e37นย\u0e31นต\u0e31วไม\u0e48ผ\u0e48าน — กล\u0e31บหน\u0e49า Title แล\u0e49วเข\u0e49าใหม\u0e48", "Session check failed. Go back to the title screen and enter again.", "Verifikasi sesi gagal. Kembali ke layar judul lalu masuk lagi.", "세션 확인 실패. 타이틀 화면으로 돌아가 다시 접속하세요." }
		},
		{
			"fail_unsupported",
			new string[4] { "เซ\u0e34ร\u0e4cฟน\u0e35\u0e49ย\u0e31งไม\u0e48ม\u0e35 Season Pass", "This server doesn't have Season Pass yet.", "Server ini belum punya Season Pass.", "이 서버에는 아직 시즌 패스가 없습니다." }
		},
		{
			"fail_offline",
			new string[4] { "ต\u0e34ดต\u0e48อเซ\u0e34ร\u0e4cฟไม\u0e48ได\u0e49 — ลองใหม\u0e48อ\u0e35กคร\u0e31\u0e49ง", "Couldn't reach the server. Try again.", "Tidak dapat menghubungi server. Coba lagi.", "서버에 연결할 수 없습니다. 다시 시도하세요." }
		}
	};

	private static string GatewayUrl
	{
		get
		{
			string text = GameManager.GatewayUrl;
			if (string.IsNullOrEmpty(text))
			{
				text = OffServerLink.Gateway;
			}
			return (text ?? string.Empty).TrimEnd('/');
		}
	}

	public static bool Available
	{
		get
		{
			if (OffServerLink.Active && GatewayUrl.Length > 0)
			{
				return !string.IsNullOrEmpty(GameManager.SessionToken);
			}
			return false;
		}
	}

	public static bool HasActiveSeason
	{
		get
		{
			if (Current != null)
			{
				return Current.Active;
			}
			return false;
		}
	}

	public static int ClaimableCount
	{
		get
		{
			if (Current == null || !Current.Active)
			{
				return 0;
			}
			return Current.Claimable;
		}
	}

	public static event Action Updated;

	public static void Reset()
	{
		Current = null;
		Busy = false;
	}

	public static void Fetch(Action<bool> done = null)
	{
		if (!Available)
		{
			LastEvent = "unavailable";
			if (done != null)
			{
				done(obj: false);
			}
			return;
		}
		Post("/season/pass", null, delegate(JObject o, int http)
		{
			bool flag = o != null && Bool(o, "ok");
			if (flag)
			{
				Apply(o);
				LastEvent = "fetch ok tier=" + ((Current != null) ? Current.Tier : 0) + " claimable=" + ClaimableCount;
			}
			else
			{
				LastEvent = "fetch fail HTTP " + http + " " + ((o != null) ? Str(o, "reason") : string.Empty);
				UnityEngine.Debug.Log("[OffServer] Season Pass " + LastEvent);
			}
			if (done != null)
			{
				done(flag);
			}
		});
	}

	public static void Claim(int tier, Action<bool, string> done = null)
	{
		if (!Available)
		{
			if (done != null)
			{
				done(arg1: false, "unavailable");
			}
			return;
		}
		Post("/season/claim", new KeyValuePair<string, string>("tier", Math.Max(0, tier).ToString()), delegate(JObject o, int http)
		{
			bool flag = o != null && Bool(o, "ok");
			string text = ((o != null) ? Str(o, "reason") : string.Empty);
			if (o != null && o["status"] is JObject)
			{
				Apply((JObject)o["status"]);
			}
			if (flag)
			{
				LastClaimed = ((o["tiers"] is JArray jArray) ? string.Join(",", jArray.ToObject<string[]>()) : string.Empty);
				LastEvent = "claim ok " + LastClaimed;
				UnityEngine.Debug.Log("[OffServer] Season Pass claim " + LastClaimed);
			}
			else
			{
				if (text.Length == 0 && http == 404)
				{
					text = "unsupported";
				}
				LastEvent = "claim fail HTTP " + http + " " + text;
				UnityEngine.Debug.Log("[OffServer] Season Pass " + LastEvent);
				UIManager.SystemMsg(Texts.ContainsKey("fail_" + text) ? Text("fail_" + text) : (Text("claim_failed") + " (" + text + ")"));
			}
			if (done != null)
			{
				done(flag, text);
			}
		});
	}

	private static void Post(string path, KeyValuePair<string, string>? field, Action<JObject, int> done)
	{
		if (Busy)
		{
			LastEvent = "busy";
			done(null, 0);
			return;
		}
		Busy = true;
		HTTPRequest hTTPRequest = new HTTPRequest(new Uri(GatewayUrl + path), HTTPMethods.Post, delegate(HTTPRequest req, HTTPResponse resp)
		{
			Busy = false;
			JObject jObject = null;
			int arg = resp?.StatusCode ?? 0;
			try
			{
				if (resp != null && !string.IsNullOrEmpty(resp.DataAsText))
				{
					jObject = JObject.Parse(resp.DataAsText);
				}
			}
			catch (Exception)
			{
				jObject = null;
			}
			if (jObject == null)
			{
				UnityEngine.Debug.Log("[OffServer] Season Pass " + path + " ล\u0e49ม: " + req.State.ToString() + " HTTP " + arg + ((req.Exception != null) ? (" " + req.Exception.Message) : string.Empty));
			}
			done(jObject, arg);
		});
		hTTPRequest.FormUsage = HTTPFormUsage.UrlEncoded;
		hTTPRequest.AddHeader("Authorization", GameManager.SessionToken);
		hTTPRequest.AddHeader("Accept", "application/json");
		hTTPRequest.AddHeader("X-OffServer-Client", OffServerLink.ClientVersion);
		hTTPRequest.AddHeader("X-OffServer-Caps", "nametag-emoji, item-link, server-popup, voice-chat, i18n-keys");
		hTTPRequest.AddField("lang", OffServerDiscordLink.Lang);
		if (field.HasValue)
		{
			hTTPRequest.AddField(field.Value.Key, field.Value.Value);
		}
		hTTPRequest.ConnectTimeout = TimeSpan.FromSeconds(10.0);
		hTTPRequest.Timeout = TimeSpan.FromSeconds(15.0);
		hTTPRequest.DisableCache = true;
		hTTPRequest.Send();
	}

	private static void Apply(JObject o)
	{
		Status status = new Status();
		status.Active = Bool(o, "active");
		status.Now = Num(o, "now");
		status.ReceivedAt = Time.realtimeSinceStartup;
		if (status.Active)
		{
			if (o["season"] is JObject o2)
			{
				status.SeasonId = Str(o2, "id");
				status.SeasonName = Str(o2, "name");
				status.Since = Num(o2, "since");
				status.Until = Num(o2, "until");
			}
			status.Points = (long)Num(o, "points");
			status.Tier = (int)Num(o, "tier");
			status.MaxTier = (int)Num(o, "maxTier");
			status.PointsPerTier = Math.Max(1, (int)Num(o, "pointsPerTier"));
			status.TierPoints = (long)Num(o, "tierPoints");
			status.Claimable = (int)Num(o, "claimable");
			if (o["tiers"] is JArray jArray)
			{
				foreach (JToken item in (IEnumerable<JToken>)jArray)
				{
					if (item is JObject jObject)
					{
						Tier tier = new Tier
						{
							Number = (int)Num(jObject, "tier"),
							State = (int)Num(jObject, "state")
						};
						FillReward(tier.Box, jObject["box"] as JObject);
						FillReward(tier.Special, jObject["special"] as JObject);
						status.Tiers.Add(tier);
					}
				}
			}
			FillMissions(status.Daily, o["daily"] as JObject);
			FillMissions(status.Weekly, o["weekly"] as JObject);
		}
		Current = status;
		if (OffServerSeasonPass.Updated != null)
		{
			try
			{
				OffServerSeasonPass.Updated();
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.Log("[OffServer] Season Pass Updated handler: " + ex.Message);
			}
		}
	}

	private static void FillReward(Reward reward, JObject o)
	{
		if (o == null)
		{
			return;
		}
		reward.TStone = (long)Num(o, "tstone");
		if (!(o["items"] is JArray jArray))
		{
			return;
		}
		foreach (JToken item in (IEnumerable<JToken>)jArray)
		{
			if (item is JObject o2 && Str(o2, "id").Length > 0)
			{
				reward.Items.Add(new WarpRushReward
				{
					Item = new WarpRushReward.ItemInfo
					{
						PrototypeId = Str(o2, "id"),
						Count = Math.Max(1, (int)Num(o2, "count")),
						Level = Math.Max(1, (int)Num(o2, "level"))
					}
				});
			}
		}
	}

	private static void FillMissions(MissionList list, JObject o)
	{
		if (o == null)
		{
			return;
		}
		list.ResetAt = Num(o, "resetAt");
		list.MaxPoints = (int)Num(o, "maxPoints");
		if (!(o["missions"] is JArray jArray))
		{
			return;
		}
		foreach (JToken item in (IEnumerable<JToken>)jArray)
		{
			if (item is JObject o2)
			{
				list.Missions.Add(new Mission
				{
					Id = Str(o2, "id"),
					Name = Str(o2, "name"),
					Event = Str(o2, "event"),
					Count = (int)Num(o2, "count"),
					Progress = (int)Num(o2, "progress"),
					Points = (int)Num(o2, "points"),
					Done = Bool(o2, "done")
				});
			}
		}
	}

	private static bool Bool(JObject o, string key)
	{
		if (!(o?[key] is JValue { Value: not null } jValue))
		{
			return false;
		}
		if (jValue.Value is bool)
		{
			return (bool)jValue.Value;
		}
		string text = jValue.Value.ToString().Trim().ToLowerInvariant();
		if (!(text == "true"))
		{
			return text == "1";
		}
		return true;
	}

	private static double Num(JObject o, string key)
	{
		if (!(o?[key] is JValue { Value: not null } jValue))
		{
			return 0.0;
		}
		try
		{
			if (jValue.Value is IConvertible)
			{
				return Convert.ToDouble(jValue.Value, CultureInfo.InvariantCulture);
			}
		}
		catch (Exception)
		{
		}
		if (!double.TryParse(jValue.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0.0;
		}
		return result;
	}

	private static string Str(JObject o, string key)
	{
		JToken jToken = o?[key];
		if (jToken == null || jToken.Type == JTokenType.Null)
		{
			return string.Empty;
		}
		return jToken.ToString();
	}

	public static string[] MissionLines()
	{
		Status current = Current;
		if (current != null && current.Active)
		{
			double predictedServerTime = Connections.Frontend.GetPredictedServerTime();
			return new string[2]
			{
				MissionBlock(Text("daily"), current.Daily, predictedServerTime),
				MissionBlock(Text("weekly"), current.Weekly, predictedServerTime)
			};
		}
		return new string[1] { Text("no_season") };
	}

	private static string MissionBlock(string title, MissionList list, double now)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("[b]").Append(title).Append("[/b]  ")
			.Append(Text("earned", list.EarnedPoints.ToString("N0"), list.MaxPoints.ToString("N0")));
		double num = list.ResetAt - now;
		if (num > 0.0)
		{
			stringBuilder.Append("  ").Append(Text("reset_in", TimedeltaFormatter.Format(num, 1, "min")));
		}
		foreach (Mission mission in list.Missions)
		{
			stringBuilder.Append('\n');
			if (mission.Done)
			{
				stringBuilder.Append("[7fd67f]");
			}
			stringBuilder.Append("- ").Append(mission.Name).Append("  ")
				.Append(mission.Progress)
				.Append('/')
				.Append(mission.Count)
				.Append("  +")
				.Append(mission.Points.ToString("N0"));
			if (mission.Done)
			{
				stringBuilder.Append("  ").Append(Text("done")).Append("[-]");
			}
		}
		return stringBuilder.ToString();
	}

	public static string Text(string key, params object[] args)
	{
		if (!Texts.TryGetValue(key, out var value))
		{
			return key;
		}
		string text = OffServerDiscordLink.Lang switch
		{
			"th" => value[0], 
			"id" => value[2], 
			"ko" => value[3], 
			_ => value[1], 
		};
		if (args == null || args.Length == 0)
		{
			return text;
		}
		try
		{
			return string.Format(text, args);
		}
		catch (FormatException)
		{
			return text;
		}
	}
}
