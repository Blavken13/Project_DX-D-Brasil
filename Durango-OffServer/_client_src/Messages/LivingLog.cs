using MsgPack;

namespace Messages;

public struct LivingLog
{
	public const uint TypeCode = 2059u;

	public string Log;

	public static void Pack(Packer packer, LivingLog val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(2059u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.Log == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.Log);
		}
	}

	public static LivingLog Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new LivingLog
		{
			Log = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<LivingLog Log=" + Log + ">";
	}
}
