using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;

namespace Durango.Online;

public partial class Player
{
    private bool _drawingWater;
    private static JObject _waterConstants;

    private static int WaterCapacity(Item item)
    {
        if (item.Tags?.Any(t => t.Id == "container") != true || item.Durability?.Get() <= 0) return 0;
        var container = ItemPerformance.Of(item.Prototype, item.Level).FirstOrDefault(p => p.Id == "container");
        float capacity = container.Nums?.GetValueOrDefault("capacity") ?? 0f;
        return float.IsFinite(capacity) ? (int)Math.Clamp(Math.Floor(capacity), 0, 1000) : 0;
    }

    private void HandleDrawWater(DrawWater msg, uint seq)
    {
        if (_drawingWater)
        {
            Send(new Abort { Text = "Aguarde a coleta de água terminar." }, seq);
            return;
        }
        var pos = PlayerPosition();
        Point2 tile = WorldStatusRules.TileFromWorldPosition(pos.x, pos.y);
        Biome biome = WorldStatusRules.UnmaskBiome(_world.BiomeAt(tile));
        if (!_context.AppearPlayer.IsAlive || !WorldStatusRules.IsWaterBiome(biome))
        {
            Send(new Abort { Text = "Entre na água para encher o recipiente." }, seq);
            return;
        }
        Item tool = string.IsNullOrEmpty(msg.ToolItemId)
            ? _context.InventoryItems.FirstOrDefault(i => WaterCapacity(i) > 0)
            : _context.InventoryItems.FirstOrDefault(i => i.Id == msg.ToolItemId);
        int amount = WaterCapacity(tool);
        if (amount <= 0)
        {
            Send(new Abort { Text = "Escolha um recipiente em bom estado para coletar água." }, seq);
            return;
        }
        _waterConstants ??= Json.ReadFromFile<JObject>("constants")?["put_water_in_container"] as JObject ?? new();
        float duration = Math.Clamp((float?)_waterConstants["duration"] ?? 4f, 0f, GatheringTuning.MaxCollectSeconds);
        float energy = Math.Max(0f, (float?)_waterConstants["energy"] ?? 2f) * EnergyCostScale();
        if (_survival.ValueAt(SurvivalState.KeyEnergy, Gauge.CurrentTime) < energy)
        {
            Send(new Abort { Text = "Você precisa recuperar energia para coletar água." }, seq);
            return;
        }
        _drawingWater = true;
        Send(default(ReplySequenceMark), seq);
        Send(new Messages.Timer { Duration = duration }, seq);
        void Finish(string error = null)
        {
            _drawingWater = false;
            if (error != null) Send(new Abort { Text = error }, seq);
            else Send(default(OK), seq);
            Send(default(ReplySequenceMark), seq);
        }
        _pendingCollects.Add(new PendingCollect(Gauge.CurrentTime + duration, () =>
        {
            var currentPos = PlayerPosition();
            Point2 currentTile = WorldStatusRules.TileFromWorldPosition(currentPos.x, currentPos.y);
            Item currentTool = _context.InventoryItems.FirstOrDefault(i => i.Id == tool.Id);
            if (!_context.AppearPlayer.IsAlive || currentTile.x != tile.x || currentTile.y != tile.y ||
                WaterCapacity(currentTool) < amount)
            { Finish("Coleta de água interrompida. Tente novamente com o recipiente."); return; }
            string prototype = biome is Biome.ColdOcean or Biome.WarmOcean ? "salt_water" : "water";
            int level = Math.Clamp(Math.Min(_world.RegionLevel, GatheringSkillLevel), 1, 70);
            var items = new List<Item>();
            for (int i = 0; i < amount; i++)
            {
                Item? item = Cheats.MakeItem(prototype, level);
                if (!item.HasValue) { Finish("Este recurso de água não está disponível."); return; }
                items.Add(item.Value);
            }
            if (_context.InventoryItems.Sum(i => (long)Math.Max(1, i.Size)) + items.Count > CurrentInventoryCapacity)
            { Finish("Não há espaço suficiente na mochila para a água."); return; }
            if (_survival.ValueAt(SurvivalState.KeyEnergy, Gauge.CurrentTime) < energy)
            { Finish("Você precisa recuperar energia para coletar água."); return; }
            _survival.Add(SurvivalState.KeyEnergy, -energy);
            FlushSurvival();
            AddItems(items);
            Send(new InventoryUpdated { EntityId = EntityId, Items = items.ToArray() });
            WearTool(tool.Id);
            OnContextChanged();
            Finish();
        }, () => Finish("Coleta de água interrompida.")));
    }
}
