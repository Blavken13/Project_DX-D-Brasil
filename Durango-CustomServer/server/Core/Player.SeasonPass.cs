using Shared.Quest;

namespace Durango.Online;

public partial class Player
{
    private void NoteSeasonQuestEvent(QuestEventType ev, int amount)
    {
        string action = ev switch
        {
            QuestEventType.Hunted => "hunt",
            QuestEventType.Collected => "collect",
            QuestEventType.Crafted => "craft",
            QuestEventType.AnimalTamed => "tame",
            _ => null
        };
        if (action != null) NoteSeasonAction(action, amount);
    }

    private void NoteSeasonAction(string action, int amount)
    {
        if (_context?.SeasonPass == null || SeasonPassCatalog.Current == null) return;
        if (!SeasonPassCatalog.ApplyEvent(_context.SeasonPass, action, amount, SeasonPassCatalog.Now())) return;
        OnContextChanged();
        _context.Save();
    }
}
