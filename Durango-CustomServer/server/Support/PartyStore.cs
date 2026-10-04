using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Utils;
using Messages;

namespace Durango.Online;

// Accepted membership survives reconnects; invitations expire after five minutes.
internal static class PartyStore
{
    internal sealed class Member { public bool Accepted; public double InvitedUntil; public string Name; public PartierStatus Status; }
    internal sealed class Team
    {
        public string Id;
        public string Leader;
        public Dictionary<string, Member> Members = new();
    }
    private static readonly object Gate = new();
    private static Dictionary<string, Team> _teams = new();
    private static string _path;
    private static readonly Dictionary<string, (string Name, PartierStatus Status)> Profiles = new();
    internal static Func<double> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    // Server policy; the recovered client has no authoritative party size table.
    internal const int Capacity = 5;
    public static void Load(string playerPath)
    {
        if (string.IsNullOrEmpty(playerPath)) return;
        lock (Gate)
        {
            string path = Path.Combine(Path.GetDirectoryName(playerPath), "parties.json");
            if (_path == path) return;
            _teams = SafeSave.ReadWithBackup(path, "party-store", bytes => Json.Read<Dictionary<string, Team>>(bytes)) ?? new();
            _path = path;
        }
    }
    private static Team Find(string id) => _teams.Values.FirstOrDefault(t => t.Members.ContainsKey(id));
    internal static void UpdateProfile(string id, string name, PartierStatus status, bool save = false)
    {
        lock (Gate)
        {
            Profiles[id] = (name, status);
            Team team = Find(id);
            if (team == null) return;
            team.Members[id].Name = name; team.Members[id].Status = status;
            if (save && _path != null) SafeSave.WriteAtomic(_path, Json.WriteToBytes(_teams), "party-store");
        }
    }
    private static Member NewMember(string id, bool accepted)
    {
        Profiles.TryGetValue(id, out var profile);
        return new Member { Accepted = accepted, InvitedUntil = accepted ? 0 : Clock() + 300, Name = profile.Name ?? id, Status = profile.Status };
    }
    private static void Prune()
    {
        foreach (Team team in _teams.Values)
            foreach (string id in team.Members.Where(m => !m.Value.Accepted && m.Value.InvitedUntil <= Clock()).Select(m => m.Key).ToArray())
                team.Members.Remove(id);
    }
    public static bool Accepted(string id)
    {
        lock (Gate) { Prune(); return Find(id)?.Members[id].Accepted == true; }
    }
    public static bool SameTeam(string a, string b)
    {
        lock (Gate) { Prune(); Team team = Find(a); return team?.Members[a].Accepted == true && team.Members.TryGetValue(b, out Member m) && m.Accepted; }
    }
    public static Team Snapshot(string id)
    {
        lock (Gate) { Prune(); return Json.Read<Team>(Json.Write(Find(id))); }
    }
    public static bool Change(string actor, string action, string target, out string[] affected, out string error)
    {
        lock (Gate)
        {
            Prune();
            string before = Json.Write(_teams);
            affected = Array.Empty<string>(); error = null;
            Team team = Find(actor);
            var ids = new HashSet<string>(team?.Members.Keys ?? Enumerable.Empty<string>()) { actor };
            if (!string.IsNullOrEmpty(target)) ids.Add(target);
            bool leader = team?.Leader == actor;
            switch (action)
            {
                case "make":
                    if (team != null) { error = "Você já participa de um grupo ou possui um convite pendente."; break; }
                    team = new Team { Id = Guid.NewGuid().ToString("N"), Leader = actor };
                    team.Members[actor] = NewMember(actor, true);
                    _teams[team.Id] = team; break;
                case "invite":
                    if (string.IsNullOrEmpty(target) || target == actor || Find(target) != null) { error = "O jogador já possui um grupo ou convite pendente."; break; }
                    if (team == null)
                    {
                        team = new Team { Id = Guid.NewGuid().ToString("N"), Leader = actor };
                        team.Members[actor] = NewMember(actor, true); _teams[team.Id] = team;
                        leader = true;
                    }
                    if (!leader || team.Members.Count >= Capacity) { error = "Somente o líder pode convidar, respeitando o limite de cinco integrantes."; break; }
                    team.Members[target] = NewMember(target, false); break;
                case "join":
                    if (team == null || team.Members[actor].Accepted) { error = "Convite de grupo não encontrado ou expirado."; break; }
                    team.Members[actor].Accepted = true; team.Members[actor].InvitedUntil = 0; break;
                case "reject":
                    target = string.IsNullOrEmpty(target) ? actor : target;
                    if (team == null || (!leader && target != actor) || !team.Members.TryGetValue(target, out Member invited) || invited.Accepted)
                    { error = "Convite não encontrado ou sem permissão para cancelá-lo."; break; }
                    team.Members.Remove(target); break;
                case "elect":
                    if (!leader || !team.Members.TryGetValue(target ?? "", out Member elected) || !elected.Accepted)
                    { error = "Somente o líder pode transferir a liderança a um membro confirmado."; break; }
                    team.Leader = target; break;
                case "kick":
                    if (!leader || target == actor || !team.Members.ContainsKey(target ?? ""))
                    { error = "Somente o líder pode remover outro integrante."; break; }
                    team.Members.Remove(target); break;
                case "leave":
                    if (team == null) break;
                    team.Members.Remove(actor);
                    if (leader)
                    {
                        string next = team.Members.FirstOrDefault(m => m.Value.Accepted).Key;
                        if (next == null) _teams.Remove(team.Id); else team.Leader = next;
                    }
                    break;
                default: error = "Comando de grupo inválido."; break;
            }
            if (error != null) { _teams = Json.Read<Dictionary<string, Team>>(before); return false; }
            if (_path == null || !SafeSave.WriteAtomic(_path, Json.WriteToBytes(_teams, false), "party-store"))
            { _teams = Json.Read<Dictionary<string, Team>>(before); error = "Não foi possível salvar o grupo. Tente novamente."; return false; }
            if (team != null) ids.UnionWith(team.Members.Keys);
            affected = ids.ToArray(); return true;
        }
    }
}
