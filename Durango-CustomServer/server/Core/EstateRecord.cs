using System.Collections.Generic;
using Newtonsoft.Json;

namespace Durango.Online;

/// <summary>สถานะที่ดินที่เซฟใน .world — แปลงเป็น EstateLicense ตอนส่งให้เกม</summary>
public class EstateRecord
{
	[JsonProperty("type")]
	public int Type;

	[JsonProperty("owner_id")]
	public string OwnerId;

	[JsonProperty("activated_at")]
	public double ActivatedAt;

	[JsonProperty("expires_at")]
	public double? ExpiresAt;

	[JsonProperty("size")]
	public int Size;

	[JsonProperty("largest_size")]
	public int LargestSize;

	[JsonProperty("region_id")]
	public string RegionId;

	[JsonProperty("tile_x")]
	public int TileX;

	[JsonProperty("tile_y")]
	public int TileY;

	[JsonProperty("cells")]
	public List<string> Cells = new();

	[JsonProperty("access_for_others")]
	public int? AccessForOthers;

	[JsonProperty("access_for_friends", NullValueHandling = NullValueHandling.Ignore)]
	public Dictionary<Shared.Player.FriendType, Shared.Estate.AccessRights> AccessForFriends;

	[JsonProperty("access_for_clan_members", NullValueHandling = NullValueHandling.Ignore)]
	public Dictionary<int, Shared.Estate.AccessRights> AccessForClanMembers;

	public Messages.AccessRights ToAccessRights()
	{
		// O cliente escreve diretamente nestes mapas ao trocar de aba ou fechar a tela.
		// Saves antigos nao possuem os mapas; enviar defaults completos evita o null.
		var friends = new Dictionary<Shared.Player.FriendType, Shared.Estate.AccessRights>
		{
			[Shared.Player.FriendType.JustFriend] = Shared.Estate.AccessRights.None,
			[Shared.Player.FriendType.BestFriend] = Shared.Estate.AccessRights.None
		};
		if (AccessForFriends != null)
			foreach (var pair in AccessForFriends)
				if (friends.ContainsKey(pair.Key)) friends[pair.Key] = pair.Value;
		return new Messages.AccessRights
		{
			ForOthers = (Shared.Estate.AccessRights)(AccessForOthers ?? 0),
			ForFriends = friends,
			ForClanMembers = AccessForClanMembers == null
				? new Dictionary<int, Shared.Estate.AccessRights>()
				: new Dictionary<int, Shared.Estate.AccessRights>(AccessForClanMembers)
		};
	}

	public void SetAccessRights(Messages.AccessRights rights)
	{
		AccessForOthers = (int)rights.ForOthers;
		AccessForFriends = rights.ForFriends == null
			? null : new Dictionary<Shared.Player.FriendType, Shared.Estate.AccessRights>(rights.ForFriends);
		AccessForClanMembers = rights.ForClanMembers == null
			? null : new Dictionary<int, Shared.Estate.AccessRights>(rights.ForClanMembers);
	}

	public bool Allows(string playerId, Shared.Estate.AccessRights required)
	{
		if (required == Shared.Estate.AccessRights.None) return false;
		var friendType = FriendStore.GetFriendType(OwnerId, playerId);
		var rights = friendType == Shared.Player.FriendType.Invalid
			? (Shared.Estate.AccessRights)(AccessForOthers ?? 0)
			: AccessForFriends?.GetValueOrDefault(friendType) ?? Shared.Estate.AccessRights.None;
		return (rights & required) == required;
	}
}
