using System.Collections.Generic;
using UnityEngine;

namespace Durango.Logic.Npc;

public static class NpcDialogs
{
	private static readonly Dictionary<string, string[][]> Pairs = new Dictionary<string, string[][]>
	{
		{
			"k|charlie",
			new string[4][]
			{
				new string[2] { "0", "ชาร\u0e4cล\u0e35 พวกใหม\u0e48จากอ\u0e31งโคร\u0e48ามาถ\u0e36งครบไหม" },
				new string[2] { "1", "ครบ! กองไฟย\u0e31งต\u0e34ดอย\u0e39\u0e48 ท\u0e38กคนอารมณ\u0e4cด\u0e35" },
				new string[2] { "0", "ด\u0e35 อย\u0e48าให\u0e49ใครออกไปเกาะไม\u0e48เสถ\u0e35ยรคนเด\u0e35ยวล\u0e48ะ" },
				new string[2] { "1", "ร\u0e31บทราบคร\u0e31บ K มองโลกในแง\u0e48ด\u0e35ไว\u0e49!" }
			}
		},
		{
			"k|d383",
			new string[3][]
			{
				new string[2] { "1", "รายงานจากบร\u0e34ษ\u0e31ท: เร\u0e37อเสบ\u0e35ยงเข\u0e49าท\u0e48าพร\u0e38\u0e48งน\u0e35\u0e49" },
				new string[2] { "0", "ให\u0e49ขนไปคล\u0e31งแคมป\u0e4cก\u0e48อน ห\u0e49ามเอาไปขาย" },
				new string[2] { "1", "เข\u0e49าใจแล\u0e49ว เอเจนต\u0e4c D383 ร\u0e31บคำส\u0e31\u0e48ง" }
			}
		},
		{
			"k|x",
			new string[4][]
			{
				new string[2] { "1", "K เธอทำเก\u0e34นคำส\u0e31\u0e48งอ\u0e35กแล\u0e49ว การช\u0e48วยคนไม\u0e48ใช\u0e48งานของบร\u0e34ษ\u0e31ท" },
				new string[2] { "0", "ถ\u0e49าปล\u0e48อยพวกเขาตาย ก\u0e47ไม\u0e48ม\u0e35ใครเหล\u0e37อให\u0e49บร\u0e34ษ\u0e31ทจ\u0e49าง" },
				new string[2] { "1", "...คณะกรรมการจะจดไว\u0e49" },
				new string[2] { "0", "จดไปเถอะ ฉ\u0e31นม\u0e35คนต\u0e49องไปช\u0e48วยอ\u0e35ก" }
			}
		},
		{
			"k|lama",
			new string[4][]
			{
				new string[2] { "1", "K เอาต\u0e31วอย\u0e48างน\u0e49ำจากเกาะใหม\u0e48มาให\u0e49ฉ\u0e31นบ\u0e49างส\u0e34" },
				new string[2] { "0", "หมอ ฉ\u0e31นเป\u0e47นคนช\u0e48วยช\u0e35ว\u0e34ต ไม\u0e48ใช\u0e48เด\u0e47กส\u0e48งของ" },
				new string[2] { "1", "แต\u0e48งานว\u0e34จ\u0e31ยน\u0e35\u0e49จะช\u0e48วยช\u0e35ว\u0e34ตได\u0e49เป\u0e47นพ\u0e31น!" },
				new string[2] { "0", "...ก\u0e47ได\u0e49 ขวดเด\u0e35ยวนะ" }
			}
		},
		{
			"liu|nowak",
			new string[4][]
			{
				new string[2] { "0", "โนว\u0e31ค พวกค\u0e38ณต\u0e31ดต\u0e49นไม\u0e49เยอะเก\u0e34นไปแล\u0e49วนะ" },
				new string[2] { "1", "ร\u0e34ว ถ\u0e49าไม\u0e48ม\u0e35ไม\u0e49 ก\u0e47ไม\u0e48ม\u0e35บ\u0e49านให\u0e49พวกใหม\u0e48" },
				new string[2] { "0", "ปล\u0e39กค\u0e37นด\u0e49วยส\u0e34 ฟอร\u0e31มจะจ\u0e31บตาด\u0e39" },
				new string[2] { "1", "ได\u0e49 ๆ ปล\u0e39กค\u0e37นสองเท\u0e48าเลย พอใจไหม" }
			}
		},
		{
			"lama|liu",
			new string[3][]
			{
				new string[2] { "0", "ร\u0e34ว ดอกไม\u0e49ตรงหน\u0e49าผาน\u0e48าจะเป\u0e47นพ\u0e31นธ\u0e38\u0e4cใหม\u0e48" },
				new string[2] { "1", "หมอลามะ อย\u0e48าเก\u0e47บมากเก\u0e34นไปล\u0e48ะ" },
				new string[2] { "0", "แค\u0e48สามดอก เพ\u0e37\u0e48อว\u0e34ทยาศาสตร\u0e4c!" }
			}
		},
		{
			"charlie|pia",
			new string[3][]
			{
				new string[2] { "0", "เป\u0e35ยร\u0e4c ว\u0e31นน\u0e35\u0e49ย\u0e34\u0e49มหน\u0e48อยส\u0e34 อากาศด\u0e35จะตาย" },
				new string[2] { "1", "...ชาร\u0e4cล\u0e35 ค\u0e38ณย\u0e34\u0e49มได\u0e49ท\u0e38กว\u0e31นเลยเหรอ" },
				new string[2] { "0", "ก\u0e47เราย\u0e31งม\u0e35ช\u0e35ว\u0e34ตอย\u0e39\u0e48น\u0e35\u0e48นา" }
			}
		},
		{
			"e|f",
			new string[3][]
			{
				new string[2] { "0", "F นายเห\u0e47น G ไหม" },
				new string[2] { "1", "ไปหาของก\u0e34นท\u0e35\u0e48แม\u0e48น\u0e49ำม\u0e31\u0e49ง" },
				new string[2] { "0", "อ\u0e35กแล\u0e49วเหรอ..." }
			}
		},
		{
			"f|g",
			new string[3][]
			{
				new string[2] { "1", "F ฉ\u0e31นเจอผลไม\u0e49แปลก ๆ ก\u0e34นได\u0e49ไหม" },
				new string[2] { "0", "อย\u0e48า! ถามหมอลามะก\u0e48อน" },
				new string[2] { "1", "แต\u0e48ม\u0e31นหอมมากเลยนะ" }
			}
		},
		{
			"mccain|rodriguez",
			new string[2][]
			{
				new string[2] { "0", "โรดร\u0e34เกซ เร\u0e37อลำใหม\u0e48ต\u0e49องใช\u0e49เช\u0e37อกอ\u0e35กเยอะ" },
				new string[2] { "1", "ไปเก\u0e47บกกท\u0e35\u0e48ทะเลสาบส\u0e34 ฉ\u0e31นเพ\u0e34\u0e48งเห\u0e47นเต\u0e47มเลย" }
			}
		},
		{
			"sawarat|zein",
			new string[2][]
			{
				new string[2] { "0", "เซน ค\u0e37นน\u0e35\u0e49เวรยามใคร" },
				new string[2] { "1", "ฉ\u0e31นเอง เอาไฟให\u0e49แรงหน\u0e48อยล\u0e48ะ" }
			}
		},
		{
			"hauata|maki",
			new string[2][]
			{
				new string[2] { "0", "มาก\u0e34 เธอเคยเห\u0e47นทะเลท\u0e35\u0e48บ\u0e49านเก\u0e34ดไหม" },
				new string[2] { "1", "เคย แต\u0e48ไม\u0e48ม\u0e35ไดโนเสาร\u0e4cว\u0e48ายอย\u0e39\u0e48แบบน\u0e35\u0e49" }
			}
		},
		{
			"josipovic|nowak",
			new string[2][]
			{
				new string[2] { "0", "โนว\u0e31ค ขอย\u0e37มขวานหน\u0e48อย" },
				new string[2] { "1", "ค\u0e37นมาล\u0e31บคมด\u0e49วยนะ คราวก\u0e48อนบ\u0e34\u0e48นหมด" }
			}
		}
	};

	private static readonly string[] Greet = new string[5] { "{b} ว\u0e31นน\u0e35\u0e49เป\u0e47นไงบ\u0e49าง", "เฮ\u0e49 {b} ได\u0e49ย\u0e34นเส\u0e35ยงไดโนเม\u0e37\u0e48อค\u0e37นไหม", "{b} คล\u0e31งแคมป\u0e4cย\u0e31งม\u0e35ของก\u0e34นอย\u0e39\u0e48ไหม", "{b} ไปท\u0e48าเร\u0e37อมาหร\u0e37อย\u0e31ง เร\u0e37อเข\u0e49าแล\u0e49วนะ", "{b} หมอลามะตามหาอย\u0e39\u0e48นะ" };

	private static readonly string[] Reply = new string[5] { "ก\u0e47เร\u0e37\u0e48อย ๆ {a} ย\u0e31งม\u0e35ช\u0e35ว\u0e34ตอย\u0e39\u0e48ก\u0e47ด\u0e35แล\u0e49ว", "ได\u0e49ย\u0e34น ต\u0e31วใหญ\u0e48มากแน\u0e48 ๆ", "เหล\u0e37อน\u0e49อยแล\u0e49ว ต\u0e49องออกไปหาเพ\u0e34\u0e48ม", "ย\u0e31ง เด\u0e35\u0e4bยวแวะไป", "อ\u0e35กแล\u0e49วเหรอ เด\u0e35\u0e4bยวไปหา" };

	private static readonly string[] Close = new string[4] { "เอาล\u0e48ะ ไปทำงานต\u0e48อด\u0e35กว\u0e48า", "ระว\u0e31งต\u0e31วด\u0e49วยนะ", "ไว\u0e49เจอก\u0e31นท\u0e35\u0e48กองไฟ", "อย\u0e48าล\u0e37มด\u0e37\u0e48มน\u0e49ำล\u0e48ะ" };

	public static List<string[]> Pick(NpcWanderAI a, NpcWanderAI b)
	{
		string key = a.NpcId + "|" + b.NpcId;
		string key2 = b.NpcId + "|" + a.NpcId;
		bool flag = false;
		if (!Pairs.TryGetValue(key, out var value) && Pairs.TryGetValue(key2, out value))
		{
			flag = true;
		}
		List<string[]> list = new List<string[]>();
		if (value != null && Random.value < 0.7f)
		{
			string[][] array = value;
			foreach (string[] array2 in array)
			{
				string text = (flag ? ((array2[0] == "0") ? "1" : "0") : array2[0]);
				list.Add(new string[2]
				{
					text,
					array2[1]
				});
			}
			return list;
		}
		list.Add(new string[2]
		{
			"0",
			Fill(Greet[Random.Range(0, Greet.Length)], a, b)
		});
		list.Add(new string[2]
		{
			"1",
			Fill(Reply[Random.Range(0, Reply.Length)], a, b)
		});
		if (Random.value < 0.6f)
		{
			list.Add(new string[2]
			{
				(Random.value < 0.5f) ? "0" : "1",
				Fill(Close[Random.Range(0, Close.Length)], a, b)
			});
		}
		return list;
	}

	private static string Fill(string s, NpcWanderAI a, NpcWanderAI b)
	{
		return s.Replace("{a}", a.DisplayName).Replace("{b}", b.DisplayName);
	}
}
