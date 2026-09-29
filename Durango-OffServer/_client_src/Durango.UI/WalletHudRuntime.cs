using Durango.Logic.Item;
using Messages;
using Shared.Economy;
using UnityEngine;

namespace Durango.UI;

/// <summary>
/// HUD compacto de moedas para builds sem o prefab CurrencyGroup original.
/// Le diretamente InventorySystem.Wallet, a mesma Wallet recebida do servidor.
/// </summary>
public sealed class WalletHudRuntime : MonoBehaviour
{
    private const float BaseHeight = 34f;
    private const float BaseWidth = 174f;
    private const float BaseGap = 4f;
    private const float BaseMargin = 14f;

    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private bool _stylesReady;

    private long _lastTStone = long.MinValue;
    private long _lastWarpGem = long.MinValue;
    private long _lastDurangoCoin = long.MinValue;
    private bool _loggedVisible;

    private void OnGUI()
    {
        if (!Application.isPlaying ||
            !GameManager.IsMainScene ||
            string.IsNullOrEmpty(GameManager.PlayerId) ||
            !GameSystem<InventorySystem>.HasInstance())
        {
            return;
        }

        Wallet wallet = InventorySystem.Wallet;
        long tStone = wallet.GetBalance(Currency.TStone);
        long warpGem = wallet.GetBalance(Currency.Gem);
        long durangoCoin = wallet.GetBalance(Currency.Coin);

        EnsureStyles();

        float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.35f);
        float width = BaseWidth * scale;
        float height = BaseHeight * scale;
        float gap = BaseGap * scale;
        float margin = BaseMargin * scale;
        float totalWidth = width * 3f + gap * 2f;

        float x = Screen.width - margin - totalWidth;
        float y = margin;

        DrawCurrency(
            new Rect(x, y, width, height),
            "T-Stone",
            Durango.Logic.Item.Inventory.CurrencyFormat(tStone));

        DrawCurrency(
            new Rect(x + width + gap, y, width, height),
            "Warp Gem",
            Durango.Logic.Item.Inventory.CurrencyFormat(warpGem));

        DrawCurrency(
            new Rect(x + (width + gap) * 2f, y, width, height),
            "Durango Coin",
            Durango.Logic.Item.Inventory.CurrencyFormat(durangoCoin));

        if (!_loggedVisible)
        {
            _loggedVisible = true;
            Debug.Log("[ui-wallet-runtime] HUD de moedas visivel no topo.");
        }

        if (tStone != _lastTStone ||
            warpGem != _lastWarpGem ||
            durangoCoin != _lastDurangoCoin)
        {
            _lastTStone = tStone;
            _lastWarpGem = warpGem;
            _lastDurangoCoin = durangoCoin;

            Debug.Log(
                "[ui-wallet-runtime] saldos: T-Stone=" + tStone +
                " WarpGem=" + warpGem +
                " DurangoCoin=" + durangoCoin);
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _stylesReady = true;

        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 58f), 14, 20),
            padding = new RectOffset(8, 8, 4, 4)
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 64f), 13, 18),
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
    }

    private void DrawCurrency(Rect rect, string name, string amount)
    {
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.Label(rect, name + "  " + amount, _labelStyle);
    }
}
