using MsgPack;

namespace Messages;

public struct TravelToStableRegion
{
	public const uint TypeCode = 20321235u;

	public string RegionId;

	public static void Pack(Packer packer, TravelToStableRegion val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(20321235u);
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

	public static TravelToStableRegion Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new TravelToStableRegion
		{
			RegionId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<TravelToStableRegion RegionId=" + RegionId + ">";
	}
}
