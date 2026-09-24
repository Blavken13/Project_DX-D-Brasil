using System.Collections;
using System.Collections.Generic;
using Durango.UI;
using UnityEngine;

namespace Durango.Logic.Npc;

public class NpcConversation : MonoBehaviour
{
	private const float PairDistance = 700f;

	private const float LineSeconds = 3.2f;

	private readonly Dictionary<string, float> _cooldown = new Dictionary<string, float>();

	private readonly HashSet<string> _talking = new HashSet<string>();

	private void Start()
	{
		StartCoroutine(Loop());
	}

	private IEnumerator Loop()
	{
		while (true)
		{
			yield return new WaitForSeconds(2f);
			List<NpcWanderAI> list = new List<NpcWanderAI>();
			foreach (GameObject instance in NpcSpawner.Instances)
			{
				if (!(instance == null))
				{
					NpcWanderAI component = instance.GetComponent<NpcWanderAI>();
					if (component != null && component.enabled && component.IsIdle && !_talking.Contains(component.NpcId))
					{
						list.Add(component);
					}
				}
			}
			for (int i = 0; i < list.Count; i++)
			{
				for (int j = i + 1; j < list.Count; j++)
				{
					NpcWanderAI npcWanderAI = list[i];
					NpcWanderAI npcWanderAI2 = list[j];
					if (_talking.Contains(npcWanderAI.NpcId) || _talking.Contains(npcWanderAI2.NpcId))
					{
						continue;
					}
					Vector3 vector = npcWanderAI.transform.position - npcWanderAI2.transform.position;
					vector.y = 0f;
					if (!(vector.magnitude > 700f))
					{
						string key = ((npcWanderAI.NpcId.CompareTo(npcWanderAI2.NpcId) < 0) ? (npcWanderAI.NpcId + "|" + npcWanderAI2.NpcId) : (npcWanderAI2.NpcId + "|" + npcWanderAI.NpcId));
						if ((!_cooldown.TryGetValue(key, out var value) || !(Time.time < value)) && !(Random.value < 0.4f))
						{
							_cooldown[key] = Time.time + 60f + Random.Range(0f, 60f);
							StartCoroutine(Talk(npcWanderAI, npcWanderAI2));
						}
					}
				}
			}
		}
	}

	private IEnumerator Talk(NpcWanderAI a, NpcWanderAI b)
	{
		_talking.Add(a.NpcId);
		_talking.Add(b.NpcId);
		List<string[]> lines = NpcDialogs.Pick(a, b);
		float seconds = (float)lines.Count * 3.2f + 1f;
		a.BeginTalkWith(b.transform, seconds);
		b.BeginTalkWith(a.transform, seconds);
		yield return new WaitForSeconds(0.8f);
		foreach (string[] item in lines)
		{
			Say((item[0] == "0") ? a : b, item[1], 3.2f);
			yield return new WaitForSeconds(3.2f);
		}
		_talking.Remove(a.NpcId);
		_talking.Remove(b.NpcId);
	}

	public static void Say(NpcWanderAI npc, string text, float seconds)
	{
		if (!(npc == null) && !(npc.Character == null))
		{
			ChatBubbleGroup chatBubbleGroup = UIManager.FindScript<ChatBubbleGroup>();
			if (!(chatBubbleGroup == null))
			{
				chatBubbleGroup.Show(npc.Character.ChatableBase, text, null, npc.PortraitIcon, Color.white, null, null, alwaysInScreen: false, seconds);
			}
		}
	}
}
