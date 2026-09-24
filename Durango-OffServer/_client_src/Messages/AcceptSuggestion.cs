using MsgPack;

namespace Messages;

public struct AcceptSuggestion
{
	public const uint TypeCode = 9138749u;

	public string ClanId;

	public static void Pack(Packer packer, AcceptSuggestion val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(9138749u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.ClanId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.ClanId);
		}
	}

	public static AcceptSuggestion Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new AcceptSuggestion
		{
			ClanId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<AcceptSuggestion ClanId=" + ClanId + ">";
	}
}
