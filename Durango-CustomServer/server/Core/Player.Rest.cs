using System;
using System.Linq;
using Messages;
using Yaml;

namespace Durango.Online;

public partial class Player
{
    private string _restArtifactId;
    private double _restStartedAt;

    private void HandleRestOn(RestOn message, uint seq)
    {
        int level;
        bool shelter;
        if (!string.IsNullOrEmpty(message.EntityId))
        {
            var artifact = _world.ArtifactManager.Get(message.EntityId);
            var blueprint = artifact.HasValue ? BlueprintStore.GetBlueprint(artifact.Value.EntityType) : null;
            if (!artifact.HasValue || artifact.Value.States.BuildingState != Shared.Building.BuildingState.Completed ||
                blueprint?.Components?.Contains("Shelter") != true ||
                !IsWithinTiles(artifact.Value.Tile, ArtifactReachTiles + Math.Max(artifact.Value.Size.x, artifact.Value.Size.y)))
            { Send(new Abort { Text = "Abrigo ou fogueira indisponivel para descansar." }, seq); return; }
            level = Math.Max(1, (int)artifact.Value.States.Level);
            shelter = true;
            _restArtifactId = artifact.Value.EntityId;
        }
        else
        {
            shelter = TryGetNearbyRestLevel(out level);
            _restArtifactId = null;
        }
        _restStartedAt = Gauge.CurrentTime;
        _survival.SetMoving(false);
        _survival.SetResting(true, level, acceleratedFatigue: shelter);
        ApplyTimedStatusEffect("rest", level, durationOverride: 0);
        SendStatusEffects();
        FlushSurvival();
        Send(default(OK), seq);
        OnContextChanged();
    }

    private bool PreserveRestDuringAttachment(Movement movement, WorldPosition position)
    {
        if (!_survival.IsResting || _restArtifactId == null) return false;
        var artifact = _world.ArtifactManager.Get(_restArtifactId);
        if (!artifact.HasValue) return false;
        var target = artifact.Value;
        // O cliente encaixa o personagem no ponto de sentar antes de enviar RestOn.
        // Esse Move pode chegar depois e deve atualizar a posição sem cancelar descanso.
        float reach = 400f + Math.Max(target.Size.x, target.Size.y) * 200f;
        float dx = position.x - target.Tile.x * 200f, dy = position.y - target.Tile.y * 200f;
        if (dx * dx + dy * dy > reach * reach) return false;
        string motion = movement.MotionName ?? "";
        bool pose = new[] { "Rest", "Sit", "Sleep", "Lay", "Lie" }
            .Any(name => motion.Contains(name, StringComparison.OrdinalIgnoreCase));
        bool arrival = Gauge.CurrentTime - _restStartedAt <= 1 && movement.Path?.Length > 0 &&
            movement.Path[^1].Time <= _restStartedAt;
        return pose || arrival;
    }
}
