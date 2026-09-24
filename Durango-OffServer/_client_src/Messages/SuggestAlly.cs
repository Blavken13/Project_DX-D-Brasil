using MsgPack;

namespace Messages;

public struct SuggestAlly
{
	public const uint TypeCode = 9138747u;

	public string ClanId;

	public static void Pack(Packer packer, SuggestAlly val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(9138747u);
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

	public static SuggestAlly Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new SuggestAlly
		{
			ClanId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<SuggestAlly ClanId=" + ClanId + ">";
	}
}
