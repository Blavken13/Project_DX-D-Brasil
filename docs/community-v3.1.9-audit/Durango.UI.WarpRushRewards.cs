using System;
using System.Collections.Generic;
using Durango.Logic;
using Durango.Logic.Clusters;
using Durango.Logic.Shop;
using Durango.Network;
using Durango.UI.Control;
using Durango.UI.Popup;
using Durango.Utils;
using L10N;
using Messages;
using NestedPrefab;
using Shared.Season2;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.UI;

public class WarpRushRewards : MonoBehaviour, IUIInitializable, IScreenResizeReceiver
{
	[SerializeField]
	private NestedPrefabLinker _tabLinker;

	[SerializeField]
	private KScrollView _rewardList;

	[SerializeField]
	private UIScrollView _scrollView;

	[SerializeField]
	private ScrollViewGridBackground _gridBackground;

	[SerializeField]
	private UISprite _rewardBoxSprite;

	[SerializeField]
	private UILabel _levelRewardText;

	[SerializeField]
	private UILabel _cashRewardText;

	[SerializeField]
	private UISprite _cashRewardIcon;

	[SerializeField]
	private UILabel _notOnSaleLabel;

	[SerializeField]
	private UISprite _cashRewardBackground;

	[SerializeField]
	private UISprite _bottomBoxSprite;

	[SerializeField]
	private UISprite _bottomSubLevelGauge;

	[SerializeField]
	private UILabel _bottomDescription;

	[SerializeField]
	private UILabel _bottomSubDescription;

	[SerializeField]
	private SelectableButton _bottomDeliveryButton;

	[SerializeField]
	private SelectableButton _bottomRewardListButton;

	[SerializeField]
	private GameObject _bottomDeliveryButtonBackground;

	[SerializeField]
	private UISpriteLabel _dateLimitLabel;

	[SerializeField]
	private SelectableButton _cashShopLinkButton;

	private HorizontalTabList _subTabList;

	private ResourceType _currentSubTab = ResourceType.Invalid;

	private bool _isDirty;

	private const string SeasonPassIcon = "icon_warprush_reward";

	private bool _seasonTabsLoaded;

	private static bool SeasonMode => WarpRushGroup.SeasonPassMode;

	void IUIInitializable.Init()
	{
		_dateLimitLabel.gameObject.SetActive(value: false);
		_levelRewardText.text = T._("레벨 달성 보상");
		_notOnSaleLabel.text = T._("현재 판매 중인 상품이 아닙니다.");
		_cashShopLinkButton.Clicked = delegate
		{
			Durango.Logic.Shop.Commodity commodity = GameSystem<ShopSystem>.Instance().GetCommodity(Yaml.Util.Singleton<Yaml.WarpRushRewards>.Instance.CashRewardCommodityId);
			if (commodity != null)
			{
				UIManager.FindScript<ShopGroup>().Open(commodity.Id, select: true);
			}
		};
		GameSystem<WarpRushSystem>.Instance().RewardStatusUpdated += SetDirty;
		GameSystem<ShopSystem>.Instance().PurchasesUpdated += SetDirty;
		GameSystem<ShopSystem>.Instance().AcceptableSubPurchasesUpdated += SetDirty;
		_rewardList.Nodes.UseBase = true;
		_rewardList.Nodes.Init(delegate(GameObject go)
		{
			go.GetComponent<WarpRushRewardPhaseWidget>().Init(_scrollView);
		});
		_subTabList = _tabLinker.Object.GetComponent<HorizontalTabList>();
		LoadSubTabs(seasonPass: false);
		_subTabList.Clicked += SelectSubTab;
		_bottomRewardListButton.Text = T._("보상품 목록");
		SelectableButton bottomRewardListButton = _bottomRewardListButton;
		bottomRewardListButton.Clicked = (Action)Delegate.Combine(bottomRewardListButton.Clicked, (Action)delegate
		{
			if (SeasonMode)
			{
				ShowSeasonMissions();
			}
			else
			{
				WarpRushRewardListPopup warpRushRewardListPopup = UIManager.Popup.Tooltip<WarpRushRewardListPopup>();
				warpRushRewardListPopup.Set(_currentSubTab);
				warpRushRewardListPopup.Show();
			}
		});
		GameSystem<WarpRushSystem>.Instance().RewardStatusUpdated += FillBottom;
		GameSystem<SeasonSystem>.Instance().SeasonUpdated += SeasonSystem_SeasonUpdated;
		SeasonSystem_SeasonUpdated();
		OffServerSeasonPass.Updated += OnSeasonPassUpdated;
	}

	void IScreenResizeReceiver.OnChangeScreenSize()
	{
		bool isPortraitScreen = UIManager.IsPortraitScreen;
		_bottomDeliveryButtonBackground.SetActive(isPortraitScreen);
		GetComponent<RectLayoutComponent>().UpdateLayout();
		UIUtility.UpdateAnchors(base.transform);
		UIWidget component = _rewardList.Nodes.BaseObject.GetComponent<UIWidget>();
		_gridBackground.ResetGrid(new Vector2(component.width, 0f), Vector2.zero);
	}

	private void OnEnable()
	{
		if (_seasonTabsLoaded != SeasonMode)
		{
			LoadSubTabs(SeasonMode);
			_currentSubTab = ResourceType.Invalid;
		}
		SetDirty();
		InitSubTab();
		if (SeasonMode)
		{
			OffServerSeasonPass.Fetch();
		}
	}

	private void Update()
	{
		if (_isDirty)
		{
			Refresh();
		}
	}

	private void SetDirty()
	{
		_isDirty = true;
	}

	private void Refresh()
	{
		_isDirty = false;
		for (int i = 0; i < _rewardList.Nodes.Count; i++)
		{
			_rewardList.Nodes[i].GetComponent<WarpRushRewardPhaseWidget>().Refresh();
		}
		if (SeasonMode)
		{
			_cashShopLinkButton.gameObject.SetActive(value: false);
			_cashRewardIcon.gameObject.SetActive(value: false);
		}
		else
		{
			bool flag = GameSystem<WarpRushSystem>.Instance().GetCashRewardPurchase() != null || !GameSystem<WarpRushSystem>.Instance().IsCashRewardPurchasable();
			_cashShopLinkButton.gameObject.SetActive(!flag);
			_cashRewardIcon.gameObject.SetActive(flag);
		}
	}

	public void InitSubTab()
	{
		if (_currentSubTab == ResourceType.Invalid)
		{
			SelectSubTab(0);
			_bottomRewardListButton.MinHeight = _bottomRewardListButton.Widget.height;
			_bottomRewardListButton.ToPreferredSize();
		}
	}

	private void SelectSubTab(int index)
	{
		if (_currentSubTab != (ResourceType)index)
		{
			_currentSubTab = (ResourceType)index;
			_subTabList.Select(index);
			FillRewards();
			FillBottom();
		}
	}

	private void LoadSubTabs(bool seasonPass)
	{
		_subTabList.BeginLoad();
		if (seasonPass)
		{
			OffServerSeasonPass.Status current = OffServerSeasonPass.Current;
			_subTabList.AddText((current != null && current.Active && !string.IsNullOrEmpty(current.SeasonName)) ? current.SeasonName : OffServerSeasonPass.Text("tab_rewards"));
		}
		else
		{
			ResourceType[] array = Enums<ResourceType>.All();
			foreach (ResourceType resourceType in array)
			{
				if (resourceType != ResourceType.Invalid && resourceType != ResourceType.CharlieStone)
				{
					_subTabList.AddText(GetSubTabName(resourceType));
				}
			}
		}
		_subTabList.EndLoadByFixedSize(200);
		_seasonTabsLoaded = seasonPass;
	}

	private void FillRewards()
	{
		if (SeasonMode)
		{
			FillSeasonRewards();
			return;
		}
		_levelRewardText.text = T._("레벨 달성 보상");
		_rewardBoxSprite.spriteName = WarpRushSystem.GetResourceBoxIcon(_currentSubTab);
		Durango.Logic.Shop.Commodity commodity = GameSystem<ShopSystem>.Instance().GetCommodity(Yaml.Util.Singleton<Yaml.WarpRushRewards>.Instance.CashRewardCommodityId);
		_cashRewardText.text = ((commodity != null) ? commodity.Title : T._("브라이언의 선물"));
		if (GameSystem<WarpRushSystem>.Instance().IsCashRewardOnSale())
		{
			_cashRewardIcon.color = PresetColor.UIYellow;
			_cashRewardText.color = PresetColor.UIYellow;
			_cashRewardBackground.color = new Color32(byte.MaxValue, 216, 91, 50);
			_notOnSaleLabel.gameObject.SetActive(value: false);
		}
		else
		{
			_cashRewardIcon.color = new Color(1f, 1f, 1f, 0.3f);
			_cashRewardText.color = new Color(1f, 1f, 1f, 0.3f);
			_cashRewardBackground.color = new Color32(0, 0, 0, 50);
			_notOnSaleLabel.gameObject.SetActive(value: true);
		}
		Yaml.WarpRushRewards instance = Yaml.Util.Singleton<Yaml.WarpRushRewards>.Instance;
		List<SupplyLevel> list = instance.SupplyLevels.Get(_currentSubTab);
		if (list == null)
		{
			return;
		}
		_rewardList.Nodes.BeginLoad();
		foreach (SupplyLevel item in list)
		{
			WarpRushReward levelReward = instance.GetLevelReward(_currentSubTab, item.Level);
			WarpRushReward cashReward = instance.GetCashReward(_currentSubTab, item.Level);
			_rewardList.Nodes.GetNext().GetComponent<WarpRushRewardPhaseWidget>().Set(_currentSubTab, item.Level, levelReward, cashReward);
		}
		_rewardList.Nodes.EndLoad();
	}

	private void FillBottom()
	{
		if (SeasonMode)
		{
			FillSeasonBottom();
		}
		else if (_currentSubTab != ResourceType.Invalid)
		{
			_bottomDeliveryButton.Clicked = null;
			_bottomDeliveryButton.Disabled = false;
			_bottomRewardListButton.Text = T._("보상품 목록");
			_bottomBoxSprite.spriteName = WarpRushSystem.GetResourceBoxIcon(_currentSubTab);
			S02RewardStatus rewardStatus = GameSystem<WarpRushSystem>.Instance().GetRewardStatus(_currentSubTab);
			string boxName = WarpRushSystem.GetBoxName(_currentSubTab);
			_bottomDescription.text = T._("<em>{0:lv:}</em> {1}", rewardStatus.Level, boxName);
			float num = (float)rewardStatus.Count / 10f;
			_bottomSubDescription.text = ((!(num < 1f)) ? string.Empty : $"<em>{rewardStatus.Count}  [icon=icon_arrow_right]</em>");
			string arg = T._("워프 스톤 교환");
			string resourceIcon = WarpRushSystem.GetResourceIcon(_currentSubTab);
			int supplyAmount = Yaml.Util.Singleton<Yaml.WarpRushRewards>.Instance.GetSupplyAmount(_currentSubTab, rewardStatus.Level);
			_bottomDeliveryButton.Text = $"{arg} [preset=round_box?[icon={resourceIcon}]   {supplyAmount}]";
			_bottomSubLevelGauge.fillAmount = num;
		}
	}

	private void FillSeasonRewards()
	{
		OffServerSeasonPass.Status current = OffServerSeasonPass.Current;
		_rewardBoxSprite.spriteName = "icon_warprush_reward";
		_levelRewardText.text = OffServerSeasonPass.Text("row_box");
		_cashRewardText.text = OffServerSeasonPass.Text("row_special");
		_cashRewardIcon.color = PresetColor.UIYellow;
		_cashRewardText.color = PresetColor.UIYellow;
		_cashRewardBackground.color = new Color32(byte.MaxValue, 216, 91, 50);
		_notOnSaleLabel.gameObject.SetActive(value: false);
		_rewardList.Nodes.BeginLoad();
		if (current != null && current.Active)
		{
			int firstClaimable = current.FirstClaimable;
			foreach (OffServerSeasonPass.Tier tier in current.Tiers)
			{
				_rewardList.Nodes.GetNext().GetComponent<WarpRushRewardPhaseWidget>().SetSeasonPass(tier, firstClaimable);
			}
		}
		_rewardList.Nodes.EndLoad();
		if (current != null && current.Active && current.Until > 0.0)
		{
			_dateLimitLabel.gameObject.SetActive(value: true);
			_dateLimitLabel.SetText(SeasonEndsSyncString(current.Until));
		}
		else
		{
			_dateLimitLabel.gameObject.SetActive(value: false);
		}
	}

	private void FillSeasonBottom()
	{
		OffServerSeasonPass.Status current = OffServerSeasonPass.Current;
		_bottomBoxSprite.spriteName = "icon_warprush_reward";
		_bottomSubDescription.text = string.Empty;
		_bottomRewardListButton.Text = OffServerSeasonPass.Text("missions");
		_bottomDeliveryButton.Clicked = ClaimAllSeasonPass;
		if (current == null || !current.Active)
		{
			_bottomDescription.text = OffServerSeasonPass.Text("no_season");
			_bottomSubLevelGauge.fillAmount = 0f;
			_bottomDeliveryButton.Text = OffServerSeasonPass.Text("claim_none");
			_bottomDeliveryButton.Disabled = true;
			return;
		}
		bool flag = current.Tier >= current.MaxTier;
		_bottomDescription.text = (flag ? OffServerSeasonPass.Text("progress_max", current.MaxTier, current.Points.ToString("N0")) : OffServerSeasonPass.Text("progress", current.Tier, current.MaxTier, current.TierPoints.ToString("N0"), current.PointsPerTier.ToString("N0")));
		_bottomSubLevelGauge.fillAmount = (flag ? 1f : Mathf.Clamp01((float)current.TierPoints / (float)current.PointsPerTier));
		_bottomDeliveryButton.Text = ((current.Claimable > 0) ? OffServerSeasonPass.Text("claim_all", current.Claimable) : OffServerSeasonPass.Text("claim_none"));
		_bottomDeliveryButton.Disabled = current.Claimable == 0;
	}

	private static SyncString SeasonEndsSyncString(double until)
	{
		return new SyncString(delegate(out string text, out float period)
		{
			double num = until - Connections.Frontend.GetPredictedServerTime();
			if (num > 0.0)
			{
				text = OffServerSeasonPass.Text("ends_in", TimedeltaFormatter.Format(num, 1, (num > 86400.0) ? "day" : "min"));
				period = (float)(num % (double)TimedeltaFormatter.CurrentMinUnit());
			}
			else
			{
				text = string.Empty;
				period = 0f;
			}
		});
	}

	private void ClaimAllSeasonPass()
	{
		if (OffServerSeasonPass.ClaimableCount <= 0)
		{
			UIManager.SystemMsg(OffServerSeasonPass.Text("claim_none"));
		}
		else
		{
			OffServerSeasonPass.Claim(0);
		}
	}

	private void ShowSeasonMissions()
	{
		SimpleTextListPopup simpleTextListPopup = UIManager.Popup.Tooltip<SimpleTextListPopup>();
		simpleTextListPopup.Set(OffServerSeasonPass.Text("missions_title"), OffServerSeasonPass.MissionLines());
		simpleTextListPopup.Show();
	}

	private void OnSeasonPassUpdated()
	{
		if (SeasonMode && base.gameObject.activeInHierarchy)
		{
			LoadSubTabs(seasonPass: true);
			_subTabList.Select(0);
			FillSeasonRewards();
			FillSeasonBottom();
			SetDirty();
		}
	}

	private static string GetSubTabName(ResourceType resourceType)
	{
		return resourceType switch
		{
			ResourceType.AlphaStone => T._("알파"), 
			ResourceType.BravoStone => T._("브라보"), 
			ResourceType.CharlieStone => T._("찰리"), 
			_ => string.Empty, 
		};
	}

	private void SeasonSystem_SeasonUpdated()
	{
		if (!SeasonMode)
		{
			Season? warpRushSeason = WarpRushSystem.GetWarpRushSeason();
			if (warpRushSeason.HasValue)
			{
				_dateLimitLabel.gameObject.SetActive(value: true);
				_dateLimitLabel.SetText(WarpRushGroup.GetDateLimitSyncString(warpRushSeason.Value.Until, "{0}"));
			}
		}
	}
}
