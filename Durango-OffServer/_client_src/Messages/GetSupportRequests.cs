using MsgPack;

namespace Messages;

public struct GetSupportRequests
{
	public const uint TypeCode = 2347809u;

	public bool RequestedUntilsOnly;

	public static void Pack(Packer packer, GetSupportRequests val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(2347809u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		packer.Pack(val.RequestedUntilsOnly);
	}

	public static GetSupportRequests Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new GetSupportRequests
		{
			RequestedUntilsOnly = unpacker.LastReadData.AsBoolean()
		};
	}

	public override string ToString()
	{
		return $"<GetSupportRequests RequestedUntilsOnly={RequestedUntilsOnly}>";
	}
}
