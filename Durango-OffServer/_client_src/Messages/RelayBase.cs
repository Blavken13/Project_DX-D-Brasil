using MsgPack;

namespace Messages;

public struct RelayBase
{
	public double SentAt;

	public static void Pack(Packer packer, RelayBase val, bool hint = false)
	{
		packer.PackArrayHeader(1);
		packer.Pack(val.SentAt);
	}

	public static RelayBase Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new RelayBase
		{
			SentAt = unpacker.LastReadData.AsDouble()
		};
	}

	public override string ToString()
	{
		return $"<RelayBase SentAt={SentAt}>";
	}
}
