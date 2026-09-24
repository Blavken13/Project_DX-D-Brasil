using MsgPack;

namespace Messages;

public struct RenameClan
{
	public const uint TypeCode = 36510u;

	public string ClanName;

	public static void Pack(Packer packer, RenameClan val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(36510u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.ClanName == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.ClanName);
		}
	}

	public static RenameClan Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new RenameClan
		{
			ClanName = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<RenameClan ClanName=" + ClanName + ">";
	}
}
