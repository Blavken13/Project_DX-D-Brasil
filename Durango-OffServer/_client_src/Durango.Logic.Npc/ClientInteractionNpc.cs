using Durango.Network;
using Durango.Render.Camera;
using Durango.Utils;
using InteractionData;
using L10N;
using Messages;
using Shared.Quest;
using UnityEngine;

namespace Durango.Logic.Npc;

public class ClientInteractionNpc : SelectableObject
{
	private int _npcIndex;

	private string _displayName = string.Empty;

	private string _actionName = "대화하기";

	public int NpcIndex => _npcIndex;

	public static ClientInteractionNpc Attach(GameObject host, int npcIndex, string entityId, string displayName)
	{
		ClientInteractionNpc obj = host.GetComponent<ClientInteractionNpc>() ?? host.AddComponent<ClientInteractionNpc>();
		obj._npcIndex = npcIndex;
		obj._displayName = displayName;
		obj.SetEntityId(entityId);
		obj.Selectable = true;
		return obj;
	}

	public override void InteractionTouched()
	{
		KUtility.DelayedCall(this, MakeInteractionMenuList, 0.1f);
	}

	public override bool MenuClicked(GameObject target, InteractionMenuData menu)
	{
		if (menu.Id != "talk")
		{
			return false;
		}
		Connections.Frontend.Send(new InteractWithEpicNPC
		{
			Npc = (EpicNPCType)_npcIndex,
			ItemIds = new string[0]
		});
		NpcWanderAI component = GetComponent<NpcWanderAI>();
		if (component != null)
		{
			component.OnTalked();
		}
		return true;
	}

	private void MakeInteractionMenuList()
	{
		InteractionMenuList menuList = GameSystem<InteractionSystem>.Instance().MenuList;
		menuList.Reset();
		menuList.Add(new InteractionMenuData(Interaction.ClientSidePropAction)
		{
			Name = T._(_actionName),
			Icon = "act_talkie",
			Id = "talk"
		});
		menuList.Name = GetName();
		menuList.Apply();
		Singleton<CameraController>.Instance().Target(base.gameObject, 0.3f);
	}

	public override string GetName()
	{
		return T._(_displayName);
	}
}
