using MsgPack;

namespace Messages;

public struct Timer
{
	public const uint TypeCode = 1134u;

	public float Duration;

	public static void Pack(Packer packer, Timer val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(1134u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		packer.Pack(val.Duration);
	}

	public static Timer Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new Timer
		{
			Duration = unpacker.LastReadData.AsSingle()
		};
	}

	public override string ToString()
	{
		return $"<Timer Duration={Duration}>";
	}
}
