using MsgPack;

namespace Messages;

public struct ElectPartyLeader
{
	public const uint TypeCode = 20010u;

	public string MemberEntityId;

	public static void Pack(Packer packer, ElectPartyLeader val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(20010u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.MemberEntityId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.MemberEntityId);
		}
	}

	public static ElectPartyLeader Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new ElectPartyLeader
		{
			MemberEntityId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<ElectPartyLeader MemberEntityId=" + MemberEntityId + ">";
	}
}
