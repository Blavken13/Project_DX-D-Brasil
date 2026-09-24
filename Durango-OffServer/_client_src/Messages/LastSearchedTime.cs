using MsgPack;

namespace Messages;

public struct LastSearchedTime
{
	public const uint TypeCode = 907u;

	public double SearchedAt;

	public static void Pack(Packer packer, LastSearchedTime val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(907u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		packer.Pack(val.SearchedAt);
	}

	public static LastSearchedTime Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new LastSearchedTime
		{
			SearchedAt = unpacker.LastReadData.AsDouble()
		};
	}

	public override string ToString()
	{
		return $"<LastSearchedTime SearchedAt={SearchedAt}>";
	}
}
