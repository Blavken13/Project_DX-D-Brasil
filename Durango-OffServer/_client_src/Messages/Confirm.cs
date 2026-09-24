using MsgPack;

namespace Messages;

public struct Confirm
{
	public const uint TypeCode = 3649u;

	public bool Confirmation;

	public static void Pack(Packer packer, Confirm val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(3649u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		packer.Pack(val.Confirmation);
	}

	public static Confirm Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new Confirm
		{
			Confirmation = unpacker.LastReadData.AsBoolean()
		};
	}

	public override string ToString()
	{
		return $"<Confirm Confirmation={Confirmation}>";
	}
}
