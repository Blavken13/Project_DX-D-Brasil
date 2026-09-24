using MsgPack;

namespace Messages;

public struct GuideProgress
{
	public const uint TypeCode = 702u;

	public byte Seq;

	public static void Pack(Packer packer, GuideProgress val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(702u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		packer.Pack(val.Seq);
	}

	public static GuideProgress Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new GuideProgress
		{
			Seq = unpacker.LastReadData.AsByte()
		};
	}

	public override string ToString()
	{
		return $"<GuideProgress Seq={Seq}>";
	}
}
