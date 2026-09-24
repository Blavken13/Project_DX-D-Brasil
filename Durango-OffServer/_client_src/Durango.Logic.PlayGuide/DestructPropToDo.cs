using UnityEngine;

namespace Durango.Logic.PlayGuide;

public class DestructPropToDo : ToDoBase
{
	private readonly string _targetId;

	private ClientRemovableProp _subscribed;

	private float _nextSearchTime;

	public DestructPropToDo(string id)
	{
		_targetId = id;
	}

	private ClientRemovableProp FindProp()
	{
		ClientRemovableProp result = null;
		ClientRemovableProp[] array = Object.FindObjectsOfType<ClientRemovableProp>();
		int num = 0;
		int num2 = array.Length;
		for (int i = 0; i < num2; i++)
		{
			if (array[i].EntityId == _targetId)
			{
				result = array[i];
				num++;
			}
		}
		return result;
	}

	public override void OnAddItem()
	{
		Subscribe();
	}

	private void Subscribe()
	{
		ClientRemovableProp clientRemovableProp = FindProp();
		if (!(clientRemovableProp == null) && !(clientRemovableProp == _subscribed))
		{
			if (_subscribed != null)
			{
				_subscribed.ClientPropDestructed -= ClientPropDestructed;
			}
			clientRemovableProp.ClientPropDestructed += ClientPropDestructed;
			_subscribed = clientRemovableProp;
		}
	}

	public override void Process()
	{
		if ((_subscribed == null || !_subscribed) && Time.time >= _nextSearchTime)
		{
			_nextSearchTime = Time.time + 1f;
			Subscribe();
		}
	}

	public override void OnRemoveItem()
	{
		if (_subscribed != null)
		{
			_subscribed.ClientPropDestructed -= ClientPropDestructed;
			_subscribed = null;
		}
	}

	private void ClientPropDestructed(string entityId)
	{
		if (entityId == _targetId)
		{
			CallComplete();
		}
	}
}
