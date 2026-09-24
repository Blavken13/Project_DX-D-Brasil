using MsgPack;

namespace Messages;

public struct GetRegion
{
	public const uint TypeCode = 2120u;

	public string RegionId;

	public static void Pack(Packer packer, GetRegion val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(2120u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.RegionId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.RegionId);
		}
	}

	public static GetRegion Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new GetRegion
		{
			RegionId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<GetRegion RegionId=" + RegionId + ">";
	}
}
