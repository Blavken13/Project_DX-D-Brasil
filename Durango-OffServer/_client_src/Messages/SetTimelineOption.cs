using MsgPack;

namespace Messages;

public struct SetTimelineOption
{
	public const uint TypeCode = 81234528u;

	public bool EstateNotification;

	public static void Pack(Packer packer, SetTimelineOption val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(81234528u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		packer.Pack(val.EstateNotification);
	}

	public static SetTimelineOption Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new SetTimelineOption
		{
			EstateNotification = unpacker.LastReadData.AsBoolean()
		};
	}

	public override string ToString()
	{
		return $"<SetTimelineOption EstateNotification={EstateNotification}>";
	}
}
