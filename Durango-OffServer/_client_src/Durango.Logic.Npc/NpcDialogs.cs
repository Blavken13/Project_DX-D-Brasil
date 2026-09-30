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
				new string[2] { "0", "Charlie, todos os recém-chegados de Ancora chegaram?" },
				new string[2] { "1", "Todos! A fogueira continua acesa e o pessoal está animado." },
				new string[2] { "0", "Ótimo. Não deixe ninguém ir sozinho para uma Ilha Instável." },
				new string[2] { "1", "Entendido, K! Vamos manter o otimismo!" }
			}
		},
		{
			"k|d383",
			new string[3][]
			{
				new string[2] { "1", "Relatório da Companhia: o navio de suprimentos chega amanhã." },
				new string[2] { "0", "Levem tudo ao armazém do acampamento. Nada de vender os suprimentos." },
				new string[2] { "1", "Entendido. Agente D383, ordem recebida." }
			}
		},
		{
			"k|x",
			new string[4][]
			{
				new string[2] { "1", "K, você desobedeceu às ordens de novo. Salvar pessoas não é o trabalho da Companhia." },
				new string[2] { "0", "Se deixarmos todos morrerem, não vai sobrar ninguém para a Companhia contratar." },
				new string[2] { "1", "...O Comitê vai registrar isso." },
				new string[2] { "0", "Pode registrar. Ainda tenho gente para salvar." }
			}
		},
		{
			"k|lama",
			new string[4][]
			{
				new string[2] { "1", "K, traga algumas amostras de água da nova ilha para mim." },
				new string[2] { "0", "Doutor, eu salvo vidas. Não sou entregadora." },
				new string[2] { "1", "Mas esta pesquisa pode salvar milhares de vidas!" },
				new string[2] { "0", "...Tudo bem. Só uma garrafa." }
			}
		},
		{
			"liu|nowak",
			new string[4][]
			{
				new string[2] { "0", "Novak, vocês estão derrubando árvores demais." },
				new string[2] { "1", "Liu, sem madeira não teremos casas para os recém-chegados." },
				new string[2] { "0", "Então plantem novas árvores. O Fórum vai ficar de olho." },
				new string[2] { "1", "Está bem, vamos plantar o dobro. Satisfeita?" }
			}
		},
		{
			"lama|liu",
			new string[3][]
			{
				new string[2] { "0", "Liu, aquelas flores perto do penhasco parecem ser de uma espécie nova." },
				new string[2] { "1", "Dr. Lamar, não colha flores demais." },
				new string[2] { "0", "Só três flores. Pela ciência!" }
			}
		},
		{
			"charlie|pia",
			new string[3][]
			{
				new string[2] { "0", "Pia, dê um sorriso! O dia está lindo." },
				new string[2] { "1", "...Charlie, como você consegue sorrir todos os dias?" },
				new string[2] { "0", "Estamos vivos, não estamos?" }
			}
		},
		{
			"e|f",
			new string[3][]
			{
				new string[2] { "0", "F, você viu G?" },
				new string[2] { "1", "Acho que foi procurar comida no rio." },
				new string[2] { "0", "De novo...?" }
			}
		},
		{
			"f|g",
			new string[3][]
			{
				new string[2] { "1", "F, encontrei uma fruta estranha. Será que dá para comer?" },
				new string[2] { "0", "Não! Pergunte ao Dr. Lamar primeiro." },
				new string[2] { "1", "Mas o cheiro está tão bom!" }
			}
		},
		{
			"mccain|rodriguez",
			new string[2][]
			{
				new string[2] { "0", "Rodriguez, o novo barco ainda precisa de muita corda." },
				new string[2] { "1", "Vá colher juncos no lago. Acabei de ver um monte por lá." }
			}
		},
		{
			"sawarat|zein",
			new string[2][]
			{
				new string[2] { "0", "Zein, quem fica de guarda esta noite?" },
				new string[2] { "1", "Eu. Deixe a fogueira bem acesa." }
			}
		},
		{
			"hauata|maki",
			new string[2][]
			{
				new string[2] { "0", "Maki, você já tinha visto o mar na sua terra natal?" },
				new string[2] { "1", "Já, mas não havia dinossauros nadando nele!" }
			}
		},
		{
			"josipovic|nowak",
			new string[2][]
			{
				new string[2] { "0", "Novak, pode me emprestar seu machado?" },
				new string[2] { "1", "Devolva afiado, por favor. Da última vez a lâmina voltou toda lascada." }
			}
		}
	};

	private static readonly string[] Greet = new string[5] { "{b}, como você está hoje?", "Ei, {b}, ouviu os dinossauros ontem à noite?", "{b}, ainda há comida no armazém do acampamento?", "{b}, já foi ao porto? O barco chegou.", "{b}, o Dr. Lamar está procurando você." };

	private static readonly string[] Reply = new string[5] { "Vou levando, {a}. Estar vivo já é uma boa notícia.", "Ouvi. Devia ser enorme!", "Está acabando. Precisamos buscar mais.", "Ainda não. Vou passar lá daqui a pouco.", "De novo? Já vou falar com ele." };

	private static readonly string[] Close = new string[4] { "Bom, é melhor voltar ao trabalho.", "Tome cuidado!", "Nos vemos na fogueira.", "Não se esqueça de beber água." };

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
