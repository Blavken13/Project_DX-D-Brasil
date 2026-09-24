using MsgPack;

namespace Messages;

public struct SetClanEmblem
{
	public const uint TypeCode = 3695u;

	public byte[] Emblem;

	public static void Pack(Packer packer, SetClanEmblem val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(3695u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.Emblem == null)
		{
			packer.PackBinary(new byte[0]);
		}
		else
		{
			packer.PackBinary(val.Emblem);
		}
	}

	public static SetClanEmblem Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new SetClanEmblem
		{
			Emblem = unpacker.LastReadData.AsBinary()
		};
	}

	public override string ToString()
	{
		return $"<SetClanEmblem Emblem={Emblem}>";
	}
}
