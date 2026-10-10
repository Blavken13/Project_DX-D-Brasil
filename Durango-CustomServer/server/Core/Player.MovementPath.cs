using System;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private Movement[] _receivedMovements;
    private Location[] _storedMovementPath;
    private World _movementWorld;
    private WorldPosition _movementAnchor;

    private Movement[] MovementPath()
    {
        var stored = _context.AppearPlayer.Move.Movements;
        if (_receivedMovements != null && ReferenceEquals(_movementWorld, _world) && stored?.Length > 0 &&
            ReferenceEquals(stored[0].Path, _storedMovementPath) && _storedMovementPath.Length > 0 &&
            _storedMovementPath[0].Position.x == _movementAnchor.x && _storedMovementPath[0].Position.y == _movementAnchor.y)
            return _receivedMovements;
        // Teleport, resurrection, or world transfer replaces the movement anchor.
        _receivedMovements = null;
        return stored;
    }
}
