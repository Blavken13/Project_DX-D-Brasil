using System;
using System.Collections;
using Durango.System;
using Durango.Terrain;
using Durango.Utils;
using L10N;
using UnityEngine;

namespace Durango.UI;

public abstract class LoadingCurtainBase : MonoBehaviour
{
	public enum LoadingState
	{
		Open,
		Closing,
		Closed
	}

	protected static bool IsChunkLoadFailed;

	private UIWidget _widget;

	protected float Duration = 0.5f;

	public Action<LoadingState> StateChanged { get; set; }

	protected LoadingState State { get; private set; }

	public UIWidget Widget
	{
		get
		{
			if (_widget == null)
			{
				return _widget = GetComponent<UIWidget>();
			}
			return _widget;
		}
	}

	protected void SetState(LoadingState state)
	{
		State = state;
		if (StateChanged != null)
		{
			StateChanged(state);
		}
	}

	protected IEnumerator WaitForChunkLoading()
	{
		IsChunkLoadFailed = false;
		float beginTime = Time.realtimeSinceStartup;
		while (!Singleton<TerrainBase>.Instance().IsReady)
		{
			yield return null;
			float num = 60f;
			if (Time.realtimeSinceStartup - beginTime > num)
			{
				IsChunkLoadFailed = true;
				string text = ((!TerrainBase.IsPlayerInitialized) ? T._("플레이어 정보를 불러오는데 실패하였습니다.") : T._("지형 정보를 불러오는데 실패하였습니다."));
				string text2 = T._("화면을 터치 후 다시 시도해 주세요.");
				GameManager.LastEvictedMsg = ((!Platform.Instance.UsePCUI) ? (text + "\n" + text2) : text);
				Singleton<GameManager>.Instance().MoveToTitle();
				break;
			}
		}
	}

	protected IEnumerator Fadein()
	{
		float remainTime = Duration;
		while (remainTime > 0f)
		{
			remainTime -= Time.deltaTime;
			Widget.alpha = Mathf.Clamp01(1f - remainTime / Duration);
			yield return null;
		}
	}

	protected IEnumerator Fadeout()
	{
		float remainTime = Duration;
		while (remainTime > 0f)
		{
			remainTime -= Time.deltaTime;
			Widget.alpha = Mathf.Clamp01(remainTime / Duration);
			yield return null;
		}
	}
}
