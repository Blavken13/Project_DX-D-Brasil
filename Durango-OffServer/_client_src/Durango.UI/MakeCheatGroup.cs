using Durango.Logic.Clusters;
using Durango.UI.Control;
using Durango.Utils;
using NestedPrefab;
using UnityEngine;

namespace Durango.UI;

public class MakeCheatGroup : UIBase
{
	public enum Tab
	{
		Build,
		Item,
		Gathering,
		Animal,
		Market
	}

	[SerializeField]
	private UITitle _titleWidget;

	[SerializeField]
	private NestedPrefabLinker _tabLinker;

	[EnumList(typeof(Tab), false, 0, -1)]
	[SerializeField]
	private GameObject[] _pages;

	private HorizontalTabList _tabList;

	private void Awake()
	{
		_tabList = _tabLinker.Object.GetComponent<HorizontalTabList>();
		_tabList.Clicked += SelectTab;
		_tabList.BeginLoad();
		Tab[] array = Enums<Tab>.All();
		foreach (Tab tab in array)
		{
			_tabList.AddText(GetTabText(tab));
		}
		_tabList.EndLoadByFixedSize(200);
		SelectTab(0);
		SetChildrenActive(activated: false);
	}

	private void Start()
	{
		_titleWidget.Object.SetTitle("Cheat");
	}

	private string GetTabText(Tab tab)
	{
		return tab switch
		{
			Tab.Build => "Build", 
			Tab.Item => "Item", 
			Tab.Gathering => "Collectible", 
			Tab.Animal => "Animal", 
			Tab.Market => "Market", 
			_ => tab.ToString(), 
		};
	}

	protected override bool TryOpen()
	{
		if (!OffServerLink.IsAdmin)
		{
			return false;
		}
		return base.TryOpen();
	}

	public void OpenTab(Tab tab)
	{
		SelectTab((int)tab);
		Open();
	}

	private void SelectTab(int index)
	{
		_tabList.Select(index);
		Tab[] array = Enums<Tab>.All();
		for (int i = 0; i < array.Length; i++)
		{
			_pages[i].SetActive(i == index);
		}
	}
}
