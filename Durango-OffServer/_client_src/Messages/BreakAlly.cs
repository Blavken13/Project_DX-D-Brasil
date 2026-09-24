using MsgPack;

namespace Messages;

public struct BreakAlly
{
	public const uint TypeCode = 9138751u;

	public string ClanId;

	public static void Pack(Packer packer, BreakAlly val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(9138751u);
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

	public static BreakAlly Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new BreakAlly
		{
			ClanId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<BreakAlly ClanId=" + ClanId + ">";
	}
}
